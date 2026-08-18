using System.Buffers;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;

namespace MesAdmin.Application.Tests;

/// <summary>
/// OpcUaPlcClient.TryReadFrame 跨段读取测试。
/// 验证帧头/帧体/帧尾分布在多个 ReadOnlySequence segment 时仍能正确解析。
/// </summary>
public class OpcUaPlcClientFrameTests
{
    [Fact]
    public void TryReadFrame_SingleSegment_ShouldParse()
    {
        var frame = CreateValidFrame();
        var sequence = new ReadOnlySequence<byte>(frame);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
        Assert.Equal(EquipmentStatus.Running, snapshot.Status);
    }

    [Fact]
    public void TryReadFrame_SplitAtHeader_ShouldParse()
    {
        var frame = CreateValidFrame();
        // 0x55 在第一个 segment 末尾，0xAA 及后续在第二个 segment
        var first = new ReadOnlyMemory<byte>(frame[..1]);
        var second = new ReadOnlyMemory<byte>(frame[1..]);
        var sequence = CreateSequence(first, second);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
    }

    [Fact]
    public void TryReadFrame_SplitInMiddle_ShouldParse()
    {
        var frame = CreateValidFrame();
        // 前 30 字节在第一个 segment，剩余 47 字节在第二个 segment
        var first = new ReadOnlyMemory<byte>(frame[..30]);
        var second = new ReadOnlyMemory<byte>(frame[30..]);
        var sequence = CreateSequence(first, second);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
    }

    [Fact]
    public void TryReadFrame_SplitIntoManySegments_ShouldParse()
    {
        var frame = CreateValidFrame();
        var segments = new List<ReadOnlyMemory<byte>>();
        for (int i = 0; i < frame.Length; i += 10)
        {
            var length = Math.Min(10, frame.Length - i);
            segments.Add(new ReadOnlyMemory<byte>(frame.AsMemory(i, length).ToArray()));
        }
        var sequence = CreateSequence([.. segments]);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
    }

    [Fact]
    public void TryReadFrame_HeaderSplitAcrossSegments_ShouldParse()
    {
        var frame = CreateValidFrame();
        // 第一个 segment 只包含 0x55，第二个 segment 以 0xAA 开头
        var first = new ReadOnlyMemory<byte>(frame[..1]);
        var second = new ReadOnlyMemory<byte>(frame[1..]);
        var sequence = CreateSequence(first, second);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
    }

    [Fact]
    public void TryReadFrame_GarbageBeforeFrame_ShouldSkipGarbage()
    {
        var frame = CreateValidFrame();
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var combined = new byte[garbage.Length + frame.Length];
        garbage.CopyTo(combined, 0);
        frame.CopyTo(combined, garbage.Length);

        var sequence = new ReadOnlySequence<byte>(combined);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
    }

    [Fact]
    public void TryReadFrame_InvalidFrameFollowedByValidFrame_ShouldSkipInvalidAndParseValid()
    {
        var validFrame = CreateValidFrame();
        var invalidFrame = new byte[PlcFrameProtocol.FrameLength];
        invalidFrame[0] = PlcFrameProtocol.Header0;
        invalidFrame[1] = PlcFrameProtocol.Header1;
        // 帧尾错误
        invalidFrame[PlcFrameProtocol.TailOffset] = 0xFF;
        invalidFrame[PlcFrameProtocol.TailOffset + 1] = 0xFF;

        var combined = new byte[invalidFrame.Length + validFrame.Length];
        invalidFrame.CopyTo(combined, 0);
        validFrame.CopyTo(combined, invalidFrame.Length);

        var sequence = new ReadOnlySequence<byte>(combined);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);

        // 验证缓冲区已推进到有效帧之后
        Assert.Equal(0, sequence.Length);
    }

    [Fact]
    public void TryReadFrame_PartialFrame_ShouldReturnFalseAndPreserveBytes()
    {
        var frame = CreateValidFrame();
        var partial = new ReadOnlyMemory<byte>(frame[..30]);
        var sequence = new ReadOnlySequence<byte>(partial);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.False(result);
        Assert.Equal(30, sequence.Length);
    }

    [Fact]
    public void TryReadFrame_HeaderByteAtEnd_ShouldPreserveForNextRead()
    {
        // 有效帧后紧跟 0x55，应解析出帧并保留 0x55 等待下一次 ReadAsync 提供 0xAA
        var frame = CreateValidFrame();
        var data = new byte[frame.Length + 1];
        frame.CopyTo(data, 0);
        data[^1] = PlcFrameProtocol.Header0;
        var sequence = new ReadOnlySequence<byte>(data);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
        Assert.Equal(1, sequence.Length);
        Assert.Equal(PlcFrameProtocol.Header0, sequence.First.Span[0]);
    }

    [Fact]
    public void TryReadFrame_TailSplitAcrossSegments_ShouldParse()
    {
        var frame = CreateValidFrame();
        // 前 75 字节在第一个 segment，帧尾 2 字节在第二个 segment
        var first = new ReadOnlyMemory<byte>(frame[..75]);
        var second = new ReadOnlyMemory<byte>(frame[75..]);
        var sequence = CreateSequence(first, second);

        var result = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot);

        Assert.True(result);
        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
        Assert.Equal(0, sequence.Length);
    }

    [Fact]
    public void TryReadFrame_TwoFrames_ShouldParseFirstAndLeaveSecond()
    {
        var frame = CreateValidFrame();
        var combined = new byte[frame.Length * 2];
        frame.CopyTo(combined, 0);
        frame.CopyTo(combined, frame.Length);

        var sequence = new ReadOnlySequence<byte>(combined);

        var result1 = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot1);
        Assert.True(result1);
        Assert.Equal("EQ-TQ-01", snapshot1.EquipmentCode);

        var result2 = OpcUaPlcClient.TryReadFrame(ref sequence, out var snapshot2);
        Assert.True(result2);
        Assert.Equal("EQ-TQ-01", snapshot2.EquipmentCode);

        Assert.Equal(0, sequence.Length);
    }

    private static byte[] CreateValidFrame()
    {
        var snapshot = PlcSnapshot.Create(
            "EQ-TQ-01",
            DateTimeOffset.UtcNow,
            EquipmentStatus.Running,
            cycleCount: 12345,
            goodCount: 12300,
            defectCount: 45,
            runTimeMs: 3600000,
            processValue: 22.5,
            processTag: "Torque-M6-FL");

        var buffer = new byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);
        return buffer;
    }

    private static ReadOnlySequence<byte> CreateSequence(params ReadOnlyMemory<byte>[] segments)
    {
        if (segments.Length == 0)
            return ReadOnlySequence<byte>.Empty;

        if (segments.Length == 1)
            return new ReadOnlySequence<byte>(segments[0]);

        var first = new BufferSegment(segments[0]);
        var current = first;
        for (int i = 1; i < segments.Length; i++)
        {
            var next = new BufferSegment(segments[i]);
            current.SetNext(next);
            current = next;
        }

        return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
    }

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public void SetNext(BufferSegment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }
}
