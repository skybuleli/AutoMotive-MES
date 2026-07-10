using Microsoft.Extensions.Configuration;

namespace MesAdmin.Infrastructure;

public static class RuntimeSafetyGuards
{
    public static void ValidateNoSimulationInProduction(IConfiguration config, string environmentName)
    {
        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            return;

        if (!config.GetValue<bool>("Sap:UseRealClient"))
            throw new InvalidOperationException("Production requires Sap:UseRealClient=true. Mock SAP is only allowed outside Production.");

        if (!config.GetValue<bool>("Plc:UseRealClients"))
            throw new InvalidOperationException("Production requires Plc:UseRealClients=true. Simulated PLC clients are only allowed outside Production.");

        // 真实驱动现已实现（OPC UA / EtherNet/IP / Profinet(S7)），
        // 生产模式下各传输层会真实连接设备并按退避重连；未启用具体驱动时该设备无数据但不降级为模拟。
        var enabledDrivers = new[]
        {
            ("OpcUa", config.GetValue("Plc:Drivers:OpcUa:Enabled", false)),
            ("ModbusTcp", config.GetValue("Plc:Drivers:ModbusTcp:Enabled", false)),
            ("EthernetIp", config.GetValue("Plc:Drivers:EthernetIp:Enabled", false)),
            ("Profinet", config.GetValue("Plc:Drivers:Profinet:Enabled", false)),
        };
        var anyEnabled = false;
        foreach (var (name, enabled) in enabledDrivers)
        {
            if (enabled) anyEnabled = true;
            else
                Console.WriteLine($"WARN: PLC 真实驱动未启用：Plc:Drivers:{name}:Enabled=false（对应设备在生产环境将无数据）");
        }

        if (!anyEnabled)
            throw new InvalidOperationException("Production requires at least one Plc:Drivers:* :Enabled=true real driver.");
    }
}
