using BenchmarkDotNet.Attributes;
using System.Threading.Channels;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;

namespace MesAdmin.Benchmarks;

/// <summary>
/// T4.7 PLC 吞吐压测基准测试。
/// - Channel 生产者/消费者吞吐量（8 设备 × 100Hz）
/// - ChannelHealth 并发跟踪
/// - PlcSnapshot 创建性能
///
/// ⚠ 每个 benchmark 方法内部重建 Channel，避免 TryComplete() 跨迭代冲突。
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn]
public class PlcThroughputBenchmarks
{
    private PlcSnapshot[] _snapshots = null!;

    [Params(1000, 10000)]   // Channel 容量参数
    public int ChannelCapacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // 预生成 8 设备快照
        _snapshots = new PlcSnapshot[8];
        for (int i = 0; i < 8; i++)
        {
            _snapshots[i] = PlcSnapshot.Create(
                $"EQ-{i:D3}", DateTimeOffset.UtcNow, EquipmentStatus.Running,
                cycleCount: 1000 + i, goodCount: 950 + i, defectCount: 50,
                runTimeMs: 86400000, processValue: 22.5 + i, processTag: "Bench");
        }
    }

    /// <summary>在每个 benchmark 方法内创建新 Channel</summary>
    private Channel<PlcSnapshot> CreateChannel()
        => Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

    // ═══════════════════════════════════════════════════════════
    //  Channel 写入吞吐量
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "Channel: 8 设备 × 1000 帧 写入")]
    public async Task Channel_WriteThroughput()
    {
        var channel = CreateChannel();
        for (int round = 0; round < 125; round++)
        {
            for (int i = 0; i < 8; i++)
                await channel.Writer.WriteAsync(_snapshots[i]);
        }
        channel.Writer.TryComplete();
    }

    [Benchmark(Description = "Channel: 8 设备 × 1000 帧 读取")]
    public async Task<int> Channel_ReadThroughput()
    {
        var channel = CreateChannel();
        // 预写入
        for (int round = 0; round < 125; round++)
            for (int i = 0; i < 8; i++)
                await channel.Writer.WriteAsync(_snapshots[i]);
        channel.Writer.TryComplete();

        var count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
            count++;
        return count;
    }

    [Benchmark(Description = "Channel: 8 设备 × 1000 帧 写入+读取（流水线）")]
    public async Task<int> Channel_PipelineThroughput()
    {
        var channel = CreateChannel();
        var producer = Task.Run(async () =>
        {
            for (int round = 0; round < 125; round++)
                for (int i = 0; i < 8; i++)
                    await channel.Writer.WriteAsync(_snapshots[i]);
            channel.Writer.TryComplete();
        });

        var consumer = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in channel.Reader.ReadAllAsync())
                count++;
            return count;
        });

        await producer;
        return await consumer;
    }

    // ═══════════════════════════════════════════════════════════
    //  PlcSnapshot 创建性能（热路径）
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "PlcSnapshot.Create: 单设备快照")]
    public PlcSnapshot Snapshot_Create()
        => PlcSnapshot.Create(
            "EQ-TQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running,
            cycleCount: 1000, goodCount: 950, defectCount: 50,
            runTimeMs: 86400000, processValue: 22.5, processTag: "Torque-M6-FL");

    [Benchmark(Description = "PlcSnapshot.Create: 8 设备快照")]
    public PlcSnapshot[] Snapshot_Create8()
    {
        var result = new PlcSnapshot[8];
        for (int i = 0; i < 8; i++)
        {
            result[i] = PlcSnapshot.Create(
                $"EQ-{i:D3}", DateTimeOffset.UtcNow, EquipmentStatus.Running,
                cycleCount: 1000 + i, goodCount: 950 + i, defectCount: 50,
                runTimeMs: 86400000, processValue: 22.5 + i, processTag: "Bench");
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    //  ChannelHealth 并发跟踪性能
    // ═══════════════════════════════════════════════════════════

    private readonly ChannelHealth _health = new();

    [Benchmark(Description = "ChannelHealth: 8 线程 × 1000 次 IncrementWritten")]
    public void ChannelHealth_ConcurrentIncrement()
    {
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                _health.IncrementWritten();
                _health.IncrementRead();
            }
        });
    }

    [Benchmark(Description = "ChannelHealth: GetUtilization 读取")]
    public double ChannelHealth_GetUtilization()
        => _health.GetUtilization(10000);
}
