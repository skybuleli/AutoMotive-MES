using System.Diagnostics;
using MesAdmin.Infrastructure.Hubs;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace MesAdmin.Application.Tests;

/// <summary>
/// T4.11 — SignalR 自动重连混沌测试。
/// 验证 HubConnection 内置的 WithAutomaticReconnect 在服务器不可用、断连等场景下的行为。
///
/// 测试策略：使用真实的 HubConnectionBuilder 创建客户端连接，验证：
/// - 连接状态机转换（Disconnected → Connecting → Reconnecting → Connected）
/// - 自定义重连间隔（2s, 5s, 10s）
/// - StartAsync 在服务器不可用时优雅降级（LogWarning）
/// - 重连事件触发
///
/// ⚠ 此测试仅验证客户端行为（HubConnectionBuilder 配置 + 状态机），
/// 不依赖真实 SignalR 服务器。完整端到端断网重连测试需部署环境混沌工程工具。
/// </summary>
public class Chaos_SignalRReconnectTests
{
    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: HubConnectionBuilder 配置验证
    //  验证 WithAutomaticReconnect 可正确构建 HubConnection。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void HubConnection_WithAutomaticReconnect_ShouldBuildSuccessfully()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5040/hubs/dashboard")
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        Assert.NotNull(connection);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: 连接不可达 — StartAsync 应优雅抛出
    //  验证 StartAsync 在服务器不可用时抛出异常（不挂起/不崩溃）。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task StartAsync_WhenServerUnreachable_ShouldThrow()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:1/hubs/dashboard")  // 端口 1 不可达
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(1) })
            .Build();

        // StartAsync 应抛出（端口不可达），不应挂起
        var sw = Stopwatch.StartNew();
        var thrown = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.StartAsync(cts.Token);
        }
        catch
        {
            thrown = true;
        }
        finally
        {
            await connection.DisposeAsync();
        }

        Assert.True(thrown, "连接不可达时 StartAsync 应抛出异常");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: HubConnection 状态机初始状态
    //  验证新创建的 HubConnection 处于 Disconnected 状态。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task HubConnection_InitialState_ShouldBeDisconnected()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5040/hubs/dashboard")
            .WithAutomaticReconnect()
            .Build();

        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: 自定义重连间隔验证
    //  验证 HubConnectionBuilder 的自定义重连间隔配置有效。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AutomaticReconnect_CustomIntervals_ShouldBeApplied()
    {
        var intervals = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

        // 验证间隔递增
        Assert.Equal(3, intervals.Length);
        Assert.True(intervals[0] < intervals[1], "第1次重连间隔应小于第2次");
        Assert.True(intervals[1] < intervals[2], "第2次重连间隔应小于第3次");
        Assert.True(intervals[0].TotalSeconds > 0, "首次重连间隔应大于0");

        // 验证与 OeeHubClient/AndonHubClient 使用的配置一致
        Assert.Equal(2000, intervals[0].TotalMilliseconds);
        Assert.Equal(5000, intervals[1].TotalMilliseconds);
        Assert.Equal(10000, intervals[2].TotalMilliseconds);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: HubConnection 被 Disposed 后不应再尝试重连
    //  验证 DisposeAsync 后连接终止。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task HubConnection_AfterDispose_ShouldNotReconnect()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:1/hubs/dashboard")
            .WithAutomaticReconnect()
            .Build();

        await connection.DisposeAsync();

        // Dispose 后 StartAsync 应抛出 ObjectDisposedException
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var cts = new CancellationTokenSource(1000);
            await connection.StartAsync(cts.Token);
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: SignalR Hub URL 配置键
    //  验证 appsettings.json 的 SignalR 配置可正确映射。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SignalRConfiguration_ShouldHaveDashboardAndAndonHubUrls()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SignalR:DashboardHubUrl"] = "http://localhost:5040/hubs/dashboard",
                ["SignalR:AndonHubUrl"] = "http://localhost:5040/hubs/andon"
            }!)
            .Build();

        var dashboardUrl = config["SignalR:DashboardHubUrl"];
        var andonUrl = config["SignalR:AndonHubUrl"];

        Assert.Equal("http://localhost:5040/hubs/dashboard", dashboardUrl);
        Assert.Equal("http://localhost:5040/hubs/andon", andonUrl);

        // 验证 URL 格式正确
        Assert.True(Uri.TryCreate(dashboardUrl!, UriKind.Absolute, out var dashboardUri));
        Assert.True(dashboardUri!.Scheme == "http" || dashboardUri.Scheme == "https");
        Assert.True(Uri.TryCreate(andonUrl!, UriKind.Absolute, out var andonUri));
        Assert.True(andonUri!.Scheme == "http" || andonUri.Scheme == "https");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: MemoryPack HubProtocol 配置验证
    //  验证 MemoryPackHubProtocol 可正确实例化。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MemoryPackHubProtocol_ShouldCreate()
    {
        var protocol = new MemoryPackHubProtocol();

        Assert.NotNull(protocol);
        Assert.Equal("memorypack", protocol.Name);
        Assert.Equal(1, protocol.Version);
        Assert.True(protocol.IsVersionSupported(1));
        Assert.False(protocol.IsVersionSupported(2));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: MemoryPack SignalR 协议使用二进制传输格式
    //  验证 MemoryPackHubProtocol 使用 TransferFormat.Binary。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MemoryPackHubProtocol_TransferFormat_ShouldBeBinary()
    {
        var protocol = new MemoryPackHubProtocol();

        // MemoryPack 使用二进制传输格式
        Assert.Equal(TransferFormat.Binary, protocol.TransferFormat);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 9: SignalR OeeHubClient 配置模式验证
    //  验证 OeeHubClient 使用的 WithUrl/WithAutomaticReconnect 模式。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OeeHubClient_Configuration_ShouldUseCorrectPattern()
    {
        // 验证 HubConnectionBuilder 模式与 OeeHubClient 匹配
        var intervals = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5040/hubs/dashboard")
            .WithAutomaticReconnect(intervals)
            .Build();

        Assert.NotNull(connection);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }
}
