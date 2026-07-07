using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;
using System.Text.Json;

namespace MesAdmin.Infrastructure.Sync;

/// <summary>
/// 离线操作重放服务（T4.5）。
/// 在 OfflineSyncService 的 DispatchOperationAsync 中调用，
/// 根据 OperationType 将离线操作路由到对应的业务 Handler。
/// 重放前通过 SagaReconciliationService 检测冲突。
/// </summary>
public sealed class OfflineReplayService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SagaReconciliationService _reconciliationService;
    private readonly ILogger<OfflineReplayService> _logger;

    public OfflineReplayService(
        IServiceScopeFactory scopeFactory,
        SagaReconciliationService reconciliationService,
        ILogger<OfflineReplayService> logger)
    {
        _scopeFactory = scopeFactory;
        _reconciliationService = reconciliationService;
        _logger = logger;
    }

    /// <summary>
    /// 重放单条离线操作，返回操作结果。
    /// 先检查冲突，再执行实际业务操作。
    /// </summary>
    public async Task<ReplayResult> ReplayAsync(
        OfflineSyncRecord record, CancellationToken ct = default)
    {
        var conflictResult = await _reconciliationService.ResolveConflictAsync(record, ct);
        switch (conflictResult)
        {
            case ConflictResolution.RejectedStale:
                _logger.ZLogInformation($"[Replay] 跳过过时操作：{record.OperationType} | {record.EntityType}:{record.EntityId}");
                return ReplayResult.Accepted();
            case ConflictResolution.Conflict:
                _logger.ZLogWarning($"[Replay] 冲突：{record.OperationType} | {record.EntityType}:{record.EntityId}");
                return ReplayResult.Conflict("Saga 状态冲突，需要人工解决");
            case ConflictResolution.Failed:
                _logger.ZLogError($"[Replay] 处理失败：{record.OperationType} | {record.EntityType}:{record.EntityId}");
                return ReplayResult.Failure("处理失败");
            case ConflictResolution.Accepted:
                break;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var payload = record.Payload;

            return record.OperationType switch
            {
                "CompleteOperation" => await ReplayCompleteOperationAsync(scope, payload, ct),
                "BindMaterial" => await ReplayBindMaterialAsync(scope, payload, ct),
                _ => ReplayResult.Accepted(),
            };
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"[Replay] 重放异常：{record.OperationType} | {record.Id}");
            return ReplayResult.Failure(ex.Message);
        }
    }

    private async Task<ReplayResult> ReplayCompleteOperationAsync(
        IServiceScope scope, string payload, CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<CompleteOpPayload>(payload);
        if (data is null || data.Sequence <= 0)
            return ReplayResult.Failure("无效的工序参数");

        if (!Ulid.TryParse(data.OrderId, out var orderId))
            return ReplayResult.Failure("无效的工单 Id");

        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var existingOp = await opRepo.GetByOrderAndSequenceAsync(orderId, data.Sequence, ct);

        if (existingOp is null)
            return ReplayResult.Failure($"工序 {data.Sequence} 不存在");

        if (existingOp.Status == OperationStatus.Completed)
        {
            _logger.ZLogInformation($"[Replay] 工序 {data.Sequence} 已完工，跳过");
            return ReplayResult.Accepted();
        }

        var now = data.CompletedAt ?? DateTimeOffset.UtcNow;
        if (existingOp.Status == OperationStatus.Pending)
            existingOp.Start(data.OperatorId ?? "OFFLINE", data.EquipmentId ?? "OFFLINE", now);

        existingOp.Complete(now);
        await opRepo.SaveChangesAsync(ct);

        _logger.ZLogInformation($"[Replay] ✅ 工序 {data.Sequence} 重放完成");
        return ReplayResult.Accepted();
    }

    private async Task<ReplayResult> ReplayBindMaterialAsync(
        IServiceScope scope, string payload, CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<BindMaterialPayload>(payload);
        if (data is null || string.IsNullOrWhiteSpace(data.MaterialCode))
            return ReplayResult.Failure("无效的物料绑定参数");

        if (!Ulid.TryParse(data.OrderId, out var orderId))
            return ReplayResult.Failure("无效的工单 Id");

        // 从批次号反查 MaterialBatch
        var batchRepo = scope.ServiceProvider.GetRequiredService<IMaterialBatchRepository>();
        var batch = await batchRepo.GetByBatchNumberAsync(data.BatchNumber, ct);
        if (batch is null)
        {
            _logger.ZLogWarning($"[Replay] 物料批次 {data.BatchNumber} 不存在，跳过绑定");
            return ReplayResult.Accepted();
        }

        var bindingRepo = scope.ServiceProvider.GetRequiredService<IMaterialBindingRepository>();
        var existingBindings = await bindingRepo.GetByOrderIdAsync(orderId, ct);
        var existing = existingBindings?.FirstOrDefault(b =>
            b.MaterialCode == data.MaterialCode && b.BatchNumber == data.BatchNumber);

        if (existing is not null)
        {
            _logger.ZLogInformation($"[Replay] 物料绑定 {data.MaterialCode}/{data.BatchNumber} 已存在，跳过");
            return ReplayResult.Accepted();
        }

        var binding = MaterialBinding.Create(
            orderId,
            batch.Id,
            data.MaterialCode,
            data.BatchNumber,
            data.ProductSerial ?? "OFFLINE-SERIAL",
            1.0,
            true,
            data.OperatorId ?? "OFFLINE");

        await bindingRepo.AddAsync(binding, ct);
        await bindingRepo.SaveChangesAsync(ct);

        _logger.ZLogInformation($"[Replay] ✅ 物料绑定 {data.MaterialCode}/{data.BatchNumber} 完成");
        return ReplayResult.Accepted();
    }

    public async Task<BatchReplayResult> ReplayTerminalBatchAsync(
        string terminalId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        var records = await repo.GetByTerminalAsync(terminalId, limit: 200, ct: ct);
        var pendingRecords = records
            .Where(r => OfflineSyncStatus.Retryable.Contains(r.Status))
            .OrderBy(r => r.CreatedAt)
            .ToList();

        if (pendingRecords.Count == 0)
            return new BatchReplayResult(0, 0, 0);

        var accepted = 0;
        var stale = 0;
        var conflictCount = 0;

        foreach (var record in pendingRecords)
        {
            var result = await ReplayAsync(record, ct);
            if (result.IsConflict)
            {
                record.Status = OfflineSyncStatus.Conflict;
                record.ErrorMessage = result.ErrorMessage;
                conflictCount++;
            }
            else if (result.IsSuccess)
            {
                record.Status = OfflineSyncStatus.Synced;
                record.SyncedAt = DateTimeOffset.UtcNow;
                record.ErrorMessage = null;
                accepted++;
            }
            else
            {
                record.Status = OfflineSyncStatus.Failed;
                record.ErrorMessage = result.ErrorMessage;
                stale++;
            }
            record.LastAttemptAt = DateTimeOffset.UtcNow;
        }

        await repo.UpdateRangeAsync(pendingRecords, ct);

        _logger.ZLogInformation(
            $"[Replay] 终端 {terminalId} 批量重放：接受 {accepted}, 过时 {stale}, 冲突 {conflictCount}");

        return new BatchReplayResult(accepted, stale, conflictCount);
    }
}

public sealed record ReplayResult(bool IsSuccess, bool IsConflict, string? ErrorMessage)
{
    public static ReplayResult Accepted() => new(true, false, null);
    public static ReplayResult Conflict(string error) => new(false, true, error);
    public static ReplayResult Failure(string error) => new(false, false, error);
}

public sealed record BatchReplayResult(int Accepted, int Stale, int Conflicts);

internal sealed record CompleteOpPayload(
    string? OrderId,
    int Sequence,
    string? OperatorId,
    string? EquipmentId,
    DateTimeOffset? CompletedAt);

internal sealed record BindMaterialPayload(
    string? OrderId,
    string MaterialCode,
    string? MaterialName,
    string BatchNumber,
    string? ProductSerial,
    string? OperatorId);
