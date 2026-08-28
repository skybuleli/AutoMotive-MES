using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// 量具校准到期提醒服务（S01 · IATF 16949 计量管理）。
/// 每日扫描台账：
///   1. 刷新 DueSoon（≤30 天）/ Overdue 状态并持久化；
///   2. 存在临期或过期器具时，经飞书机器人推送分级提醒卡片。
/// 过期量具必须立即停用送检——状态流转落库后，S02 的检验引用校验据此拦截。
/// </summary>
public sealed class GaugeDueReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFeishuNotifier _notifier;
    private readonly ILogger<GaugeDueReminderService> _logger;

    /// <summary>扫描间隔（测试时可缩短）</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>首次启动延迟（测试时可缩短）</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    public GaugeDueReminderService(
        IServiceScopeFactory scopeFactory,
        IFeishuNotifier notifier,
        ILogger<GaugeDueReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _notifier = notifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.ZLogInformation($"量具校准提醒服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.ZLogError(ex, $"量具校准提醒服务扫描异常");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// 执行一次扫描。返回 (过期数, 临期数)。公开供测试直接调用。
    /// </summary>
    public async Task<(int OverdueCount, int DueSoonCount)> RunCheckAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var gauges = await gaugeRepo.GetAllAsync(ct: ct);
        var now = DateTimeOffset.UtcNow;

        var overdue = new List<Gauge>();
        var dueSoon = new List<Gauge>();

        foreach (var gauge in gauges)
        {
            if (gauge.Status == GaugeStatus.Scrapped) continue;

            var before = gauge.Status;
            gauge.RefreshStatus(now);

            // 状态流转持久化（InService→DueSoon→Overdue 单向推进）
            if (gauge.Status != before)
                await gaugeRepo.UpdateAsync(gauge, ct);

            switch (gauge.Status)
            {
                case GaugeStatus.Overdue:
                    overdue.Add(gauge);
                    break;
                case GaugeStatus.DueSoon:
                    dueSoon.Add(gauge);
                    break;
            }
        }

        if (overdue.Count > 0 || dueSoon.Count > 0)
        {
            var text = ComposeMessage(overdue, dueSoon);
            var sent = await _notifier.SendTextAsync(text, ct);
            _logger.ZLogWarning($"量具校准提醒：过期 {overdue.Count} 台 / 临期 {dueSoon.Count} 台，飞书推送={sent}");
        }
        else
        {
            _logger.ZLogInformation($"量具校准扫描完成：无临期/过期器具（共 {gauges.Count} 台在册）");
        }

        return (overdue.Count, dueSoon.Count);
    }

    internal static string ComposeMessage(IReadOnlyList<Gauge> overdue, IReadOnlyList<Gauge> dueSoon)
    {
        const int maxLinesPerGroup = 10;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("AutoMES 计量器具校准提醒");

        if (overdue.Count > 0)
        {
            sb.Append("【已过期 ").Append(overdue.Count).AppendLine(" 台 — 请立即停用送检】");
            foreach (var g in overdue.Take(maxLinesPerGroup))
                sb.Append("- ").Append(g.GaugeNumber).Append(' ').Append(g.Name)
                  .Append(" 到期 ").AppendLine(FormatDate(g.NextDueAt));
            if (overdue.Count > maxLinesPerGroup)
                sb.AppendLine($"... 其余 {overdue.Count - maxLinesPerGroup} 台已省略");
        }

        if (dueSoon.Count > 0)
        {
            sb.Append("【30 天内到期 ").Append(dueSoon.Count).AppendLine(" 台 — 请安排送检计划】");
            foreach (var g in dueSoon.Take(maxLinesPerGroup))
                sb.Append("- ").Append(g.GaugeNumber).Append(' ').Append(g.Name)
                  .Append(" 到期 ").AppendLine(FormatDate(g.NextDueAt));
            if (dueSoon.Count > maxLinesPerGroup)
                sb.AppendLine($"... 其余 {dueSoon.Count - maxLinesPerGroup} 台已省略");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatDate(DateTimeOffset? date)
        => date?.ToString("yyyy-MM-dd") ?? "-";
}
