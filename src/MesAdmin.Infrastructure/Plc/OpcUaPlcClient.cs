using System.Buffers;
using MesAdmin.Application.Observability;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// OPC UA PLC 客户端（T2.12/T2.16）。
/// 通过 IPlcTransport 读取 Pipe 数据，用 PlcFrameReader 零分配解析帧。
/// 传输层可以是：Simulated（开发）/ OPC UA / Modbus TCP / EtherNet/IP / Profinet。
/// 帧协议与解析层保持不变，仅替换传输层实现（策略模式）。
/// </summary>
public sealed class OpcUaPlcClient : IPlcClient, IAsyncDisposable
{
    private readonly PlcDriverFactory _driverFactory;
    private readonly ILogger<OpcUaPlcClient> _logger;
    private readonly Dictionary<string, PlcSnapshot> _latest = new();
    private readonly object _lock = new();
    private readonly HashSet<string> _realEquipmentCodes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cts;

    /// <summary>所有传输层的最新快照</summary>
    public IReadOnlyDictionary<string, PlcSnapshot> LatestSnapshots
    {
        get { lock (_lock) return new Dictionary<string, PlcSnapshot>(_latest); }
    }

    public OpcUaPlcClient(
        PlcDriverFactory driverFactory,
        ILogger<OpcUaPlcClient> logger)
    {
        _driverFactory = driverFactory;
        _logger = logger;

        // 预计算真实协议传输层覆盖的设备编码（用于阻止 Simulated 覆盖真实数据）
        foreach (var transport in _driverFactory.GetAllTransports())
        {
            if (transport is not SimulatedPlcTransport)
            {
                foreach (var code in transport.SupportedEquipmentCodes)
                    _realEquipmentCodes.Add(code);
            }
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        foreach (var transport in _driverFactory.GetAllTransports())
        {
            try
            {
                _ = transport.StartAsync(_cts.Token);

                // Simulated 传输层：仅处理未被真实协议覆盖的设备编码
                if (transport is SimulatedPlcTransport && _realEquipmentCodes.Count > 0)
                {
                    _ = ReadLoopAsync(transport, _cts.Token, isSimulated: true);
                    _logger.ZLogInformation($"已启动 {transport.TransportName} 读取循环（降级兜底，排除 {_realEquipmentCodes.Count} 台真实设备）");
                }
                else
                {
                    _ = ReadLoopAsync(transport, _cts.Token, isSimulated: false);
                    _logger.ZLogInformation($"已启动 {transport.TransportName} 读取循环");
                }
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"启动 {transport.TransportName} 失败");
            }
        }

        var realCount = _driverFactory.GetAllTransports().Count(t => t is not SimulatedPlcTransport);
        _logger.ZLogInformation($"OpcUaPlcClient 启动完成：{realCount} 个真实传输层 + 模拟降级");
        return Task.CompletedTask;
    }

    /// <summary>
    /// PipeReader 零拷贝读取循环（AGENTS.md 4.3：禁止裸 Stream.ReadAsync）。
    /// 从指定传输层的 PipeReader 读取字节流，SearchValues 定位帧头 → PlcFrameReader 解析。
    /// </summary>
    /// <param name="isSimulated">是否为 Simulated 传输层。为 true 时跳过已被真实协议覆盖的设备编码。</param>
    private async Task ReadLoopAsync(IPlcTransport transport, CancellationToken ct, bool isSimulated = false)
    {
        var reader = transport.Reader;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await reader.ReadAsync(ct);
                var buffer = result.Buffer;

                // 用 SearchValues 扫描帧头，逐帧解析
                while (TryReadFrame(ref buffer, out var snapshot))
                {
                    // Simulated 传输层：跳过已被真实协议覆盖的设备（避免数据竞争）
                    if (isSimulated && _realEquipmentCodes.Contains(snapshot.EquipmentCode))
                        continue;

                    lock (_lock)
                    {
                        _latest[snapshot.EquipmentCode] = snapshot;
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AutoMesMetrics.RecordPlcReadLoopError(transport.TransportName);
                _logger.ZLogError(ex, $"{transport.TransportName} 读取循环异常");
                // 短暂延迟后重试，避免空转
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>
    /// 从 PipeReader 缓冲区中零分配扫描并解析一帧。
    /// 使用 SequenceReader 在完整 ReadOnlySequence 上定位帧头，支持帧跨 ReadOnlySequence 多个 segment。
    /// 解析前将 77 字节帧复制到 stackalloc 连续缓冲区，再交给 PlcFrameReader 解析。
    /// </summary>
    internal static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out PlcSnapshot snapshot)
    {
        snapshot = default!;
        const int frameLength = PlcFrameProtocol.FrameLength;

        if (buffer.Length < frameLength)
            return false;

        var reader = new SequenceReader<byte>(buffer);
        Span<byte> frame = stackalloc byte[frameLength];

        while (!reader.End)
        {
            // 在完整 sequence 上定位帧头首字节 0x55
            // 注：SequenceReader 按字节扫描，跨段安全；77 字节帧场景下 SIMD 收益有限。
            if (!reader.TryAdvanceTo(PlcFrameProtocol.Header0, false))
                break;

            var frameStart = reader.Position;

            // 如果 0x55 是最后一个字节，保留到下一次 ReadAsync 再判断
            if (reader.Remaining < 2)
                return false;

            // 跳过 0x55 并验证下一字节为 0xAA
            reader.Advance(1);

            if (!reader.TryRead(out byte h1) || h1 != PlcFrameProtocol.Header1)
                continue;

            // 当前 reader 位于 0xAA 之后；检查剩余字节是否足够完整一帧
            if (reader.Remaining < frameLength - 2)
            {
                // 数据不足一帧，保留未消费字节等待下一次 ReadAsync
                return false;
            }

            // 将可能跨 segment 的帧复制到连续 stack 缓冲区
            var frameSequence = buffer.Slice(frameStart, frameLength);
            frameSequence.CopyTo(frame);

            var frameReader = new PlcFrameReader(frame);
            if (frameReader.TryParse(out snapshot))
            {
                // 成功解析，推进 reader 到帧尾并返回剩余缓冲区
                reader.Advance(frameLength - 2);
                buffer = buffer.Slice(reader.Position);
                return true;
            }

            // 帧头匹配但校验失败（帧尾/内容损坏），跳过整帧避免在损坏帧体内误识别新帧头
            AutoMesMetrics.RecordPlcFrameParseError();
            reader.Advance(frameLength - 2);
        }

        // 未找到有效帧：消费除最后 1 字节外的所有字节，保留可能的跨段帧头
        var consumed = buffer.Length > 1 ? buffer.Length - 1 : 0;
        buffer = buffer.Slice(consumed);
        return false;
    }

    /// <summary>读取设备寄存器值（返回最新缓存的快照对应字段）</summary>
    public Task<object> ReadAsync(string address, string tag, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(address, out var snapshot))
            {
                object value = tag switch
                {
                    "Status" => snapshot.Status,
                    "CycleCount" => snapshot.CycleCount,
                    "GoodCount" => snapshot.GoodCount,
                    "DefectCount" => snapshot.DefectCount,
                    "RunTimeMs" => snapshot.RunTimeMs,
                    "ProcessValue" => snapshot.ProcessValue,
                    _ => snapshot.ProcessValue,
                };
                return Task.FromResult(value);
            }
        }

        // 缓存未命中 → 通过传输层直接读取
        var transport = _driverFactory.GetTransport(address);
        return transport.ReadRegisterAsync(address, tag, ct);
    }

    public async Task WriteAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        var transport = _driverFactory.GetTransport(address);
        await transport.WriteRegisterAsync(address, tag, value, ct);
    }

    public Task<bool> IsReadyAsync(string plcAddress, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _latest.TryGetValue(plcAddress, out var snapshot)
                && snapshot.Status is EquipmentStatus.Running or EquipmentStatus.Idle);
        }
    }

    /// <summary>获取指定设备的最新快照（供 PlcDataAcquisitionPipeline 使用）</summary>
    public bool TryGetSnapshot(string equipmentCode, out PlcSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(equipmentCode, out var latest))
            {
                snapshot = latest;
                return true;
            }
            snapshot = null!;
            return false;
        }
    }

    /// <summary>获取所有已连接设备的最新快照</summary>
    public IReadOnlyList<PlcSnapshot> GetAllSnapshots()
    {
        lock (_lock)
        {
            return _latest.Values.ToList();
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException ex) { _logger.ZLogDebug(ex, $"OPC UA 客户端取消令牌已释放"); }
        if (cts is not null)
        {
            try { await Task.Delay(100); }
            catch (Exception ex) { _logger.ZLogDebug(ex, $"OPC UA 客户端停止等待异常"); }
            cts.Dispose();
        }
    }
}
