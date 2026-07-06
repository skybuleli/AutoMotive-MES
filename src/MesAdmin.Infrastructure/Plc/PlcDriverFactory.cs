using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 驱动工厂（T2.16 多协议驱动调度器）。
/// 根据设备编码或设备类型解析对应的 IPlcTransport 实现。
/// 支持 5 种传输层：Simulated（开发模拟）/ OPC UA / Modbus TCP / EtherNet/IP / Profinet。
///
/// 传输层优先级（由调用方传入顺序决定）：
/// 后注册的传输层覆盖先注册的。因此真实协议传输层应放在 Simulated 之后传入。
/// 未匹配到专用驱动的设备自动降级到 SimulatedPlcTransport（开发模式可工作）。
/// </summary>
public sealed class PlcDriverFactory
{
    private readonly IReadOnlyList<IPlcTransport> _transports;
    private readonly Dictionary<string, IPlcTransport> _equipmentToTransport;
    private readonly ILogger<PlcDriverFactory> _logger;
    private readonly SimulatedPlcTransport? _simulatedFallback;

    public PlcDriverFactory(
        IReadOnlyList<IPlcTransport> transports,
        ILogger<PlcDriverFactory> logger)
    {
        _transports = transports;
        _logger = logger;

        _equipmentToTransport = new Dictionary<string, IPlcTransport>(StringComparer.Ordinal);
        foreach (var t in transports)
        {
            foreach (var code in t.SupportedEquipmentCodes)
            {
                // 后注册的传输层覆盖先注册的（真实协议覆盖 Simulated）
                // 方法：非 Simulated 的传输层使用 TryAdd（不覆盖已有的）
                // Simulated 传输层不添加，仅作为降级兜底
                if (t is SimulatedPlcTransport)
                    continue; // Simulated 不加入字典，仅作降级用
                _equipmentToTransport.TryAdd(code, t);
            }
        }

        _simulatedFallback = transports.OfType<SimulatedPlcTransport>().FirstOrDefault();
    }

    /// <summary>
    /// 获取指定设备编码对应的传输层。
    /// 未找到时降级到 SimulatedPlcTransport（开发环境安全降级）。
    /// </summary>
    public IPlcTransport GetTransport(string equipmentCode)
    {
        if (_equipmentToTransport.TryGetValue(equipmentCode, out var transport))
            return transport;

        // 未匹配到专用驱动 → 降级到模拟传输层
        if (_simulatedFallback is not null)
        {
            _logger.LogWarning("设备 {EquipmentCode} 未找到专用驱动，使用模拟传输层降级", equipmentCode);
            return _simulatedFallback;
        }

        // 极端降级：返回任意第一个
        _logger.LogError("设备 {EquipmentCode} 未找到任何传输层", equipmentCode);
        return _transports[0];
    }

    /// <summary>获取所有传输层（供 OpcUaPlcClient 启动读取循环）</summary>
    public IReadOnlyList<IPlcTransport> GetAllTransports() => _transports;

    /// <summary>获取指定传输层支持的设备编码列表</summary>
    public IReadOnlySet<string> GetSupportedCodes(string transportName)
        => _transports
            .FirstOrDefault(t => t.TransportName == transportName)
            ?.SupportedEquipmentCodes ?? new HashSet<string>();

    /// <summary>获取传输层状态（供日志/监控）</summary>
    public IEnumerable<(string TransportName, int EquipmentCount, bool IsConnected)> GetTransportStatus()
        => _transports.Select(t => (t.TransportName, t.SupportedEquipmentCodes.Count, t.IsConnected));
}
