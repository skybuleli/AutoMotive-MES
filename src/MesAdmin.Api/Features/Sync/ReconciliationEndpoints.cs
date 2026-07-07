using FastEndpoints;
using FluentValidation;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Sync;

namespace MesAdmin.Api.Features.Sync;

// ═══════════════════════════════════════════
//  调停端点组
// ═══════════════════════════════════════════

public sealed class ReconciliationGroup : Group
{
    public ReconciliationGroup()
    {
        Configure("api/v1/sync/reconcile", ep =>
        {
            ep.Roles(MesRoles.ProductionManager, MesRoles.QualityEngineer);
        });
    }
}

// ═══════════════════════════════════════════
//  DTO 定义
// ═══════════════════════════════════════════

public sealed record ReconciliationResponse(
    string TerminalId,
    int Resolved,
    int Failed,
    int Conflicts);

public sealed record ReconciliationStatusResponse(
    int PendingCount,
    int SyncedCount,
    int ConflictCount,
    int FailedCount,
    List<TerminalStatus> Terminals);

public sealed record TerminalStatus(
    string TerminalId,
    int PendingCount,
    int ConflictCount);

// ═══════════════════════════════════════════
//  POST /api/v1/sync/reconcile/{terminalId} — 手动触发终端的调停
// ═══════════════════════════════════════════

public sealed class ReconcileTerminalEndpoint : EndpointWithoutRequest<ReconciliationResponse>
{
    public override void Configure()
    {
        Post("/{terminalId}");
        Group<ReconciliationGroup>();
        Summary(s => s.Summary = "手动触发指定终端的离线记录调停与重放");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var terminalId = Route<string>("terminalId")!;

        var replayService = Resolve<OfflineReplayService>();
        var result = await replayService.ReplayTerminalBatchAsync(terminalId, ct);

        await Send.OkAsync(new ReconciliationResponse(
            terminalId, result.Accepted, result.Stale, result.Conflicts), ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/sync/reconcile — 全局调停所有终端
// ═══════════════════════════════════════════

public sealed class ReconcileAllEndpoint : EndpointWithoutRequest<List<ReconciliationResponse>>
{
    public override void Configure()
    {
        Post("/");
        Group<ReconciliationGroup>();
        Summary(s => s.Summary = "全局调停：对所有终端的离线记录执行冲突解决和重放");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IOfflineSyncRepository>();
        var stats = await repo.GetStatsAsync(ct);

        // 获取所有待处理记录的终端
        var pending = await repo.GetPendingAsync(1000, ct);
        var terminalIds = pending
            .Select(r => r.TerminalId)
            .Distinct()
            .ToList();

        if (terminalIds.Count == 0)
        {
            await Send.OkAsync([], ct);
            return;
        }

        var results = new List<ReconciliationResponse>();
        var replayService = Resolve<OfflineReplayService>();

        foreach (var terminalId in terminalIds)
        {
            var result = await replayService.ReplayTerminalBatchAsync(terminalId, ct);
            results.Add(new ReconciliationResponse(
                terminalId, result.Accepted, result.Stale, result.Conflicts));
        }

        await Send.OkAsync(results, ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/sync/reconcile/status — 调停状态概览
// ═══════════════════════════════════════════

public sealed class ReconciliationStatusEndpoint : EndpointWithoutRequest<ReconciliationStatusResponse>
{
    public override void Configure()
    {
        Get("/status");
        Group<ReconciliationGroup>();
        Summary(s => s.Summary = "查询调停状态概览");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IOfflineSyncRepository>();
        var stats = await repo.GetStatsAsync(ct);

        // 获取各终端的待处理和冲突数量
        var pending = await repo.GetPendingAsync(1000, ct);
        var conflicts = await repo.GetConflictsAsync(ct: ct);

        var terminalGroups = pending
            .GroupBy(r => r.TerminalId)
            .Select(g =>
            {
                var terminalConflicts = conflicts.Count(c => c.TerminalId == g.Key);
                return new TerminalStatus(g.Key, g.Count(), terminalConflicts);
            })
            .ToList();

        // 添加仅有冲突记录的终端
        foreach (var conflict in conflicts)
        {
            if (!terminalGroups.Any(t => t.TerminalId == conflict.TerminalId))
            {
                terminalGroups.Add(new TerminalStatus(conflict.TerminalId, 0, 1));
            }
        }

        await Send.OkAsync(new ReconciliationStatusResponse(
            stats.PendingCount,
            stats.SyncedCount,
            stats.ConflictCount,
            stats.FailedCount,
            terminalGroups), ct);
    }
}
