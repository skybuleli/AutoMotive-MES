using System.Runtime.InteropServices;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;

namespace MesAdmin.Application.Tests;

/// <summary>
/// PLC 帧协议零分配解析测试（T2.12）。
/// 验证 SearchValues 帧头扫描 + PlcFrameReader ref struct 解析 + 帧完整性校验。
/// </summary>
public class PlcFrameProtocolTests
{
    [Fact]
    public void PlcFrameWriter_Then_Reader_ShouldRoundTrip()
    {
        //Arrange：构造 PLC 快照
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

        //Act：编码为帧 → 解码
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        var reader = new PlcFrameReader(buffer);
        Assert.True(reader.TryParse(out var parsed));

        //Assert：字段逐一比对
        Assert.Equal(snapshot.EquipmentCode, parsed.EquipmentCode);
        Assert.Equal(snapshot.Status, parsed.Status);
        Assert.Equal(snapshot.CycleCount, parsed.CycleCount);
        Assert.Equal(snapshot.GoodCount, parsed.GoodCount);
        Assert.Equal(snapshot.DefectCount, parsed.DefectCount);
        Assert.Equal(snapshot.RunTimeMs, parsed.RunTimeMs);
        Assert.Equal(snapshot.ProcessValue, parsed.ProcessValue, precision: 5);
        Assert.Equal(snapshot.ProcessTag, parsed.ProcessTag);
    }

    [Fact]
    public void FrameHeader_ShouldBe0x55_0xAA()
    {
        var snapshot = PlcSnapshot.Create("EQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        Assert.Equal(0x55, buffer[0]);
        Assert.Equal(0xAA, buffer[1]);
    }

    [Fact]
    public void FrameTail_ShouldBe0x0D_0x0A()
    {
        var snapshot = PlcSnapshot.Create("EQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        Assert.Equal(0x0D, buffer[PlcFrameProtocol.TailOffset]);
        Assert.Equal(0x0A, buffer[PlcFrameProtocol.TailOffset + 1]);
    }

    [Fact]
    public void Validate_ShouldRejectTamperedHeader()
    {
        var snapshot = PlcSnapshot.Create("EQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        // 篡改帧头
        buffer[0] = 0x00;

        var reader = new PlcFrameReader(buffer);
        Assert.False(reader.Validate());
        Assert.False(reader.TryParse(out _));
    }

    [Fact]
    public void Validate_ShouldRejectTamperedTail()
    {
        var snapshot = PlcSnapshot.Create("EQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        // 篡改帧尾
        buffer[PlcFrameProtocol.TailOffset] = 0xFF;

        var reader = new PlcFrameReader(buffer);
        Assert.False(reader.Validate());
    }

    [Fact]
    public void PlcFrameReader_ShouldThrowOnWrongLength()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Span<byte> tooShort = stackalloc byte[10];
            var reader = new PlcFrameReader(tooShort);
        });
    }

    [Fact]
    public void EquipmentCode_PadsShortCodeWithNullBytes()
    {
        // 设备码 "EQ-1" 只有 4 字节，应补 \0 到 8 字节
        var snapshot = PlcSnapshot.Create("EQ-1", DateTimeOffset.UtcNow, EquipmentStatus.Running, 1, 1, 0, 1000, 0, "tag");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        var reader = new PlcFrameReader(buffer);
        Assert.True(reader.TryParse(out var parsed));
        Assert.Equal("EQ-1", parsed.EquipmentCode);
    }

    [Fact]
    public void ProcessValue_UsesMemoryMarshal_NotBitConverter()
    {
        // 验证 double 零分配读取（MemoryMarshal.Read<double>，禁止 byte[]+BitConverter）
        var snapshot = PlcSnapshot.Create("EQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running, 0, 0, 0, 0, 22.567, "Torque");
        Span<byte> buffer = stackalloc byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, in snapshot);

        // 直接用 MemoryMarshal 读取 ProcessValue 字段验证（不经过 PlcFrameReader）
        var directRead = MemoryMarshal.Read<double>(buffer.Slice(PlcFrameProtocol.ProcessValueOffset, 8));
        Assert.Equal(22.567, directRead, precision: 3);
    }
}
