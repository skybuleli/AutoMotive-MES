using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Sync;

namespace MesAdmin.Api.Features.Sync;

// ═══════════════════════════════════════════
//  同步端点组
// ═══════════════════════════════════════════

public sealed class SyncGroup : Group
{
    public SyncGroup()
    {
        Configure("api/v1/sync", ep =>
        {
            ep.Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader,
                     MesRoles.QualityEngineer, MesRoles.WarehouseClerk);
        });
    }
}

// ═══════════════════════════════════════════
//  DTO 定义
// ═══════════════════════════════════════════

public sealed record OfflineOperationBody(
    string TerminalId,
    string OperationType,
    string Payload,
    string EntityType,
    string? EntityId,
    DateTimeOffset? OperationTimestamp);

public sealed record UploadSyncBatchBody(List<OfflineOperationBody> Operations);

public sealed record UploadSyncBatchResponse(
    int TotalReceived,
    int SuccessCount,
    int ConflictCount,
    int FailedCount,
    List<SyncErrorItem>? Errors);

public sealed record SyncErrorItem(int Index, string OperationType, string ErrorMessage);

public sealed record SyncStatusResponse(
    OfflineSyncStats Stats,
    long ChannelBacklog,
    long ChannelProcessed,
    long ChannelConflicts);

public sealed record SyncConflictItem(
    string Id,
    string TerminalId,
    string OperationType,
    string EntityType,
    string? EntityId,
    string Payload,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

public sealed record ResolveConflictBody(string Resolution);

// ═══════════════════════════════════════════
//  POST /api/v1/sync/upload
// ═══════════════════════════════════════════

public sealed class UploadSyncBatchEndpoint : Endpoint<UploadSyncBatchBody, UploadSyncBatchResponse>
{
    public override void Configure()
    {
        Post("/upload");
        Group<SyncGroup>();
        Summary(s => s.Summary = "终端批量上传离线期间产生的操作记录");
    }

    public override async Task HandleAsync(UploadSyncBatchBody req, CancellationToken ct)
    {
        if (req.Operations.Count == 0)
        {
            await Send.OkAsync(new UploadSyncBatchResponse(0, 0, 0, 0, null), ct);
            return;
        }

        var repo = Resolve<IOfflineSyncRepository>();
        var syncService = Resolve<OfflineSyncService>();

        var records = req.Operations.Select(op =>
        {
            try
            {
                return OfflineSyncRecord.Create(
                    op.TerminalId, op.OperationType, op.Payload,
                    op.EntityType, op.EntityId, op.OperationTimestamp);
            }
            catch { return null; }
        }).ToList();

        var validRecords = records.Where(r => r is not null).Cast<OfflineSyncRecord>().ToList();

        if (validRecords.Count == 0)
        {
            await Send.OkAsync(new UploadSyncBatchResponse(req.Operations.Count, 0, 0, 0,
                [new SyncErrorItem(0, "All", "所有操作均参数无效")]), ct);
            return;
        }

        await repo.AddRangeAsync(validRecords, ct);
        await syncService.EnqueueBatchAsync(validRecords, ct);

        await Send.OkAsync(new UploadSyncBatchResponse(
            req.Operations.Count, validRecords.Count, 0,
            req.Operations.Count - validRecords.Count, null), ct);
    }
}

public sealed class UploadSyncBatchValidator : Validator<UploadSyncBatchBody>
{
    public UploadSyncBatchValidator()
    {
        RuleFor(x => x.Operations).NotEmpty().WithMessage("操作列表不能为空");
        RuleForEach(x => x.Operations).ChildRules(op =>
        {
            op.RuleFor(o => o.TerminalId).NotEmpty().WithMessage("终端标识不能为空");
            op.RuleFor(o => o.OperationType).NotEmpty().WithMessage("操作类型不能为空");
            op.RuleFor(o => o.Payload).NotEmpty().WithMessage("操作载荷不能为空");
            op.RuleFor(o => o.EntityType).NotEmpty().WithMessage("实体类型不能为空");
        });
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/sync/status
// ═══════════════════════════════════════════

public sealed class SyncStatusEndpoint : EndpointWithoutRequest<SyncStatusResponse>
{
    public override void Configure()
    {
        Get("/status");
        Group<SyncGroup>();
        Summary(s => s.Summary = "查询离线同步状态概览");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IOfflineSyncRepository>();
        var syncService = Resolve<OfflineSyncService>();
        var stats = await repo.GetStatsAsync(ct);
        var health = syncService.Health;

        await Send.OkAsync(new SyncStatusResponse(
            stats, health.Backlog, health.Processed, health.Conflicts), ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/sync/pending
// ═══════════════════════════════════════════

public sealed class PendingSyncEndpoint : EndpointWithoutRequest<List<SyncConflictItem>>
{
    public override void Configure()
    {
        Get("/pending");
        Group<SyncGroup>();
        Summary(s => s.Summary = "查询待同步/冲突的离线记录");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IOfflineSyncRepository>();
        var terminalId = Query<string?>("terminalId", isRequired: false);
        var status = Query<string?>("status", isRequired: false);

        List<OfflineSyncRecord> records;
        if (terminalId is not null)
            records = await repo.GetByTerminalAsync(terminalId, status, 200, ct);
        else if (status == OfflineSyncStatus.Conflict)
            records = await repo.GetConflictsAsync(ct: ct);
        else
            records = await repo.GetPendingAsync(200, ct);

        var items = records.Select(r => new SyncConflictItem(
            r.Id.ToString(), r.TerminalId, r.OperationType,
            r.EntityType, r.EntityId, r.Payload,
            r.ErrorMessage, r.CreatedAt)).ToList();

        await Send.OkAsync(items, ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/sync/resolve/{id}
// ═══════════════════════════════════════════

public sealed class ResolveConflictEndpoint : Endpoint<ResolveConflictBody, SyncConflictItem>
{
    public override void Configure()
    {
        Post("/resolve/{id}");
        Group<SyncGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "解决离线同步冲突记录");
    }

    public override async Task HandleAsync(ResolveConflictBody req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var recordId))
        {
            AddError("id", "无效的记录 Id");
            ThrowIfAnyErrors();
        }

        if (req.Resolution is not ("use_local" or "use_server" or "manual"))
        {
            AddError("resolution", "冲突解决方案必须为 use_local / use_server / manual");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IOfflineSyncRepository>();
        var record = await repo.GetByIdAsync(recordId, ct);
        if (record is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        record.ConflictResolution = req.Resolution;
        record.Status = req.Resolution == "use_server"
            ? OfflineSyncStatus.Synced
            : OfflineSyncStatus.Pending;
        record.LastAttemptAt = DateTimeOffset.UtcNow;

        if (req.Resolution == "use_server")
            record.SyncedAt = DateTimeOffset.UtcNow;

        await repo.UpdateRangeAsync([record], ct);

        await Send.OkAsync(new SyncConflictItem(
            record.Id.ToString(), record.TerminalId, record.OperationType,
            record.EntityType, record.EntityId, record.Payload,
            record.ErrorMessage, record.CreatedAt), ct);
    }
}

public sealed class ResolveConflictValidator : Validator<ResolveConflictBody>
{
    public ResolveConflictValidator()
    {
        RuleFor(x => x.Resolution).NotEmpty().WithMessage("冲突解决方案不能为空");
    }
}
