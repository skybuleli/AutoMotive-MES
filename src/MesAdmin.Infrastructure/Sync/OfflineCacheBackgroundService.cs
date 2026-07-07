using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sync;

/// <summary>
/// 离线缓存后台服务（T4.4）。
/// 定期执行以下任务：
/// 1. 重试 Pending/Failed 离线同步记录（每 60s）
/// 2. 记录同步统计（每 24h）
/// 3. 记录 Channel 健康度日志
/// </summary>
public sealed class OfflineCacheBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OfflineSyncService _syncService;
    private readonly ILogger<OfflineCacheBackgroundService> _logger;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(24);
    private const int RetentionDays = 7;

    private DateTime _lastReport;

    public OfflineCacheBackgroundService(
        IServiceScopeFactory scopeFactory,
        OfflineSyncService syncService,
        ILogger<OfflineCacheBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _syncService = syncService;
        _logger = logger;
        _lastReport = DateTime.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.ZLogInformation($"[OfflineCache] 离线缓存后台服务启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var retried = await _syncService.RetryPendingAsync(limit: 100, ct: stoppingToken);

                var health = _syncService.Health;
                var backlog = health.Enqueued - health.Processed;
                if (backlog > 0 || retried > 0)
                {
                    _logger.ZLogInformation(
                        $"[OfflineCache] 状态：Channel 积压 {backlog} | 已处理 {health.Processed} | 冲突 {health.Conflicts} | 重试 {retried}");
                }

                if (DateTime.UtcNow - _lastReport >= ReportInterval)
                {
                    await ReportStatisticsAsync(stoppingToken);
                    _lastReport = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"[OfflineCache] 后台任务异常");
            }

            try { await Task.Delay(RetryInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.ZLogInformation($"[OfflineCache] 离线缓存后台服务停止");
    }

    /// <summary>每日报告 + 清理已同步的历史记录（保留 RetentionDays 天）</summary>
    private async Task ReportStatisticsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
            var stats = await repo.GetStatsAsync(ct);
            _logger.ZLogInformation(
                $"[OfflineCache] 同步统计：总 {stats.TotalCount} | 已同步 {stats.SyncedCount} | 待处理 {stats.PendingCount} | 冲突 {stats.ConflictCount} | 失败 {stats.FailedCount} | 终端数 {stats.TerminalCount}");

            // 清理超过 7 天的已同步记录
            var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            var deleted = await repo.DeleteSyncedBeforeAsync(cutoff, ct);
            if (deleted > 0)
            {
                _logger.ZLogInformation($"[OfflineCache] 清理 {deleted} 条已同步历史记录（保留 {RetentionDays} 天）");
            }
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"[OfflineCache] 报告同步统计异常");
        }
    }
}
