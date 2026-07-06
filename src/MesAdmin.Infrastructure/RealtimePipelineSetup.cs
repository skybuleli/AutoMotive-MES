using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Hubs;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Infrastructure;

/// <summary>
/// PLC + R3 OEE + SignalR 实时管道 DI 注册扩展（T2.12-T2.16）。
/// 注册多协议 PLC 传输层（Simulated/OPC UA/Modbus TCP/EtherNet/IP/Profinet）。
/// 传输层优先级由 PlcDriverFactory 内部处理：真实协议优先，Simulated 仅作降级兜底。
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
        var useRealClients = plcSection.GetValue("UseRealClients", false);

        // 设备清单（单例）
        services.AddSingleton(Equipment.DefaultEquipment);

        // ── 多协议 PLC 传输层注册（T2.16）──
        // 注册顺序：Simulated 在前（兜底），真实协议在后（覆盖）
        // PlcDriverFactory 使用 TryAdd，真实协议首次添加后不会被 Simulated 覆盖

        // 模拟传输层（开发环境默认，对所有设备生效）
        services.AddSingleton<SimulatedPlcTransport>(sp =>
        {
            var equipment = sp.GetRequiredService<IReadOnlyList<Equipment>>();
            var logger = sp.GetRequiredService<ILogger<SimulatedPlcTransport>>();
            return new SimulatedPlcTransport(equipment);
        });

        // OPC UA 传输层（拧紧机 Atlas Copco + 终检台）
        services.AddSingleton<OpcUaPlcTransport>(sp =>
        {
            var equipment = sp.GetRequiredService<IReadOnlyList<Equipment>>();
            var logger = sp.GetRequiredService<ILogger<OpcUaPlcTransport>>();
            return new OpcUaPlcTransport(equipment, logger, useRealClients);
        });

        // Modbus TCP 传输层（刷写台 + VIN 标刻机）
        services.AddSingleton<ModbusTcpPlcTransport>(sp =>
        {
            var equipment = sp.GetRequiredService<IReadOnlyList<Equipment>>();
            var logger = sp.GetRequiredService<ILogger<ModbusTcpPlcTransport>>();
            return new ModbusTcpPlcTransport(equipment, logger, useRealClients);
        });

        // EtherNet/IP 传输层（液压测试台）
        services.AddSingleton<EthernetIpPlcTransport>(sp =>
        {
            var equipment = sp.GetRequiredService<IReadOnlyList<Equipment>>();
            var logger = sp.GetRequiredService<ILogger<EthernetIpPlcTransport>>();
            return new EthernetIpPlcTransport(equipment, logger, useRealClients);
        });

        // Profinet 传输层（压装/合装工作站 — Siemens S7）
        services.AddSingleton<ProfinetPlcTransport>(sp =>
        {
            var equipment = sp.GetRequiredService<IReadOnlyList<Equipment>>();
            var logger = sp.GetRequiredService<ILogger<ProfinetPlcTransport>>();
            return new ProfinetPlcTransport(equipment, logger, useRealClients);
        });

        // ── 驱动工厂（统一接口，按设备编码分发到对应传输层）──
        // 注意：PlcDriverFactory 内部 Simulated 不作为字典映射（仅作降级兜底）
        services.AddSingleton<PlcDriverFactory>(sp =>
        {
            var transports = new List<IPlcTransport>
            {
                sp.GetRequiredService<SimulatedPlcTransport>(),      // 降级兜底
                sp.GetRequiredService<OpcUaPlcTransport>(),           // OPC UA: EQ-TQ-01/02, EQ-FT-01
                sp.GetRequiredService<ModbusTcpPlcTransport>(),       // Modbus: EQ-FLS-01, EQ-VN-01
                sp.GetRequiredService<EthernetIpPlcTransport>(),      // E/IP:   EQ-HYD-01
                sp.GetRequiredService<ProfinetPlcTransport>(),        // Profi:  EQ-ASM-01/02
            };
            var logger = sp.GetRequiredService<ILogger<PlcDriverFactory>>();
            return new PlcDriverFactory(transports, logger);
        });

        // OPC UA 兼容客户端（帧解析层，使用 PlcDriverFactory 读取多协议数据）
        services.AddSingleton<OpcUaPlcClient>();
        services.AddSingleton<IPlcClient>(sp => sp.GetRequiredService<OpcUaPlcClient>());

        // PLC 数据采集管道（HostedService，传入配置参数）
        services.AddSingleton<PlcDataAcquisitionPipeline>(sp =>
        {
            var client = sp.GetRequiredService<OpcUaPlcClient>();
            var factory = sp.GetRequiredService<PlcDriverFactory>();
            var logger = sp.GetRequiredService<ILogger<PlcDataAcquisitionPipeline>>();
            return new PlcDataAcquisitionPipeline(client, factory, logger, channelCapacity, readIntervalMs);
        });
        services.AddHostedService(sp => sp.GetRequiredService<PlcDataAcquisitionPipeline>());

        // R3 OEE 响应式管道（HostedService）
        services.AddHostedService<OeeReactivePipeline>();

        // Andon 报警管道 + 升级服务（T2.21-T2.22）
        services.AddHostedService<RealTime.AndonReactivePipeline>();
        services.AddHostedService<RealTime.AndonEscalationService>();

        // SignalR + MemoryPack 协议（注册自定义 HubProtocol 替代默认 JSON）
        services.AddSignalR();
        services.AddSingleton<Microsoft.AspNetCore.SignalR.Protocol.IHubProtocol, MemoryPackHubProtocol>();

        // Channel 健康度推送服务（10s 周期）
        services.AddHostedService<ChannelHealthPushService>();

        return services;
    }
}
