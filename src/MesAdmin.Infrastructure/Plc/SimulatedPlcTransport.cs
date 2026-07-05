using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Plc;

/// <summary>
/// PLC 帧编码器（零分配，写入 Span&lt;byte&gt;）。
/// 模拟传输层和真实驱动共用此编码器生成/解析帧。
/// </summary>
public static class PlcFrameWriter
{
    /// <summary>
    /// 将 PLC 快照编码为帧（写入目标 Span，长度必须 ≥ FrameLength）。
    /// 返回写入的字节数。
    /// </summary>
    public static int Write(Span<byte> destination, in PlcSnapshot snapshot)
    {
        if (destination.Length < PlcFrameProtocol.FrameLength)
            throw new ArgumentException($"目标缓冲区至少需要 {PlcFrameProtocol.FrameLength} 字节", nameof(destination));

        // 帧头
        destination[0] = PlcFrameProtocol.Header0;
        destination[1] = PlcFrameProtocol.Header1;

        // 设备码 8 字节 ASCII（不足补 \0）
        WriteAscii(destination.Slice(PlcFrameProtocol.EquipmentCodeOffset, 8), snapshot.EquipmentCode);

        // 状态 1 字节
        destination[PlcFrameProtocol.StatusOffset] = (byte)snapshot.Status;

        // 数值字段 little-endian（MemoryMarshal 零分配写入，需局部变量避免表达式引用传递）
        var cycleCount = snapshot.CycleCount;
        var goodCount = snapshot.GoodCount;
        var defectCount = snapshot.DefectCount;
        var runTimeMs = snapshot.RunTimeMs;
        var processValue = snapshot.ProcessValue;

        MemoryMarshal.Write(destination.Slice(PlcFrameProtocol.CycleCountOffset, 8), in cycleCount);
        MemoryMarshal.Write(destination.Slice(PlcFrameProtocol.GoodCountOffset, 8), in goodCount);
        MemoryMarshal.Write(destination.Slice(PlcFrameProtocol.DefectCountOffset, 8), in defectCount);
        MemoryMarshal.Write(destination.Slice(PlcFrameProtocol.RunTimeOffset, 8), in runTimeMs);
        MemoryMarshal.Write(destination.Slice(PlcFrameProtocol.ProcessValueOffset, 8), in processValue);

        // 过程标签 16 字节 ASCII
        WriteAscii(destination.Slice(PlcFrameProtocol.ProcessTagOffset, 16), snapshot.ProcessTag);

        // 帧尾
        destination[PlcFrameProtocol.TailOffset] = PlcFrameProtocol.Tail0;
        destination[PlcFrameProtocol.TailOffset + 1] = PlcFrameProtocol.Tail1;

        return PlcFrameProtocol.FrameLength;
    }

    /// <summary>写入 ASCII 字段，不足补 \0，超出截断</summary>
    private static void WriteAscii(Span<byte> destination, string value)
    {
        destination.Clear(); // 先清零（补 \0）
        var bytes = Encoding.ASCII.GetBytes(value.AsSpan(), destination);
    }
}

/// <summary>
/// 模拟 PLC 传输层（T2.12 开发环境用，无真实硬件时生成 0x55 0xAA 帧）。
/// 每 500ms 为 8 台设备各生成一帧，写入 Pipe 供 OpcUaPlcClient 用 PipeReader 读取。
/// 使用 ArrayPool&lt;byte&gt;.Shared 租用 512B 缓冲池（AGENTS.md 4.3）。
/// T2.16 替换为真实 OPC UA / Modbus / EtherNet/IP 驱动时，仅需替换传输层，帧协议不变。
/// </summary>
public sealed class SimulatedPlcTransport : IAsyncDisposable
{
    private readonly Pipe _pipe = new Pipe(System.IO.Pipelines.PipeOptions.Default);
    private readonly IReadOnlyList<Equipment> _equipment;
    private readonly TimeSpan _sampleInterval;
    private CancellationTokenSource? _cts;
    private Task? _generateTask;

    // 模拟运行状态：每台设备的累积计数
    private readonly Dictionary<string, (long Cycle, long Good, long Defect, long RunMs)> _state = new();

    public SimulatedPlcTransport(IReadOnlyList<Equipment> equipment, TimeSpan? sampleInterval = null)
    {
        _equipment = equipment;
        _sampleInterval = sampleInterval ?? TimeSpan.FromMilliseconds(500);
        foreach (var eq in equipment)
            _state[eq.EquipmentCode] = (0, 0, 0, 0);
    }

    /// <summary>暴露 PipeReader 供 OpcUaPlcClient 零拷贝读取</summary>
    public PipeReader Reader => _pipe.Reader;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _generateTask = GenerateFramesAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 帧生成循环：每 500ms 为 8 台设备各生成一帧。
    /// 使用 ArrayPool 租用缓冲区，禁止 per-iteration new byte[]（4.3）。
    /// </summary>
    private async Task GenerateFramesAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var eq in _equipment)
            {
                var s = _state[eq.EquipmentCode];
                // 模拟数据：每周期 +1 件，95% 合格，运行时长累积
                s.Cycle += 1;
                var isGood = Random.Shared.NextDouble() > 0.05;
                if (isGood) s.Good += 1; else s.Defect += 1;
                s.RunMs += (long)_sampleInterval.TotalMilliseconds;
                _state[eq.EquipmentCode] = s;

                // 模拟过程值（按设备类型）
                var processValue = eq.EquipmentType switch
                {
                    "拧紧机" => 22.0 + Random.Shared.NextDouble() * 2 - 1,  // M6 扭矩 22±1Nm
                    "液压台" => 150.0 + Random.Shared.NextDouble() * 10 - 5, // 液压压力 150±5 bar
                    "刷写台" => Random.Shared.NextDouble() * 50,              // CAN 延迟 0~50ms
                    _ => Random.Shared.NextDouble() * 100,
                };
                var processTag = eq.EquipmentType switch
                {
                    "拧紧机" => "Torque-M6-FL",
                    "液压台" => "HydraulicPressure",
                    "刷写台" => "CanLatency",
                    _ => "Generic",
                };

                var snapshot = PlcSnapshot.Create(
                    eq.EquipmentCode, now,
                    EquipmentStatus.Running, s.Cycle, s.Good, s.Defect, s.RunMs,
                    processValue, processTag);

                // ArrayPool 租用缓冲区写帧（零分配热路径）
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

            try { await Task.Delay(_sampleInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        await _pipe.Writer.CompleteAsync();
        if (_generateTask is not null)
        {
            try { await _generateTask; } catch { /* 取消异常忽略 */ }
        }
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
