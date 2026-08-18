using Microsoft.AspNetCore.SignalR.Client;
using MesAdmin.Infrastructure.RealTime;
using ZLogger;

namespace MesAdmin.Web.Services;

/// <summary>
/// Andon SignalR Hub 客户端（T2.22）。
/// 连接 Api 的 /hubs/andon，接收 AndonCreated/Escalated/Acknowledged/Resolved/Closed 推送。
/// 自动重连（SignalR Client 内置）。
/// </summary>
public sealed class AndonHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<AndonHubClient> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _disposed;

    public event Action<AndonEventCreatedMessage>? OnAndonCreated;
    public event Action<AndonEventEscalatedMessage>? OnAndonEscalated;
    public event Action<AndonEventAcknowledgedMessage>? OnAndonAcknowledged;
    public event Action<AndonEventResolvedMessage>? OnAndonResolved;
    public event Action<AndonEventClosedMessage>? OnAndonClosed;

    public AndonHubClient(IConfiguration config, ILogger<AndonHubClient> logger)
    {
        _logger = logger;
        var hubUrl = config["SignalR:AndonHubUrl"] ?? "http://localhost:5040/hubs/andon";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _connection.On<AndonEventCreatedMessage>("AndonCreated", msg =>
        {
            _logger.ZLogInformation($"Andon 新报警：{msg.EventNumber}");
            OnAndonCreated?.Invoke(msg);
        });

        _connection.On<AndonEventEscalatedMessage>("AndonEscalated", msg =>
        {
            _logger.ZLogWarning($"Andon 升级 L{msg.NewLevel}：{msg.EventId}");
            OnAndonEscalated?.Invoke(msg);
        });

        _connection.On<AndonEventAcknowledgedMessage>("AndonAcknowledged", msg =>
        {
            _logger.ZLogInformation($"Andon 已确认：{msg.EventId}");
            OnAndonAcknowledged?.Invoke(msg);
        });

        _connection.On<AndonEventResolvedMessage>("AndonResolved", msg =>
        {
            _logger.ZLogInformation($"Andon 已解决：{msg.EventId}");
            OnAndonResolved?.Invoke(msg);
        });

        _connection.On<AndonEventClosedMessage>("AndonClosed", msg =>
        {
            _logger.ZLogInformation($"Andon 已关闭：{msg.EventId}");
            OnAndonClosed?.Invoke(msg);
        });
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        // 单例连接由多个页面电路（circuit）共享，串行化启动并防止对已释放连接调用。
        if (_disposed || _connection.State == HubConnectionState.Connected) return;

        await _startGate.WaitAsync(ct);
        try
        {
            if (_disposed || _connection.State == HubConnectionState.Connected) return;

            await _connection.StartAsync(ct);
            _logger.ZLogInformation($"Andon Hub 已连接：{_connection}");
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"Andon Hub 连接失败（将在后台自动重连）");
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _startGate.Dispose();
        await _connection.DisposeAsync();
    }
}
