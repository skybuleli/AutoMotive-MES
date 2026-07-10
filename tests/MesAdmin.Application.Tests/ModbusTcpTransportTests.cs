using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

public class ModbusTcpTransportTests
{
    [Fact]
    public async Task RealClientMode_ShouldReadRegistersFromTcpEndpoint()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = RunSingleReadServerAsync(listener, cts.Token);

        var equipment = new[]
        {
            Equipment.Create(Ulid.NewUlid(), "EQ-FLS-01", "ECU 刷写台", 5, "刷写台", $"127.0.0.1:{port}")
        };
        var transport = new ModbusTcpPlcTransport(
            equipment,
            NullLogger<ModbusTcpPlcTransport>.Instance,
            useRealClient: true,
            pollIntervalMs: 50);

        await transport.StartAsync(cts.Token);
        var read = await transport.Reader.ReadAsync(cts.Token);
        var buffer = read.Buffer;
        var frame = buffer.FirstSpan[..PlcFrameProtocol.FrameLength];
        var reader = new PlcFrameReader(frame);

        Assert.True(reader.TryParse(out var snapshot));
        Assert.Equal("EQ-FLS-01", snapshot.EquipmentCode);
        Assert.Equal(EquipmentStatus.Running, snapshot.Status);
        Assert.Equal(7, snapshot.CycleCount);
        Assert.Equal(6, snapshot.GoodCount);
        Assert.Equal(1, snapshot.DefectCount);
        Assert.Equal(1234, snapshot.RunTimeMs);
        Assert.Equal(42.1, snapshot.ProcessValue, precision: 1);
        Assert.Equal("CanLatency", snapshot.ProcessTag);

        transport.Reader.AdvanceTo(buffer.End);
        await transport.StopAsync();
        await server;
    }

    private static async Task RunSingleReadServerAsync(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        var request = new byte[12];
        await stream.ReadExactlyAsync(request, ct);

        var response = new byte[25];
        request.AsSpan(0, 2).CopyTo(response);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), 19);
        response[6] = 1;
        response[7] = 0x03;
        response[8] = 16;
        WriteRegister(response, 0, 0);
        WriteRegister(response, 1, 7);
        WriteRegister(response, 2, 6);
        WriteRegister(response, 3, 1);
        WriteRegister(response, 4, 1234);
        WriteRegister(response, 5, 0);
        WriteRegister(response, 6, 421);
        WriteRegister(response, 7, 1);
        await stream.WriteAsync(response, ct);
    }

    private static void WriteRegister(byte[] response, int index, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(9 + index * 2, 2), value);
}
