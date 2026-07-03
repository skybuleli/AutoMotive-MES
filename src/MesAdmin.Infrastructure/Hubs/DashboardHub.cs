using MemoryPack;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Hubs;

/// <summary>
/// 仪表盘 SignalR Hub（T2.15）。
/// OeeUpdated 3s 推送：订阅 MessagePipe PlcDataChanged → 广播到所有客户端。
/// ChannelHealth 10s 推送：通道健康度（容量使用率、写入/读取计数）。
/// 强制 MemoryPack 二进制协议（AGENTS.md 4.4）。
/// </summary>
public sealed class DashboardHub : Hub
{
    private readonly IAsyncSubscriber<PlcDataChanged> _subscriber;
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(IAsyncSubscriber<PlcDataChanged> subscriber, ILogger<DashboardHub> logger)
    {
        _subscriber = subscriber;
        _logger = logger;
    }

    /// <summary>客户端连接时注册 OEE 更新订阅</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        _logger.ZLogInformation($"DashboardHub 客户端连接：{Context.ConnectionId}");

        // 订阅 PlcDataChanged 消息，推送给当前客户端
        _ = _subscriber.Subscribe(async (msg, ct) =>
        {
            try
            {
                await Clients.Caller.SendAsync("OeeUpdated", msg.Oee, ct);
            }
            catch (Exception ex)
            {
                _logger.ZLogError($"OEE 推送失败：{ex.Message}");
            }
        });
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.ZLogInformation($"DashboardHub 客户端断开：{Context.ConnectionId}");
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Channel 健康度推送服务（T2.15，10s 周期推送）。
/// 作为 IHostedService 运行，定时读取 PlcDataAcquisitionPipeline.Health 推送到所有 Hub 客户端。
/// </summary>
public sealed class ChannelHealthPushService : IHostedService, IAsyncDisposable
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly PlcDataAcquisitionPipeline _pipeline;
    private readonly ILogger<ChannelHealthPushService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);
    private CancellationTokenSource? _cts;
    private Task? _pushTask;

    public ChannelHealthPushService(
        IHubContext<DashboardHub> hubContext,
        PlcDataAcquisitionPipeline pipeline,
        ILogger<ChannelHealthPushService> logger)
    {
        _hubContext = hubContext;
        _pipeline = pipeline;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pushTask = PushLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task PushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var health = _pipeline.Health;
                var msg = new ChannelHealthMessage(
                    EquipmentCount: Equipment.DefaultEquipment.Count.ToString(),
                    Written: health.Written,
                    Read: health.Read,
                    Utilization: Math.Round(health.GetUtilization(10000), 4));

                await _hubContext.Clients.All.SendAsync("ChannelHealth", msg, ct);
            }
            catch (Exception ex)
            {
                _logger.ZLogError($"ChannelHealth 推送异常：{ex.Message}");
            }

            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_pushTask is not null)
        {
            try { await _pushTask; } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
