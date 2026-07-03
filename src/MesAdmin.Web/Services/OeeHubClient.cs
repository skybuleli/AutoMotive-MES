using System.Collections.ObjectModel;
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

    /// <summary>8 设备最新 OEE 记录（ObservableCollections 增量绑定，AGENTS.md 9.2）</summary>
    public ObservableCollection<OeeRecord> LatestOee { get; } = [];

    /// <summary>通道健康度信息</summary>
    public ChannelHealthInfo? Health { get; private set; }

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
        // 增量更新：同设备覆盖，新设备追加
        var existing = LatestOee.FirstOrDefault(o => o.EquipmentCode == oee.EquipmentCode);
        if (existing is not null)
        {
            var idx = LatestOee.IndexOf(existing);
            LatestOee[idx] = oee;
        }
        else
        {
            LatestOee.Add(oee);
        }
    }

    private void OnChannelHealth(ChannelHealthInfo health)
        => Health = health;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection.State == HubConnectionState.Connected) return;

        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("OEE Hub 已连接：{Url}", _connection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OEE Hub 连接失败（将在后台自动重连）");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

/// <summary>通道健康度信息（从 Hub 推送）</summary>
public sealed record ChannelHealthInfo(
    string EquipmentCount,
    long Written,
    long Read,
    double Utilization);
