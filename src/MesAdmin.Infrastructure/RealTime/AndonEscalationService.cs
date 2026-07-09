using MesAdmin.Application.Observability;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Notifications;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// Andon 三级升级服务（T2.21）。
/// IHostedService 后台扫描，每 15s 检查一次未关闭报警：
/// L1 Active 超 5min → 升级 L2
/// L2 EscalatedL2 超 10min → 升级 L3
/// 升级时发布 AndonEventEscalatedMessage 推送至前端。
/// </summary>
public sealed class AndonEscalationService : IHostedService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAsyncPublisher<AndonEventEscalatedMessage> _publisher;
    private readonly IFeishuNotificationService _feishu;
    private readonly ILogger<AndonEscalationService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _scanTask;

    /// <summary>L1→L2 超时（5min）</summary>
    private static readonly TimeSpan L2Timeout = TimeSpan.FromMinutes(5);

    /// <summary>L2→L3 超时（10min）</summary>
    private static readonly TimeSpan L3Timeout = TimeSpan.FromMinutes(10);

    /// <summary>扫描间隔</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(15);

    public AndonEscalationService(
        IServiceScopeFactory scopeFactory,
        IAsyncPublisher<AndonEventEscalatedMessage> publisher,
        IFeishuNotificationService feishu,
        ILogger<AndonEscalationService> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _feishu = feishu;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scanTask = ScanLoopAsync(_cts.Token);
        _logger.ZLogInformation($"Andon 升级服务启动：L1→L2={L2Timeout.TotalMinutes}min, L2→L3={L3Timeout.TotalMinutes}min");
        return Task.CompletedTask;
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();
                var active = await repo.GetActiveAsync(ct);
                var now = DateTimeOffset.UtcNow;
                var maxAgeSeconds = active.Count > 0
                    ? active.Max(ev => (now - ev.OccurredAt).TotalSeconds)
                    : 0;
                AutoMesMetrics.SetAndonResponseDurationSeconds(maxAgeSeconds);

                var escalated = new List<AndonEvent>();

                foreach (var ev in active)
                {
                    var elapsed = now - ev.OccurredAt;

                    // L1 Active → L2（5min 未确认）
                    if (ev.Status == AndonEventStatus.Active
                        && ev.EscalationLevel == 0
                        && elapsed >= L2Timeout)
                    {
                        ev.Escalate();
                        escalated.Add(ev);
                        AutoMesMetrics.RecordAndonEscalation(ev.EscalationLevel);
                        _logger.ZLogWarning($"Andon L1→L2 升级：{ev.EventNumber} {ev.Description}（已过去 {elapsed.TotalMinutes:F1}min）");
                    }
                    // L2 EscalatedL2 → L3（从 L2 升级起 10min）
                    else if (ev.Status == AndonEventStatus.EscalatedL2
                        && ev.EscalationLevel == 1
                        && ev.EscalatedAt.HasValue
                        && (now - ev.EscalatedAt.Value) >= L3Timeout)
                    {
                        ev.Escalate();
                        escalated.Add(ev);
                        AutoMesMetrics.RecordAndonEscalation(ev.EscalationLevel);
                        _logger.ZLogError($"Andon L2→L3 升级：{ev.EventNumber} {ev.Description}（已过去 {elapsed.TotalMinutes:F1}min）");
                    }
                }

                if (escalated.Count > 0)
                {
                    await repo.UpdateRangeAsync(escalated, ct);

                    foreach (var ev in escalated)
                    {
                        await _publisher.PublishAsync(new AndonEventEscalatedMessage(
                            ev.Id.ToString(),
                            ev.EscalationLevel,
                            ev.EscalatedAt ?? now), ct);

                        // 🔔 P2 外部通知：L2/L3 升级时推送飞书报警卡片
                        await _feishu.SendAndonAlertCardAsync(
                            ev.EventNumber,
                            ev.EquipmentCode,
                            ev.Station,
                            ev.AlarmType.ToString(),
                            ev.Severity.ToString(),
                            ev.Description,
                            ev.EscalationLevel,
                            ev.ProcessValue,
                            ev.UpperLimit,
                            ev.OccurredAt,
                            ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ZLogError($"Andon 升级扫描异常：{ex.Message}");
            }

            try { await Task.Delay(ScanInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_scanTask is not null)
        {
            try { await _scanTask; } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
