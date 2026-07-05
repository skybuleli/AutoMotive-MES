using MemoryPack;

namespace MesAdmin.Infrastructure.Hubs;

/// <summary>
/// MemoryPack 序列化的 HubMessage 包装类型（供 MemoryPackHubProtocol 使用）。
/// 因 MemoryPack 源生成器对嵌套类型支持有限，提取为顶级类型。
/// </summary>
[MemoryPackable]
public partial class HubMessageEnvelope
{
    /// <summary>消息类型（1=Invocation, 2=StreamItem, 3=Completion, 6=Ping, 7=Close）</summary>
    public int MessageType { get; set; }

    /// <summary>目标方法名（Invocation）</summary>
    public string? Target { get; set; }

    /// <summary>调用 Id（StreamItem/Completion）</summary>
    public string? InvocationId { get; set; }

    /// <summary>MemoryPack 序列化的参数数组</summary>
    public byte[]? Arguments { get; set; }

    /// <summary>错误信息（Completion/Close）</summary>
    public string? Error { get; set; }

    /// <summary>是否有返回值（Completion）</summary>
    public bool HasResult { get; set; }
}
