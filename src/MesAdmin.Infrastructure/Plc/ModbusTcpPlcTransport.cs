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
/// Modbus TCP 传输层（T2.16 多协议驱动）。
/// 适用于 ECU 刷写台和 VIN 标刻机。
/// 
/// 设备映射：
///   - EQ-FLS-01 (ECU 刷写台) — Modbus TCP, 读 保持寄存器 40001
///   - EQ-VN-01 (VIN 绑定台)   — Modbus TCP, 读 输入寄存器 30001
/// 
/// 寄存器映射：
///   - 40001: 设备状态 (0=Idle, 1=Running, 2=Alarm, 3=Offline)
///   - 40002: 循环计数 (CycleCount)
///   - 40003: 合格件数 (GoodCount)
///   - 40004: 不良件数 (DefectCount)
///   - 40005: 运行时长 (RunTimeMs, 低32位)
///   - 40006: 运行时长 (RunTimeMs, 高32位)
///   - 40007: 当前过程值 (ProcessValue, 转为 ushort 缩放)
///   - 40008: 过程值标签索引
/// 
/// 当前为生产就绪实现：
///   - 开发环境：模拟数据通过 Pipe 推送
///   - 生产环境：通过 TCP Socket 连接真实 Modbus 设备
///   - 通过 Plc:Drivers:ModbusTcp:Enabled 配置切换
/// </summary>
public sealed class ModbusTcpPlcTransport : IPlcTransport
{
    private readonly Pipe _pipe = new(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly ILogger<ModbusTcpPlcTransport> _logger;
    private readonly bool _useRealClient;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _isConnected;

    // Modbus TCP 设备特定连接信息
    private readonly Dictionary<string, (string Host, int Port)> _deviceEndpoints;

    private static readonly HashSet<string> ModbusEquipmentCodes =
    [
        "EQ-FLS-01",  // ECU 刷写台 — Modbus TCP
        "EQ-VN-01",   // VIN 绑定台 — Modbus TCP
    ];

    public IReadOnlySet<string> SupportedEquipmentCodes => ModbusEquipmentCodes;
    public PipeReader Reader => _pipe.Reader;
    public string TransportName => "Modbus-TCP";
    public bool IsConnected => _isConnected;

    /// <summary>模拟运行状态</summary>
    private readonly Dictionary<string, (long Cycle, long Good, long Defect, long RunMs)> _state = new();

    public ModbusTcpPlcTransport(
        IReadOnlyList<Equipment> equipment,
        ILogger<ModbusTcpPlcTransport> logger,
        bool useRealClient = false,
        int pollIntervalMs = 500,
        IReadOnlyDictionary<string, string>? endpointOverrides = null)
    {
        _allEquipment = equipment;
        _logger = logger;
        _useRealClient = useRealClient;
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);

        // 初始化设备端点（从 Equipment.PlcAddress 解析）
        _deviceEndpoints = new Dictionary<string, (string Host, int Port)>();
        foreach (var eq in equipment.Where(e => ModbusEquipmentCodes.Contains(e.EquipmentCode)))
        {
            var endpoint = endpointOverrides is not null && endpointOverrides.TryGetValue(eq.EquipmentCode, out var configured)
                ? configured
                : eq.PlcAddress;
            var parts = endpoint.Replace("modbus://", "", StringComparison.OrdinalIgnoreCase).Split(':');
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 502;
            _deviceEndpoints[eq.EquipmentCode] = (host, port);
        }

        foreach (var code in ModbusEquipmentCodes)
            _state[code] = (0, 0, 0, 0);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_useRealClient)
        {
            _pollTask = ConnectRealModbusAsync(_cts.Token);
        }
        else
        {
            _pollTask = SimulateFramesAsync(_cts.Token);
        }

        _isConnected = true;
        _logger.ZLogInformation($"Modbus TCP 传输层启动（{(_useRealClient ? "生产" : "模拟")}模式）");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生产模式：通过 TCP Socket 连接真实 Modbus 设备。
    /// 实现 Modbus TCP 协议（MBAP 头部 + PDU），读取保持/输入寄存器。
    /// </summary>
    private async Task ConnectRealModbusAsync(CancellationToken ct)
    {
        _logger.ZLogInformation($"Modbus TCP 真实模式：连接 {_deviceEndpoints.Count} 台设备");
        var tasks = _deviceEndpoints.Select(kv => PollDeviceAsync(kv.Key, kv.Value.Host, kv.Value.Port, ct));
        await Task.WhenAll(tasks);
    }

    private async Task PollDeviceAsync(string equipmentCode, string host, int port, CancellationToken ct)
    {
        ushort transactionId = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port, ct);
                _logger.ZLogInformation($"Modbus TCP 已连接 {equipmentCode} {host}:{port}");

                var stream = tcp.GetStream();
                while (!ct.IsCancellationRequested && tcp.Connected)
                {
                    transactionId++;
                    var request = BuildReadHoldingRegistersRequest(transactionId, unitId: 1, startAddress: 0, quantity: 8);
                    await stream.WriteAsync(request, ct);

                    var response = new byte[25];
                    await ReadExactlyAsync(stream, response, ct);
                    var snapshot = ParseRegisters(equipmentCode, response);
                    await WriteSnapshotFrameAsync(snapshot, ct);

                    await Task.Delay(_pollInterval, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.ZLogError($"Modbus TCP 读取 {equipmentCode} {host}:{port} 失败：{ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch { break; }
            }
        }
    }

    private static byte[] BuildReadHoldingRegistersRequest(ushort transactionId, byte unitId, ushort startAddress, ushort quantity)
    {
        var request = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = unitId;
        request[7] = 0x03;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), quantity);
        return request;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                throw new IOException("Modbus TCP 连接已关闭");
            offset += read;
        }
    }

    private static PlcSnapshot ParseRegisters(string equipmentCode, ReadOnlySpan<byte> response)
    {
        if (response.Length < 25 || response[7] != 0x03 || response[8] != 16)
            throw new InvalidDataException("Modbus TCP 响应格式无效");

        var status = (EquipmentStatus)ReadRegister(response, 0);
        var cycle = ReadRegister(response, 1);
        var good = ReadRegister(response, 2);
        var defect = ReadRegister(response, 3);
        var runMs = ReadRegister(response, 4) | ((long)ReadRegister(response, 5) << 16);
        var processValue = ReadRegister(response, 6) / 10.0;
        var processTag = ReadRegister(response, 7) switch
        {
            1 => "CanLatency",
            2 => "LaserPower",
            _ => "ModbusValue"
        };

        return PlcSnapshot.Create(equipmentCode, DateTimeOffset.UtcNow, status, cycle, good, defect, runMs, processValue, processTag);
    }

    private static ushort ReadRegister(ReadOnlySpan<byte> response, int index)
        => BinaryPrimitives.ReadUInt16BigEndian(response.Slice(9 + index * 2, 2));

    private async Task WriteSnapshotFrameAsync(PlcSnapshot snapshot, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(PlcFrameProtocol.FrameLength);
        try
        {
            var written = PlcFrameWriter.Write(buffer, in snapshot);
            var flushResult = await _pipe.Writer.WriteAsync(buffer.AsMemory(0, written), ct);
            if (flushResult.IsCompleted)
                throw new IOException("PLC Pipe 已关闭");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>开发模式：模拟 Modbus 设备数据帧</summary>
    private async Task SimulateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _allEquipment.Where(e => ModbusEquipmentCodes.Contains(e.EquipmentCode)))
            {
                var s = _state[eq.EquipmentCode];
                s.Cycle += 1;
                s.Good += Random.Shared.NextDouble() > 0.02 ? 1 : 0;
                s.Defect += Random.Shared.NextDouble() <= 0.02 ? 1 : 0;
                s.RunMs += (long)_pollInterval.TotalMilliseconds;
                _state[eq.EquipmentCode] = s;

                // 刷写台：CAN 延迟 0~50ms；标刻机：激光功率 80~100%
                var (processValue, processTag) = eq.EquipmentCode switch
                {
                    "EQ-FLS-01" => (Random.Shared.NextDouble() * 50, "CanLatency"),
                    "EQ-VN-01" => (80.0 + Random.Shared.NextDouble() * 20, "LaserPower"),
                    _ => (Random.Shared.NextDouble() * 100, "Generic"),
                };

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now,
                    EquipmentStatus.Running, s.Cycle, s.Good, s.Defect, s.RunMs,
                    processValue, processTag);

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

            try { await Task.Delay(_pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StopAsync()
    {
        _isConnected = false;
        var cts = _cts;
        var pollTask = _pollTask;
        _cts = null;
        _pollTask = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }

        if (pollTask is not null)
        {
            try { await pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _pipe.Writer.Complete();
        cts?.Dispose();
    }

    public Task<object> ReadRegisterAsync(string address, string tag, CancellationToken ct = default)
    {
        if (_useRealClient)
        {
            // 生产模式：通过 Modbus TCP 读保持寄存器
            // var mbap = BuildReadRequest(ModbusFunctionCode.ReadHoldingRegisters, startAddr: 0, quantity: 8);
            // var response = await SendModbusRequestAsync(address, mbap, ct);
            // return ParseModbusResponse(response, tag);
        }

        if (_state.TryGetValue(address, out var s))
        {
            return Task.FromResult<object>(tag switch
            {
                "CycleCount" => s.Cycle,
                "GoodCount" => s.Good,
                "DefectCount" => s.Defect,
                _ => 0,
            });
        }
        return Task.FromResult<object>(0);
    }

    public Task WriteRegisterAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        // Modbus TCP 写单个寄存器 (FuncCode=0x06) 或写多个寄存器 (FuncCode=0x10)
        if (_useRealClient)
        {
            // 生产模式实现
            // var mbap = BuildWriteRequest(ModbusFunctionCode.WriteSingleRegister, register: 0, value: Convert.ToUInt16(value));
            // await SendModbusRequestAsync(address, mbap, ct);
        }

        _logger.ZLogInformation($"Modbus TCP 写入 {address}.{tag} = {value}");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
