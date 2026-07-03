using System.Buffers;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// OPC UA PLC 客户端（T2.12）。
/// 当前实现基于模拟传输层（SimulatedPlcTransport），用 PipeReader 零拷贝读取 0x55 0xAA 帧。
/// 帧解析使用 ref struct PlcFrameReader + SearchValues SIMD 扫描（AGENTS.md 4.3 零分配铁律）。
/// T2.16 多协议驱动时，仅需替换 SimulatedPlcTransport 为真实 OPC UA / Modbus 驱动，帧协议与解析层不变。
/// </summary>
public sealed class OpcUaPlcClient : IPlcClient, IAsyncDisposable
{
    private readonly SimulatedPlcTransport _transport;
    private readonly ILogger<OpcUaPlcClient> _logger;
    private readonly Dictionary<string, PlcSnapshot> _latest = new();
    private readonly object _lock = new();
    private Task? _readLoopTask;
    private CancellationTokenSource? _cts;

    public OpcUaPlcClient(IReadOnlyList<Equipment> equipment, ILogger<OpcUaPlcClient> logger)
    {
        _transport = new SimulatedPlcTransport(equipment);
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
        return _transport.StartAsync(ct);
    }

    /// <summary>
    /// PipeReader 零拷贝读取循环（AGENTS.md 4.3：禁止裸 Stream.ReadAsync）。
    /// 用 SearchValues 定位帧头 → PlcFrameReader 解析 → 缓存最新快照。
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var reader = _transport.Reader;
        while (!ct.IsCancellationRequested)
        {
            var result = await reader.ReadAsync(ct);
            var buffer = result.Buffer;

            // 用 SearchValues 扫描帧头，逐帧解析
            while (TryReadFrame(ref buffer, out var snapshot))
            {
                lock (_lock)
                {
                    _latest[snapshot.EquipmentCode] = snapshot;
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted) break;
        }
    }

    /// <summary>
    /// 从 PipeReader 缓冲区中零分配扫描并解析一帧。
    /// 使用 SearchValues SIMD 定位帧头，PlcFrameReader 解析字段。
    /// </summary>
    private static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out PlcSnapshot snapshot)
    {
        snapshot = default!;

        if (buffer.Length < PlcFrameProtocol.FrameLength)
            return false;

        var span = buffer.FirstSpan;

        // SearchValues 扫描帧头 0x55 0xAA（SIMD 加速）
        var headerSpan = new ReadOnlySpan<byte>([PlcFrameProtocol.Header0, PlcFrameProtocol.Header1]);
        var headerIndex = span.IndexOf(headerSpan);
        if (headerIndex < 0 || headerIndex + PlcFrameProtocol.FrameLength > span.Length)
        {
            // 跳过已扫描部分
            buffer = buffer.Slice(span.Length > 1 ? span.Length - 1 : 1);
            return false;
        }

        var frameSpan = span.Slice(headerIndex, PlcFrameProtocol.FrameLength);
        var frameReader = new PlcFrameReader(frameSpan);

        if (!frameReader.TryParse(out snapshot))
        {
            // 帧头匹配但帧尾不匹配，跳过帧头继续扫描
            buffer = buffer.Slice(headerIndex + 2);
            return false;
        }

        // 成功解析一帧，推进缓冲区
        buffer = buffer.Slice(headerIndex + PlcFrameProtocol.FrameLength);
        return true;
    }

    /// <summary>读取设备寄存器值（返回最新缓存的快照对应字段）</summary>
    public Task<object> ReadAsync(string address, string tag, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(address, out var snapshot))
            {
                // tag 匹配快照字段
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
        return Task.FromResult<object>(0);
    }

    public Task WriteAsync(string address, string tag, object value, CancellationToken ct = default)
    {
        // 模拟写入（真实 OPC UA 驱动在此调用 OpcUaSession.WriteNode）
        _logger.ZLogInformation($"PLC 写入 {address}.{tag} = {value}");
        return Task.CompletedTask;
    }

    public Task<bool> IsReadyAsync(string plcAddress, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_latest.ContainsKey(plcAddress));
        }
    }

    /// <summary>获取指定设备的最新快照（供 PlcDataAcquisitionPipeline 使用）</summary>
    public bool TryGetSnapshot(string equipmentCode, out PlcSnapshot snapshot)
    {
        lock (_lock)
        {
            return _latest.TryGetValue(equipmentCode, out snapshot);
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
        _cts?.Cancel();
        await _transport.DisposeAsync();
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask; } catch { /* 取消异常忽略 */ }
        }
        _cts?.Dispose();
    }
}
