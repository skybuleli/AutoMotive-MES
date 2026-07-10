using System.Buffers.Binary;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using Xunit;

namespace MesAdmin.Application.Tests;

public class PlcDriverRealImplementationsTests
{
    [Fact]
    public void OpcUa_ToSnapshot_MapsNodeValuesCorrectly()
    {
        var values = new Dictionary<string, object?>
        {
            ["Status"] = 0,
            ["CycleCount"] = 123L,
            ["GoodCount"] = 118L,
            ["DefectCount"] = 5L,
            ["RunTimeMs"] = 615000L,
            ["ProcessValue"] = 22.4,
            ["ProcessTag"] = "Torque-M6-FL",
        };

        var snapshot = OpcUaPlcTransport.ToSnapshot("EQ-TQ-01", values);

        Assert.Equal("EQ-TQ-01", snapshot.EquipmentCode);
        Assert.Equal(EquipmentStatus.Running, snapshot.Status);
        Assert.Equal(123, snapshot.CycleCount);
        Assert.Equal(118, snapshot.GoodCount);
        Assert.Equal(5, snapshot.DefectCount);
        Assert.Equal(615000, snapshot.RunTimeMs);
        Assert.Equal(22.4, snapshot.ProcessValue, 3);
        Assert.Equal("Torque-M6-FL", snapshot.ProcessTag);
    }

    [Fact]
    public void OpcUa_ToSnapshot_CoercesIntegerVariants()
    {
        var values = new Dictionary<string, object?>
        {
            ["Status"] = (short)2,
            ["CycleCount"] = 10,
            ["GoodCount"] = 9,
            ["DefectCount"] = 1,
            ["RunTimeMs"] = 5000,
            ["ProcessValue"] = 45.0,
            ["ProcessTag"] = "Torque-M8-FL",
        };

        var snapshot = OpcUaPlcTransport.ToSnapshot("EQ-TQ-02", values);

        Assert.Equal(EquipmentStatus.Alarm, snapshot.Status);
        Assert.Equal(10, snapshot.CycleCount);
        Assert.Equal("Torque-M8-FL", snapshot.ProcessTag);
    }

    [Fact]
    public void OpcUa_ToSnapshot_DefaultsWhenMissing()
    {
        var snapshot = OpcUaPlcTransport.ToSnapshot("EQ-FT-01", new Dictionary<string, object?>());
        Assert.Equal(EquipmentStatus.Running, snapshot.Status);
        Assert.Equal(0, snapshot.CycleCount);
        Assert.Equal("Generic", snapshot.ProcessTag);
    }

    [Fact]
    public void EthernetIp_ParseReadTagResponse_ParsesDint()
    {
        var body = new byte[4 + 2 + 16];
        var cip = body.AsSpan(6);
        cip[0] = 0xCC;
        cip[1] = 0x00;
        cip[2] = 0x00;
        cip[3] = 0xC4;
        BinaryPrimitives.WriteInt32LittleEndian(cip.Slice(4, 4), 4242);

        var value = EthernetIpPlcTransport.ParseReadTagResponse(body);
        Assert.Equal(4242, value);
    }

    [Fact]
    public void EthernetIp_ParseReadTagResponse_ParsesReal()
    {
        var body = new byte[4 + 2 + 16];
        var cip = body.AsSpan(6);
        cip[0] = 0xCC;
        cip[1] = 0x00;
        cip[2] = 0x00;
        cip[3] = 0xCA;
        BinaryPrimitives.WriteSingleLittleEndian(cip.Slice(4, 4), 152.5f);

        var value = EthernetIpPlcTransport.ParseReadTagResponse(body);
        Assert.Equal(152.5f, (float)value!);
    }

    [Fact]
    public void EthernetIp_ParseReadTagResponse_ReturnsNullOnError()
    {
        var body = new byte[4 + 2 + 8];
        var cip = body.AsSpan(6);
        cip[0] = 0xCC;
        cip[1] = 0x04;
        cip[2] = 0x00;

        Assert.Null(EthernetIpPlcTransport.ParseReadTagResponse(body));
    }

    [Fact]
    public void EthernetIp_ParseReadTagResponse_ReturnsNullOnWrongService()
    {
        var body = new byte[4 + 2 + 8];
        var cip = body.AsSpan(6);
        cip[0] = 0xCD;
        cip[1] = 0x00;

        Assert.Null(EthernetIpPlcTransport.ParseReadTagResponse(body));
    }

    [Fact]
    public void EthernetIp_BuildReadTagRequest_HasCorrectEncapsulation()
    {
        var packet = EthernetIpPlcTransport.BuildReadTagRequest(0x12345678, "TotalCycles");

        Assert.Equal(0x006F, BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(0, 2)));
        Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
        Assert.Equal(0x4C, packet[24 + 6]);
        Assert.Equal(0x91, packet[24 + 6 + 2]);
        Assert.Equal((byte)"TotalCycles".Length, packet[24 + 6 + 3]);
        var encapsulatedLen = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2));
        Assert.True(encapsulatedLen > 0);
    }
}
