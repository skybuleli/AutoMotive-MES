using MesAdmin.Application.Observability;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Infrastructure.Hubs;

public sealed class AndonHub : Hub
{
    private readonly IAsyncSubscriber<AndonEventCreatedMessage> _createdSub;
    private readonly IAsyncSubscriber<AndonEventEscalatedMessage> _escalatedSub;
    private readonly IAsyncSubscriber<AndonEventAcknowledgedMessage> _ackSub;
    private readonly IAsyncSubscriber<AndonEventResolvedMessage> _resolvedSub;
    private readonly IAsyncSubscriber<AndonEventClosedMessage> _closedSub;
    private readonly ILogger<AndonHub> _logger;
    private readonly List<IDisposable> _subscriptions = [];

    public AndonHub(
        IAsyncSubscriber<AndonEventCreatedMessage> createdSub,
        IAsyncSubscriber<AndonEventEscalatedMessage> escalatedSub,
        IAsyncSubscriber<AndonEventAcknowledgedMessage> ackSub,
        IAsyncSubscriber<AndonEventResolvedMessage> resolvedSub,
        IAsyncSubscriber<AndonEventClosedMessage> closedSub,
        ILogger<AndonHub> logger)
    {
        _createdSub = createdSub;
        _escalatedSub = escalatedSub;
        _ackSub = ackSub;
        _resolvedSub = resolvedSub;
        _closedSub = closedSub;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        AutoMesMetrics.RecordSignalRConnected("andon");
        _logger.LogInformation("AndonHub client connected: {Id}", Context.ConnectionId);

        lock (_subscriptions)
        {
            var sub1 = _createdSub.Subscribe(async (AndonEventCreatedMessage msg, CancellationToken ct) =>
            {
                try { await Clients.Caller.SendAsync("AndonCreated", msg, ct); }
                catch (Exception ex)
                {
                    AutoMesMetrics.RecordSignalRPushFailure("andon", "AndonCreated");
                    _logger.LogError(ex, "AndonCreated push failed");
                }
            });
            _subscriptions.Add(sub1);

            var sub2 = _escalatedSub.Subscribe(async (AndonEventEscalatedMessage msg, CancellationToken ct) =>
            {
                try { await Clients.Caller.SendAsync("AndonEscalated", msg, ct); }
                catch (Exception ex)
                {
                    AutoMesMetrics.RecordSignalRPushFailure("andon", "AndonEscalated");
                    _logger.LogError(ex, "AndonEscalated push failed");
                }
            });
            _subscriptions.Add(sub2);

            var sub3 = _ackSub.Subscribe(async (AndonEventAcknowledgedMessage msg, CancellationToken ct) =>
            {
                try { await Clients.Caller.SendAsync("AndonAcknowledged", msg, ct); }
                catch (Exception ex)
                {
                    AutoMesMetrics.RecordSignalRPushFailure("andon", "AndonAcknowledged");
                    _logger.LogError(ex, "AndonAcknowledged push failed");
                }
            });
            _subscriptions.Add(sub3);

            var sub4 = _resolvedSub.Subscribe(async (AndonEventResolvedMessage msg, CancellationToken ct) =>
            {
                try { await Clients.Caller.SendAsync("AndonResolved", msg, ct); }
                catch (Exception ex)
                {
                    AutoMesMetrics.RecordSignalRPushFailure("andon", "AndonResolved");
                    _logger.LogError(ex, "AndonResolved push failed");
                }
            });
            _subscriptions.Add(sub4);

            var sub5 = _closedSub.Subscribe(async (AndonEventClosedMessage msg, CancellationToken ct) =>
            {
                try { await Clients.Caller.SendAsync("AndonClosed", msg, ct); }
                catch (Exception ex)
                {
                    AutoMesMetrics.RecordSignalRPushFailure("andon", "AndonClosed");
                    _logger.LogError(ex, "AndonClosed push failed");
                }
            });
            _subscriptions.Add(sub5);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        AutoMesMetrics.RecordSignalRDisconnected("andon");
        _logger.LogInformation("AndonHub client disconnected: {Id}", Context.ConnectionId);

        lock (_subscriptions)
        {
            foreach (var sub in _subscriptions)
                sub.Dispose();
            _subscriptions.Clear();
        }

        return base.OnDisconnectedAsync(exception);
    }
}
