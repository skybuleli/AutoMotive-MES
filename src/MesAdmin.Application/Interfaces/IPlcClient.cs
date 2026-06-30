using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// PLC 客户端接口（Application 层接口）。
/// Infrastructure 层提供 OpcUaPlcClient 等多协议实现。
/// 零分配：读取结果通过 Span/ReadOnlySpan 解析，禁止 byte[] + BitConverter 分配版本。
/// </summary>
public interface IPlcClient
{
    /// <summary>读取设备寄存器值</summary>
    Task<object> ReadAsync(string address, string tag, CancellationToken cancellationToken = default);

    /// <summary>写入设备寄存器</summary>
    Task WriteAsync(string address, string tag, object value, CancellationToken cancellationToken = default);

    /// <summary>设备就绪检查</summary>
    Task<bool> IsReadyAsync(string plcAddress, CancellationToken cancellationToken = default);
}
