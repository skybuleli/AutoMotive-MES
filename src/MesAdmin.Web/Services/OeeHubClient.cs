using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MesAdmin.Domain.Models;

namespace MesAdmin.Web.Services;

/// <summary>
/// OEE SignalR Hub 客户端（T2.15/T2.19）。
/// 连接 Api 的 /hubs/dashboard，接收 OeeUpdated 推送 → ObservableCollection 增量绑定。
/// 自动重连（SignalR Client 内置，T4.11 混沌工程验证）。
/// </summary>
public sealed class OeeHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ILogger<OeeHubClient> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _disposed;

    // 单例跨电路共享：SignalR 回调线程写、UI 渲染线程读，必须线程安全。
    private readonly ConcurrentDictionary<string, OeeRecord> _latest = new(StringComparer.Ordinal);

    /// <summary>数据变更通知（各页面组件订阅后经 InvokeAsync 刷新，避免直接跨线程读集合）。</summary>
    public event Action? Changed;

    /// <summary>通道健康度信息（引用赋值原子，读到的是一致快照）。</summary>
    public ChannelHealthInfo? Health { get; private set; }

    /// <summary>获取 8 设备最新 OEE 的线程安全快照（副本，供 UI 安全枚举）。</summary>
    public IReadOnlyList<OeeRecord> Snapshot()
        => _latest.Values.OrderBy(o => o.EquipmentCode, StringComparer.Ordinal).ToList();

    public OeeHubClient(IConfiguration config, ILogger<OeeHubClient> logger)
    {
        _logger = logger;
        var hubUrl = config["SignalR:DashboardHubUrl"] ?? "http://localhost:5040/hubs/dashboard";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        // 注册 OeeUpdated 回调
        _connection.On<OeeRecord>("OeeUpdated", OnOeeUpdated);
        _connection.On<ChannelHealthInfo>("ChannelHealth", OnChannelHealth);
    }

    private void OnOeeUpdated(OeeRecord oee)
    {
        // 线程安全增量更新：同设备覆盖，新设备追加（ConcurrentDictionary 原子）
        _latest[oee.EquipmentCode] = oee;
        Changed?.Invoke();
    }

    private void OnChannelHealth(ChannelHealthInfo health)
    {
        Health = health;
        Changed?.Invoke();
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
            _logger.LogInformation("OEE Hub 已连接：{Url}", _connection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OEE Hub 连接失败（将在后台自动重连）");
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

/// <summary>通道健康度信息（从 Hub 推送）</summary>
public sealed record ChannelHealthInfo(
    string EquipmentCount,
    long Written,
    long Read,
    double Utilization);
