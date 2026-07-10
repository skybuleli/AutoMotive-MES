using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// EtherNet/IP 传输层（T2.16 多协议驱动）。
/// 适用于液压测试台（Rockwell / Allen-Bradley CompactLogix 等 PLC 控制）。
/// 使用 CIP (Common Industrial Protocol) over TCP，手工实现封装协议（无第三方库）。
///
/// 设备映射：
///   - EQ-HYD-01 (液压测试台) — EtherNet/IP, 端口 44818
///
/// CIP 标签映射（与 PLC 程序一致）：
///   - DeviceStatus   INT   (0=Idle, 1=Run, 2=Fault, 3=Offline)
///   - TotalCycles    DINT
///   - GoodParts      DINT
///   - DefectParts    DINT
///   - RunTime_MS     DINT
///   - Hydraulic_Pressure REAL (bar)
///   - LeakRate_CC_HR REAL (CC/hr)
///
/// 真实模式（Plc:Drivers:EthernetIp:Enabled=true）：
///   TCP 连接 → RegisterSession → 周期 SendRRData 读标签 → 解析 → 写 PlcSnapshot 帧。
///   连接失败按退避重连，不降级到模拟。
/// 开发模式（默认）：模拟数据生成。
/// </summary>
public sealed class EthernetIpPlcTransport : IPlcTransport
{
    private readonly Pipe _pipe = new(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly ILogger<EthernetIpPlcTransport> _logger;
    private readonly bool _useRealClient;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _isConnected;

    private static readonly HashSet<string> EthernetIpEquipmentCodes =
    [
        "EQ-HYD-01",  // 液压测试台 — EtherNet/IP
    ];

    public IReadOnlySet<string> SupportedEquipmentCodes => EthernetIpEquipmentCodes;
    public PipeReader Reader => _pipe.Reader;
    public string TransportName => "EtherNet-IP";
    public bool IsConnected => _isConnected;

    public EthernetIpPlcTransport(
        IReadOnlyList<Equipment> equipment,
        ILogger<EthernetIpPlcTransport> logger,
        bool useRealClient = false,
        int pollIntervalMs = 500)
    {
        _allEquipment = equipment;
        _logger = logger;
        _useRealClient = useRealClient;
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_useRealClient)
            _runTask = RunRealAsync(_cts.Token);
        else
            _runTask = SimulateFramesAsync(_cts.Token);

        _isConnected = _useRealClient;
        _logger.ZLogInformation($"EtherNet/IP 传输层启动（{(_useRealClient ? "生产" : "模拟")}模式）");
        return Task.CompletedTask;
    }

    private async Task RunRealAsync(CancellationToken ct)
    {
        var devices = _allEquipment.Where(e => EthernetIpEquipmentCodes.Contains(e.EquipmentCode)).ToList();
        var tasks = devices.Select(dev => PollDeviceAsync(dev, ct));
        await Task.WhenAll(tasks);
    }

    private static (string Host, int Port) ParseEndpoint(Equipment eq)
    {
        var s = eq.PlcAddress;
        foreach (var scheme in new[] { "opc.tcp://", "tcp://", "ethernetip://" })
            if (s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                s = s[scheme.Length..];
        var parts = s.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 44818;
        return (host, port);
    }

    private async Task PollDeviceAsync(Equipment eq, CancellationToken ct)
    {
        var (host, port) = ParseEndpoint(eq);
        while (!ct.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, ct);
                var stream = tcp.GetStream();

                var sessionHandle = await RegisterSessionAsync(stream, ct);
                _isConnected = true;
                _logger.ZLogInformation($"EtherNet/IP 已连接 {eq.EquipmentCode} {host}:{port} (session {sessionHandle})");

                while (!ct.IsCancellationRequested && tcp.Connected)
                {
                    var status = ReadTagIntAsync(stream, sessionHandle, "DeviceStatus", ct);
                    var cycle = ReadTagIntAsync(stream, sessionHandle, "TotalCycles", ct);
                    var good = ReadTagIntAsync(stream, sessionHandle, "GoodParts", ct);
                    var defect = ReadTagIntAsync(stream, sessionHandle, "DefectParts", ct);
                    var runMs = ReadTagIntAsync(stream, sessionHandle, "RunTime_MS", ct);
                    var pressure = ReadTagRealAsync(stream, sessionHandle, "Hydraulic_Pressure", ct);
                    var leak = ReadTagRealAsync(stream, sessionHandle, "LeakRate_CC_HR", ct);
                    await Task.WhenAll(status, cycle, good, defect, runMs, pressure, leak);

                    var estatus = (status.Result, leak.Result) switch
                    {
                        (0, _) => EquipmentStatus.Idle,
                        (1, _) => EquipmentStatus.Running,
                        (2, _) => EquipmentStatus.Alarm,
                        (3, _) => EquipmentStatus.Offline,
                        _ => EquipmentStatus.Running,
                    };

                    // 交替输出压力 / 泄漏率作为过程值
                    var isEven = (cycle.Result & 1) == 0;
                    var snapshot = PlcSnapshot.Create(
                        eq.EquipmentCode, DateTimeOffset.UtcNow, estatus,
                        cycle.Result, good.Result, defect.Result, runMs.Result,
                        isEven ? pressure.Result : leak.Result,
                        isEven ? "HydraulicPressure" : "LeakRate");

                    await WriteSnapshotFrameAsync(snapshot, ct);
                    await Task.Delay(_pollInterval, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.ZLogError($"EtherNet/IP 读取 {eq.EquipmentCode} 失败：{ex.Message}（2s 后重连）");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                try { tcp?.Dispose(); } catch { }
            }
        }
    }

    // ── CIP 封装协议（EtherNet/IP Encapsulation）──

    private static async Task<uint> RegisterSessionAsync(NetworkStream stream, CancellationToken ct)
    {
        var packet = new byte[24];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), 0x0065); // RegisterSession
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), 4);       // length
        // session/status/context/options 全 0，data = [0x01,0x00,0x00,0x00] (protocol version 1)
        packet[20] = 0x01;
        await stream.WriteAsync(packet, ct);
        var resp = new byte[28];
        await ReadExactlyAsync(stream, resp, ct);
        return BinaryPrimitives.ReadUInt32LittleEndian(resp.AsSpan(4, 4));
    }

    public static byte[] BuildReadTagRequest(uint sessionHandle, string tag)
    {
        var tagBytes = Encoding.ASCII.GetBytes(tag);
        var pad = (tagBytes.Length & 1) == 0 ? 0 : 1;
        var pathLen = 2 + tagBytes.Length + pad;            // 0x91 + len + ascii
        var pathSizeWords = (byte)(pathLen / 2);
        var cipLen = 1 + 1 + pathLen + 2;                   // service + pathSize + path + elementCount(2)
        var encapsulatedLen = 4 + 2 + cipLen;               // iface(4) + timeout(2) + cip
        var total = 24 + encapsulatedLen;

        var packet = new byte[total];
        var p = packet.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(p[0..2], 0x006F);            // SendRRData
        BinaryPrimitives.WriteUInt16BigEndian(p[2..4], (ushort)encapsulatedLen);
        BinaryPrimitives.WriteUInt32LittleEndian(p[4..8], sessionHandle);
        BinaryPrimitives.WriteUInt32LittleEndian(p[16..20], 0);          // options
        // encapsulated data
        var e = 24;
        BinaryPrimitives.WriteUInt32LittleEndian(p[e..(e + 4)], 0);      // interface handle
        BinaryPrimitives.WriteUInt16BigEndian(p[(e + 4)..(e + 6)], 0x000A); // timeout
        var c = e + 6;
        p[c] = 0x4C;                                               // Read Tag service
        p[c + 1] = pathSizeWords;
        p[c + 2] = 0x91;                                           // symbolic segment
        p[c + 3] = (byte)tagBytes.Length;
        tagBytes.CopyTo(p[(c + 4)..]);
        p[c + 4 + tagBytes.Length + pad] = 0x01;                   // element count = 1
        return packet;
    }

    private static async Task<ReadOnlyMemory<byte>> SendRrDataAsync(NetworkStream stream, byte[] request, CancellationToken ct)
    {
        await stream.WriteAsync(request, ct);
        var header = new byte[24];
        await ReadExactlyAsync(stream, header, ct);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        var body = new byte[length];
        await ReadExactlyAsync(stream, body, ct);
        return body;
    }

    private static async Task<int> ReadTagIntAsync(NetworkStream stream, uint session, string tag, CancellationToken ct)
    {
        var resp = await SendRrDataAsync(stream, BuildReadTagRequest(session, tag), ct);
        return (int)(ParseReadTagResponse(resp.Span) ?? 0);
    }

    private static async Task<double> ReadTagRealAsync(NetworkStream stream, uint session, string tag, CancellationToken ct)
    {
        var resp = await SendRrDataAsync(stream, BuildReadTagRequest(session, tag), ct);
        return (double)(ParseReadTagResponse(resp.Span) ?? 0.0);
    }

    /// <summary>
    /// 纯函数：解析 CIP Read Tag 响应，提取数值（与连接解耦，便于单元测试）。
    /// 返回 null 表示解析失败或 General Status 非零。
    /// </summary>
    public static object? ParseReadTagResponse(ReadOnlySpan<byte> encapsulatedData)
    {
        // encapsulatedData = [InterfaceHandle 4][Timeout 2][CIP...]
        if (encapsulatedData.Length < 8) return null;
        var cip = encapsulatedData[6..];
        if (cip.Length < 4) return null;
        if (cip[0] != 0xCC) return null;            // Read Tag 成功响应服务码
        var generalStatus = cip[1];
        if (generalStatus != 0) return null;
        var addStatusSize = cip[2];
        var dataStart = 3 + addStatusSize * 2;
        if (cip.Length <= dataStart) return null;
        var typeCode = cip[dataStart];
        var v = cip[(dataStart + 1)..];
        return typeCode switch
        {
            0xC2 => v.Length >= 1 ? v[0] : null,                         // SINT
            0xC3 => v.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(v[..2]) : null,  // INT
            0xC4 => v.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(v[..4]) : null,  // DINT
            0xC5 => v.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(v[..8]) : null,  // LINT
            0xC6 => v.Length >= 1 ? v[0] : null,                         // USINT
            0xC7 => v.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(v[..2]) : null, // UINT
            0xC8 => v.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(v[..4]) : null, // UDINT
            0xCA => v.Length >= 4 ? BinaryPrimitives.ReadSingleLittleEndian(v[..4]) : null,  // REAL
            _ => null,
        };
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0) throw new IOException("EtherNet/IP 连接已关闭");
            offset += read;
        }
    }

    private async Task WriteSnapshotFrameAsync(PlcSnapshot snapshot, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(PlcFrameProtocol.FrameLength);
        try
        {
            var written = PlcFrameWriter.Write(buffer, in snapshot);
            var flushResult = await _pipe.Writer.WriteAsync(buffer.AsMemory(0, written), ct);
            if (flushResult.IsCompleted) return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>开发模式：模拟液压测试台数据帧</summary>
    private async Task SimulateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _allEquipment.Where(e => EthernetIpEquipmentCodes.Contains(e.EquipmentCode)))
            {
                var cycle = Random.Shared.Next(1, 100000);
                var pressure = 150.0 + Random.Shared.NextDouble() * 10 - 5;
                var leakRate = Random.Shared.NextDouble() * 0.5;
                var (processValue, processTag) = (cycle & 1) == 0 ? (pressure, "HydraulicPressure") : (leakRate, "LeakRate");

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now, EquipmentStatus.Running,
                    cycle, (long)(cycle * 0.96), (long)(cycle * 0.04),
                    cycle * 500L, processValue, processTag);

                await WriteSnapshotFrameAsync(snapshot, ct);
            }

            try { await Task.Delay(_pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task StopAsync()
    {
        _isConnected = false;
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        _pipe.Writer.Complete();
        cts?.Dispose();
        return Task.CompletedTask;
    }

    public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default)
    {
        if (_useRealClient)
            _logger.ZLogWarning($"EtherNet/IP 直接读寄存器 {address}.{tag} 需在活动连接中执行（建议通过实时订阅）");
        return Task.FromResult<object>(0);
    }

    public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        if (_useRealClient)
            _logger.ZLogWarning($"EtherNet/IP 直接写寄存器 {address}.{tag} 需在活动连接中执行");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
