using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ZLogger;

namespace MesAdmin.Infrastructure.Sync;

/// <summary>
/// Saga 状态合并与冲突解决服务（T4.5）。
/// 当终端断网后重新上线时，检测离线操作与 Saga 当前状态的冲突，
/// 并根据预定策略自动解决（LastWriteWins / ServerWins / ClientWins）。
/// </summary>
public sealed class SagaReconciliationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SagaReconciliationService> _logger;

    public SagaReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<SagaReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 对指定终端的离线记录执行冲突检测和自动解决。
    /// 返回 (resolved, failed, conflicts) 计数。
    /// </summary>
    public async Task<ReconciliationResult> ReconcileTerminalAsync(
        string terminalId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        var pending = await repo.GetByTerminalAsync(terminalId, limit: 200, ct: ct);

        if (pending.Count == 0)
            return new ReconciliationResult(0, 0, 0);

        var resolved = 0;
        var failed = 0;
        var conflicts = 0;

        foreach (var record in pending)
        {
            if (record.Status == OfflineSyncStatus.Synced)
                continue;

            var result = await ResolveConflictAsync(record, ct);
            switch (result)
            {
                case ConflictResolution.Accepted:
                    record.Status = OfflineSyncStatus.Synced;
                    record.SyncedAt = DateTimeOffset.UtcNow;
                    record.ErrorMessage = null;
                    resolved++;
                    break;
                case ConflictResolution.RejectedStale:
                    record.Status = OfflineSyncStatus.Synced;
                    record.SyncedAt = DateTimeOffset.UtcNow;
                    record.ErrorMessage = "Stale: superseded by Saga progress";
                    resolved++;
                    break;
                case ConflictResolution.Conflict:
                    record.Status = OfflineSyncStatus.Conflict;
                    record.ErrorMessage = "Conflict requires manual resolution";
                    conflicts++;
                    break;
                case ConflictResolution.Failed:
                    record.Status = OfflineSyncStatus.Failed;
                    failed++;
                    break;
            }

            record.LastAttemptAt = DateTimeOffset.UtcNow;
        }

        await repo.UpdateRangeAsync(pending, ct);

        _logger.ZLogInformation(
            $"[SagaReconciliation] 终端 {terminalId}: 已解决 {resolved}, 失败 {failed}, 冲突 {conflicts}");

        return new ReconciliationResult(resolved, failed, conflicts);
    }

    /// <summary>
    /// 检测单条离线记录是否存在 Saga 状态冲突。
    /// 根据操作类型和当前 Saga 进展判定。
    /// </summary>
    public async Task<ConflictResolution> ResolveConflictAsync(
        OfflineSyncRecord record, CancellationToken ct = default)
    {
        // 非 Saga 操作：直接接受
        if (record.EntityType is not ("ProductionOrder" or "WorkOrderOperation"))
            return ConflictResolution.Accepted;

        if (record.EntityId is null)
            return ConflictResolution.Accepted;

        if (!Ulid.TryParse(record.EntityId, out var orderId))
            return ConflictResolution.Accepted;

        using var scope = _scopeFactory.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();

        // 1. 检查工单存在性和状态
        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            // 工单不存在 → 操作失效
            _logger.ZLogWarning($"[SagaReconciliation] 工单 {orderId} 不存在，操作 {record.OperationType} 已失效");
            return ConflictResolution.RejectedStale;
        }

        // 已完成的工单不再接受离线操作
        if (order.Status is OrderStatus.Completed or OrderStatus.Closed)
        {
            _logger.ZLogInformation($"[SagaReconciliation] 工单 {order.OrderNumber} 已 {order.Status}，离线操作已过时");
            return ConflictResolution.RejectedStale;
        }

        // 2. 根据操作类型检测冲突
        return record.OperationType switch
        {
            "CompleteOperation" => await ResolveCompleteOperationAsync(record, order, opRepo, ct),
            "CompleteStation" => await ResolveCompleteStationAsync(record, order, opRepo, ct),
            "ReportMeasurement" => await ResolveMeasurementAsync(record, order, ct),
            "BindMaterial" => await ResolveMaterialBindingAsync(record, order, ct),
            _ => ConflictResolution.Accepted,
        };
    }

    private async Task<ConflictResolution> ResolveCompleteOperationAsync(
        OfflineSyncRecord record, ProductionOrder order,
        IWorkOrderOperationRepository opRepo, CancellationToken ct)
    {
        // 从 Payload 解析工序序号
        var seq = TryParseSequence(record.Payload);
        if (!seq.HasValue)
            return ConflictResolution.Accepted;

        var existing = await opRepo.GetByOrderAndSequenceAsync(order.Id, seq.Value, ct);
        if (existing is null)
        {
            // 工序不存在 — 使用离线数据（终端可能正在创建新记录，但现有系统不删除工序，所以视为失败）
            return ConflictResolution.Failed;
        }

        if (existing.Status == OperationStatus.Completed)
        {
            // Saga 已经执行了这道工序，接受终端的状态并标记为已同步
            // 使用 Saga 已有的完成时间，保留 Saga 版本
            _logger.ZLogInformation(
                $"[SagaReconciliation] 工序 {seq} 已被 Saga 完成，离线操作同步为已确认");
            return ConflictResolution.Accepted;
        }

        if (existing.Status == OperationStatus.InProgress)
        {
            // 工序正在由 Saga 处理中，离线操作已过时（Saga 稍后会完成它）
            return ConflictResolution.RejectedStale;
        }

        // 工序 Pending — 离线操作是最新的，接受并应用
        // 实际应用由 DispatchOperation 完成
        return ConflictResolution.Accepted;
    }

    private async Task<ConflictResolution> ResolveCompleteStationAsync(
        OfflineSyncRecord record, ProductionOrder order,
        IWorkOrderOperationRepository opRepo, CancellationToken ct)
    {
        // 站完工：检查该站最后一道工序在 Saga 中是否已完成
        // Payload 格式: {"station":3,"fromSeq":6,"toSeq":10}
        var station = TryParseStation(record.Payload);
        if (!station.HasValue)
            return ConflictResolution.Accepted;

        // 通过查询该站关键工序判断 Saga 进展
        // 检查站内中间工序（如站3的 seq 6），Saga AtLeastOnce 执行后该工序即完成
        var midSeq = station.Value switch
        {
            2 => 4, 3 => 8, 4 => 17, 5 => 26, 6 => 29, 7 => 31, _ => 1
        };

        var midOp = await opRepo.GetByOrderAndSequenceAsync(order.Id, midSeq, ct);
        if (midOp?.Status == OperationStatus.Completed)
        {
            _logger.ZLogInformation(
                $"[SagaReconciliation] 站 {station} 已被 Saga 处理，终端站完工操作过时");
            return ConflictResolution.RejectedStale;
        }

        return ConflictResolution.Accepted;
    }

    private Task<ConflictResolution> ResolveMeasurementAsync(
        OfflineSyncRecord record, ProductionOrder order, CancellationToken ct)
    {
        // 测量值：始终接受，Saga 不关心测量值
        return Task.FromResult(ConflictResolution.Accepted);
    }

    private Task<ConflictResolution> ResolveMaterialBindingAsync(
        OfflineSyncRecord record, ProductionOrder order, CancellationToken ct)
    {
        // 物料绑定：如果工单已完成则拒绝，否则接受
        // 重复绑定由 DB 唯一约束保护
        return Task.FromResult(ConflictResolution.Accepted);
    }

    private static int? TryParseSequence(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("sequence", out var seqEl) &&
                seqEl.ValueKind == JsonValueKind.Number)
            {
                return seqEl.GetInt32();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryParseStation(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("station", out var stEl) &&
                stEl.ValueKind == JsonValueKind.Number)
            {
                return stEl.GetInt32();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>冲突解决结果</summary>
public enum ConflictResolution
{
    /// <summary>接受离线操作，与服务器状态一致</summary>
    Accepted,
    /// <summary>离线操作已过时（Saga 已推进到更后工站）</summary>
    RejectedStale,
    /// <summary>存在冲突，需要人工介入</summary>
    Conflict,
    /// <summary>处理失败</summary>
    Failed,
}

/// <summary>批量调停结果</summary>
public sealed record ReconciliationResult(int Resolved, int Failed, int Conflicts);
