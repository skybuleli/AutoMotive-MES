using MesAdmin.Application.Observability;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Infrastructure.Hubs;

public sealed class AndonHub : Hub
{
    private readonly ILogger<AndonHub> _logger;

    public AndonHub(ILogger<AndonHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        AutoMesMetrics.RecordSignalRConnected("andon");
        _logger.LogInformation("AndonHub client connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        AutoMesMetrics.RecordSignalRDisconnected("andon");
        _logger.LogInformation("AndonHub client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Andon 推送服务（T2.22）。
/// 单例后台服务订阅 5 类 Andon 消息，经 IHubContext 广播到所有客户端。
///
/// ⚠ 不可在 Hub 内订阅并持有 Clients/Context：Hub 实例每次调用创建、方法返回即释放，
/// 回调触发时 Hub 已释放 → ObjectDisposedException。必须通过 IHubContext&lt;AndonHub&gt; 外部广播。
/// </summary>
public sealed class AndonPushService : IHostedService, IDisposable
{
    private readonly IAsyncSubscriber<AndonEventCreatedMessage> _createdSub;
    private readonly IAsyncSubscriber<AndonEventEscalatedMessage> _escalatedSub;
    private readonly IAsyncSubscriber<AndonEventAcknowledgedMessage> _ackSub;
    private readonly IAsyncSubscriber<AndonEventResolvedMessage> _resolvedSub;
    private readonly IAsyncSubscriber<AndonEventClosedMessage> _closedSub;
    private readonly IHubContext<AndonHub> _hubContext;
    private readonly ILogger<AndonPushService> _logger;
    private readonly List<IDisposable> _subscriptions = [];

    public AndonPushService(
        IAsyncSubscriber<AndonEventCreatedMessage> createdSub,
        IAsyncSubscriber<AndonEventEscalatedMessage> escalatedSub,
        IAsyncSubscriber<AndonEventAcknowledgedMessage> ackSub,
        IAsyncSubscriber<AndonEventResolvedMessage> resolvedSub,
        IAsyncSubscriber<AndonEventClosedMessage> closedSub,
        IHubContext<AndonHub> hubContext,
        ILogger<AndonPushService> logger)
    {
        _createdSub = createdSub;
        _escalatedSub = escalatedSub;
        _ackSub = ackSub;
        _resolvedSub = resolvedSub;
        _closedSub = closedSub;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_createdSub.Subscribe((msg, ct) => BroadcastAsync("AndonCreated", msg, ct)));
        _subscriptions.Add(_escalatedSub.Subscribe((msg, ct) => BroadcastAsync("AndonEscalated", msg, ct)));
        _subscriptions.Add(_ackSub.Subscribe((msg, ct) => BroadcastAsync("AndonAcknowledged", msg, ct)));
        _subscriptions.Add(_resolvedSub.Subscribe((msg, ct) => BroadcastAsync("AndonResolved", msg, ct)));
        _subscriptions.Add(_closedSub.Subscribe((msg, ct) => BroadcastAsync("AndonClosed", msg, ct)));
        return Task.CompletedTask;
    }

    private async ValueTask BroadcastAsync<T>(string method, T msg, CancellationToken ct)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(method, msg, ct);
        }
        catch (Exception ex)
        {
            AutoMesMetrics.RecordSignalRPushFailure("andon", method);
            _logger.LogError(ex, "{Method} push failed", method);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }
}
