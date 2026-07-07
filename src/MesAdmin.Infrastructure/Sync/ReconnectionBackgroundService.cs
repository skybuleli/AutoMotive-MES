using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sync;

/// <summary>
/// 断网重连自动同步后台服务（T4.5）。
/// 定期执行：
/// 1. 扫描所有终端的 Pending/Conflict 记录
/// 2. 执行 Saga 状态合并和冲突解决
/// 3. 自动重放过时的离线操作
/// </summary>
public sealed class ReconnectionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReconnectionBackgroundService> _logger;

    // 扫描间隔：30 秒
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    public ReconnectionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReconnectionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.ZLogInformation($"[Reconnect] 断网重连自动同步后台服务启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"[Reconnect] 扫描异常");
            }

            try { await Task.Delay(ScanInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.ZLogInformation($"[Reconnect] 断网重连自动同步后台服务停止");
    }

    /// <summary>扫描所有待处理记录并执行重放</summary>
    private async Task ScanAndReconcileAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOfflineSyncRepository>();
        var replayService = scope.ServiceProvider.GetRequiredService<OfflineReplayService>();

        // 1. 获取统计信息
        var stats = await repo.GetStatsAsync(ct);
        if (stats.PendingCount == 0 && stats.ConflictCount == 0)
            return; // 无待处理记录

        // 2. 获取待处理记录
        var pending = await repo.GetPendingAsync(limit: 200, ct);
        if (pending.Count == 0)
            return;

        // 3. 按终端分组直接重放（ReplayAsync 内部自动执行冲突检测 + 业务分派）
        var terminalGroups = pending
            .GroupBy(r => r.TerminalId)
            .ToList();

        foreach (var group in terminalGroups)
        {
            _logger.ZLogInformation(
                $"[Reconnect] 终端 {group.Key}：{group.Count()} 条待同步记录");

            var replayResult = await replayService.ReplayTerminalBatchAsync(group.Key, ct);
            if (replayResult.Accepted > 0 || replayResult.Conflicts > 0)
            {
                _logger.ZLogInformation(
                    $"[Reconnect] 终端 {group.Key} 重放：接受 {replayResult.Accepted}, 过时 {replayResult.Stale}, 冲突 {replayResult.Conflicts}");
            }
        }

        // 4. 对 Conflict 记录记录日志
        var conflicts = await repo.GetConflictsAsync(ct: ct);
        if (conflicts.Count > 0)
        {
            _logger.ZLogWarning($"[Reconnect] 仍有 {conflicts.Count} 条冲突记录等待人工解决");
        }
    }
}
