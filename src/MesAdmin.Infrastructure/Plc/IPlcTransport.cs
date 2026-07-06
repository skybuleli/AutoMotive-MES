using System.IO.Pipelines;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 传输层抽象（T2.16 多协议驱动接口）。
/// 每种工业协议（OPC UA / Modbus TCP / EtherNet/IP / Profinet）实现此接口。
/// OpcUaPlcClient 通过此接口读取 Pipe 数据，帧协议与解析层不变。
/// 零分配：传输层负责将原始字节写入 Pipe，客户端用 PipeReader 零拷贝读取。
/// </summary>
public interface IPlcTransport : IAsyncDisposable
{
    /// <summary>暴露 PipeReader 供 OpcUaPlcClient 零拷贝读取</summary>
    PipeReader Reader { get; }

    /// <summary>启动传输层连接</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止传输层</summary>
    Task StopAsync();

    /// <summary>读取设备寄存器值（直接调用，非 Pipe 方式）</summary>
    Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default);

    /// <summary>写入设备寄存器（直接调用，非 Pipe 方式）</summary>
    Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default);

    /// <summary>设备连接状态</summary>
    bool IsConnected { get; }

    /// <summary>传输层名称（用于日志）</summary>
    string TransportName { get; }

    /// <summary>支持的设备编码列表</summary>
    IReadOnlySet<string> SupportedEquipmentCodes { get; }
}
