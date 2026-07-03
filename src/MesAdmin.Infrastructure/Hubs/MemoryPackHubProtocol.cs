using System.Buffers;
using MemoryPack;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace MesAdmin.Infrastructure.Hubs;

/// <summary>
/// MemoryPack SignalR 协议（T2.15，AGENTS.md 4.4 铁律：SignalR 强制 MemoryPack 二进制，禁止 JSON）。
/// 因 Cysharp 未提供 MemoryPack SignalR 协议 NuGet 包，基于 MemoryPackSerializer 自定义实现 IHubProtocol。
/// 序列化 HubMessage 为 MemoryPack 二进制，替代默认的 JSON 协议。
/// </summary>
public class MemoryPackHubProtocol : IHubProtocol
{
    private const string ProtocolName = "memorypack";

    public string Name => ProtocolName;
    public TransferFormat TransferFormat => TransferFormat.Binary;
    public int Version => 1;

    public bool IsVersionSupported(int version) => version == 1;

    public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage? message)
    {
        try
        {
            var inputSpan = input.IsSingleSegment ? input.FirstSpan : input.ToArray();
            var envelope = MemoryPackSerializer.Deserialize<HubMessageEnvelope>(inputSpan);
            if (envelope is null)
            {
                message = null;
                return false;
            }

            message = envelope.MessageType switch
            {
                1 => ParseInvocation(envelope),
                6 => PingMessage.Instance,
                7 => new CloseMessage(envelope.Error ?? string.Empty),
                _ => null,
            };

            return message is not null;
        }
        catch
        {
            message = null;
            return false;
        }
    }

    private static HubMessage? ParseInvocation(HubMessageEnvelope envelope)
    {
        if (envelope.Target is null || envelope.Arguments is null)
            return null;

        // 简化：参数以 object[] 反序列化（MemoryPack）
        var args = MemoryPackSerializer.Deserialize<object[]>(envelope.Arguments) ?? [];
        return new InvocationMessage(envelope.Target, args);
    }

    public void WriteMessage(HubMessage message, IBufferWriter<byte> output)
    {
        HubMessageEnvelope envelope;
        switch (message)
        {
            case InvocationMessage inv:
                envelope = new HubMessageEnvelope
                {
                    MessageType = 1,
                    Target = inv.Target,
                    Arguments = MemoryPackSerializer.Serialize(inv.Arguments),
                };
                break;
            case StreamItemMessage si:
                envelope = new HubMessageEnvelope
                {
                    MessageType = 2,
                    InvocationId = si.InvocationId,
                    Arguments = MemoryPackSerializer.Serialize(si.Item),
                };
                break;
            case CompletionMessage cm:
                envelope = new HubMessageEnvelope
                {
                    MessageType = 3,
                    InvocationId = cm.InvocationId,
                    Arguments = cm.HasResult ? MemoryPackSerializer.Serialize(cm.Result) : null,
                    Error = cm.Error,
                    HasResult = cm.HasResult,
                };
                break;
            case PingMessage:
                envelope = new HubMessageEnvelope { MessageType = 6 };
                break;
            case CloseMessage cm:
                envelope = new HubMessageEnvelope { MessageType = 7, Error = cm.Error };
                break;
            default:
                envelope = new HubMessageEnvelope();
                break;
        }

        var bytes = MemoryPackSerializer.Serialize(envelope);
        output.Write(bytes);
    }

    public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteMessage(message, writer);
        return writer.WrittenSpan.ToArray();
    }
}
