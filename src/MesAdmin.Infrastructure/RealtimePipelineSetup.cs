using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Hubs;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace MesAdmin.Infrastructure;

/// <summary>
/// PLC + R3 OEE + SignalR 实时管道 DI 注册扩展（T2.12-T2.15）。
/// </summary>
public static class RealtimePipelineSetup
{
    /// <summary>
    /// 注册 PLC 数据采集 + R3 OEE + SignalR DashboardHub 完整链路。
    /// </summary>
    public static IServiceCollection AddRealtimePipeline(this IServiceCollection services, IConfiguration config)
    {
        // MessagePipe 进程内消息总线
        services.AddMessagePipe();

        // PLC 配置
        var plcSection = config.GetSection("Plc");
        var channelCapacity = plcSection.GetValue("ChannelCapacity", 10000);
        var readIntervalMs = plcSection.GetValue("ReadIntervalMs", 10);

        // PLC 客户端 + 模拟传输（单例，共享 PipeReader）
        services.AddSingleton<OpcUaPlcClient>();
        services.AddSingleton<IPlcClient>(sp => sp.GetRequiredService<OpcUaPlcClient>());

        // PLC 数据采集管道（HostedService）
        services.AddSingleton<PlcDataAcquisitionPipeline>();
        services.AddHostedService(sp => sp.GetRequiredService<PlcDataAcquisitionPipeline>());

        // R3 OEE 响应式管道（HostedService）
        services.AddHostedService<OeeReactivePipeline>();

        // SignalR + MemoryPack 协议（注册自定义 HubProtocol 替代默认 JSON）
        services.AddSignalR();
        services.AddSingleton<Microsoft.AspNetCore.SignalR.Protocol.IHubProtocol, MemoryPackHubProtocol>();

        // Channel 健康度推送服务（10s 周期）
        services.AddHostedService<ChannelHealthPushService>();

        return services;
    }
}
