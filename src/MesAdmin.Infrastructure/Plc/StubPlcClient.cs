using MesAdmin.Application.Interfaces;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 客户端骨架实现（T2.12 需替换为 OpcUaPlcClient 等真实驱动）。
/// 当前返回默认值，保证 DI 容器可构造。
/// </summary>
public class StubPlcClient : IPlcClient
{
    public Task<object> ReadAsync(string address, string tag, CancellationToken ct = default)
        => Task.FromResult<object>(0);

    public Task WriteAsync(string address, string tag, object value, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> IsReadyAsync(string plcAddress, CancellationToken ct = default)
        => Task.FromResult(true);
}
