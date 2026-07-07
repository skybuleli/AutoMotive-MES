using System.Threading.Channels;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

// 禁用 CS1998：DispatchOperationAsync 返回 Task<DispatchResult>，实际同步流程由 OfflineReplayService 异步处理
#pragma warning disable CS1998

namespace MesAdmin.Infrastructure.Sync;

/// <summary>
/// 离线同步服务（T4.4）。
/// 使用 BoundedChannel 缓冲待处理离线记录，消费端批量处理：
/// 1. 从 Channel 读取待同步记录
/// 2. 尝试重放操作到对应业务服务
/// 3. 成功 → MarkSynced，失败（网络/冲突）→ MarkFailed/MarkConflict
/// 4. 支持重试（指数退避）
/// </summary>
public sealed class OfflineSyncService : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OfflineSyncService> _logger;

    // Channel 容量 5000，FullMode=Wait 背压
    private readonly Channel<OfflineSyncRecord> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _consumerTask;
    private bool _disposed;

    // 重试退避（秒）
    private static readonly int[] RetryDelaysSec = [5, 15, 30, 60, 120, 300];

    /// <summary>Channel 健康度</summary>
    public OfflineChannelHealth Health { get; } = new();

    public OfflineSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<OfflineSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<OfflineSyncRecord>(
            new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumerLoopAsync(_cts.Token));
    }

    public async Task EnqueueAsync(OfflineSyncRecord record, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(record, ct);
        Health.IncrementEnqueued();
        _logger.ZLogInformation($"[OfflineSync] 入队：{record.OperationType} | {record.EntityType}:{record.EntityId}");
    }

    public async Task EnqueueBatchAsync(List<OfflineSyncRecord> records, CancellationToken ct = default)
    {
        foreach (var record in records)
        {
            await _channel.Writer.WriteAsync(record, ct);
            Health.IncrementEnqueued();
        }
        _logger.ZLogInformation($"[OfflineSync] 批量入队：{records.Count} 条来自终端 {records[0].TerminalId}");
    }

    private async Task ConsumerLoopAsync(CancellationToken ct)
    {
        _logger.ZLogInformation($"[OfflineSync] 消费循环启动");

        try
        {
            await foreach (var record in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessRecordAsync(record, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.ZLogError(ex, $"[OfflineSync] 处理异常：{record.OperationType} | {record.Id}");
                    await PersistFailureAsync(record, ex.Message, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"[OfflineSync] 消费循环异常");
        }
    }

    private async Task ProcessRecordAsync(OfflineSyncRecord record, CancellationToken ct)
    {
        record.Status = OfflineSyncStatus.Syncing;
        record.LastAttemptAt = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();

        try
        {
            var result = await DispatchOperationAsync(scope, record, ct);

            if (result.IsSuccess)
            {
                await MarkSyncedAsync(record, ct);
                Health.IncrementProcessed();
                _logger.ZLogInformation($"[OfflineSync] ✅ 同步成功：{record.OperationType} | {record.EntityType}:{record.EntityId}");
            }
            else if (result.IsConflict)
            {
                await PersistConflictAsync(record, result.ErrorMessage!, ct);
                Health.IncrementConflicts();
                _logger.ZLogWarning($"[OfflineSync] ⚠️ 冲突：{record.OperationType} | {record.EntityType}:{record.EntityId} | {result.ErrorMessage}");
            }
            else
            {
                await HandleRetryAsync(record, result.ErrorMessage!, ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await HandleRetryAsync(record, ex.Message, ct);
        }
    }

    private async Task<DispatchResult> DispatchOperationAsync(
        IServiceScope scope, OfflineSyncRecord record, CancellationToken ct)
    {
        var replayService = scope.ServiceProvider.GetRequiredService<OfflineReplayService>();
        var result = await replayService.ReplayAsync(record, ct);

        if (result.IsConflict)
            return DispatchResult.Conflict(result.ErrorMessage ?? "未知冲突");
        if (!result.IsSuccess)
            return DispatchResult.Fail(result.ErrorMessage ?? "未知错误");

        return DispatchResult.Ok();
    }

    private async Task MarkSyncedAsync(OfflineSyncRecord record, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        record.Status = OfflineSyncStatus.Synced;
        record.SyncedAt = DateTimeOffset.UtcNow;
        record.ErrorMessage = null;
        await repo.UpdateRangeAsync([record], ct);
    }

    private async Task HandleRetryAsync(OfflineSyncRecord record, string error, CancellationToken ct)
    {
        record.RetryCount++;
        record.ErrorMessage = error;

        if (record.RetryCount >= RetryDelaysSec.Length)
        {
            record.Status = OfflineSyncStatus.Failed;
            _logger.ZLogWarning($"[OfflineSync] ❌ 超过最大重试次数：{record.OperationType} | {record.EntityType}:{record.EntityId} | {error}");
        }
        else
        {
            record.Status = OfflineSyncStatus.Pending;
            var delay = RetryDelaysSec[record.RetryCount - 1];
            _logger.ZLogWarning($"[OfflineSync] 🔄 重试 #{record.RetryCount}/{RetryDelaysSec.Length}：{record.OperationType} | 等待 {delay}s | {error}");
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        await repo.UpdateRangeAsync([record], ct);
    }

    private async Task PersistFailureAsync(OfflineSyncRecord record, string error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        record.Status = OfflineSyncStatus.Failed;
        record.ErrorMessage = error;
        record.LastAttemptAt = DateTimeOffset.UtcNow;
        await repo.UpdateRangeAsync([record], ct);
    }

    private async Task PersistConflictAsync(OfflineSyncRecord record, string error, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        record.Status = OfflineSyncStatus.Conflict;
        record.ErrorMessage = error;
        record.LastAttemptAt = DateTimeOffset.UtcNow;
        await repo.UpdateRangeAsync([record], ct);
    }

    public async Task<int> RetryPendingAsync(int limit = 100, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        var pending = await repo.GetPendingAsync(limit, ct);

        foreach (var record in pending)
        {
            await _channel.Writer.WriteAsync(record, ct);
            Health.IncrementEnqueued();
        }

        _logger.ZLogInformation($"[OfflineSync] 后台重试：重新入队 {pending.Count} 条待同步记录");
        return pending.Count;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _channel.Writer.TryComplete();
        try { await _consumerTask; } catch { }
        _cts.Dispose();
    }
}

public sealed record DispatchResult(bool IsSuccess, bool IsConflict, string? ErrorMessage)
{
    public static DispatchResult Ok() => new(true, false, null);
    public static DispatchResult Conflict(string error) => new(false, true, error);
    public static DispatchResult Fail(string error) => new(false, false, error);
}

public sealed class OfflineChannelHealth
{
    private long _enqueued;
    private long _processed;
    private long _conflicts;

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Processed => Interlocked.Read(ref _processed);
    public long Conflicts => Interlocked.Read(ref _conflicts);

    public void IncrementEnqueued() => Interlocked.Increment(ref _enqueued);
    public void IncrementProcessed() => Interlocked.Increment(ref _processed);
    public void IncrementConflicts() => Interlocked.Increment(ref _conflicts);

    public long Backlog => Enqueued - Processed;
}
