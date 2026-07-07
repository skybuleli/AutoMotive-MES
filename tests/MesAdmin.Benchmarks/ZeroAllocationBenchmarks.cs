using System.Buffers;
using BenchmarkDotNet.Attributes;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;

namespace MesAdmin.Benchmarks;

/// <summary>
/// T4.6 热路径零分配基准测试。
/// 验证 AGENTS.md 4.3 零分配铁律的落实情况：
/// - PlcFrameReader ref struct (ReadOnlySpan) 帧解析
/// - SpcCalculator stackalloc 均值-极差计算
/// - OeeReactivePipeline.ComputeOee stackalloc
/// - Gs1Barcode.Parse ReadOnlySpan<char> 零分配解析
/// - ArrayPool<byte>.Shared 租用/归还性能
/// </summary>
[MemoryDiagnoser]        // 报告 GC 分配量
[MinColumn, MaxColumn]   // 最小值/最大值
[RankColumn]             // 性能排名
public class ZeroAllocationBenchmarks
{
    private byte[] _validFrame = null!;
    private double[] _measurements = null!;
    private PlcSnapshot _snapshotForWriter = null!;
    private byte[] _serializedSnapshot = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ── 构建有效 PLC 帧 ──
        var snapshot = PlcSnapshot.Create(
            "EQ-TQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running,
            cycleCount: 12345, goodCount: 12000, defectCount: 345,
            runTimeMs: 86400000, processValue: 22.5, processTag: "Torque-M6-FL");
        var buffer = new byte[PlcFrameProtocol.FrameLength];
        PlcFrameWriter.Write(buffer, snapshot);
        _validFrame = buffer;

        // ── SPC 测量数据（n=5 子组）──
        _measurements = [22.1, 22.3, 21.8, 22.0, 22.2];

        // ── 帧编码基准用快照 ──
        _snapshotForWriter = PlcSnapshot.Create(
            "EQ-TQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running,
            cycleCount: 12345, goodCount: 12000, defectCount: 345,
            runTimeMs: 86400000, processValue: 22.5, processTag: "Torque-M6-FL");

        // ── MemoryPack 反序列化基准用预序列化数据 ──
        _serializedSnapshot = MemoryPack.MemoryPackSerializer.Serialize(_snapshotForWriter);
    }

    // ═══════════════════════════════════════════════════════════
    //  PlcFrameReader 零分配帧解析
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "PlcFrameReader: ref struct 帧解析 (TryParse)")]
    public PlcSnapshot FrameReader_Parse()
    {
        var reader = new PlcFrameReader(_validFrame);
        reader.TryParse(out var snapshot);
        return snapshot;
    }

    [Benchmark(Description = "PlcFrameReader: EquipmentCode 读取 (ASCII Span)")]
    public string FrameReader_EquipmentCode()
    {
        var reader = new PlcFrameReader(_validFrame);
        return reader.EquipmentCode;
    }

    [Benchmark(Description = "PlcFrameReader: ProcessValue 读取 (MemoryMarshal)")]
    public double FrameReader_ProcessValue()
    {
        var reader = new PlcFrameReader(_validFrame);
        return reader.ProcessValue;
    }

    [Benchmark(Description = "PlcFrameReader: 帧完整性校验 (Validate)")]
    public bool FrameReader_Validate()
    {
        var reader = new PlcFrameReader(_validFrame);
        return reader.Validate();
    }

    // ═══════════════════════════════════════════════════════════
    //  PlcFrameWriter 零分配帧编码
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "PlcFrameWriter: Span 帧编码 (Write)")]
    public int FrameWriter_Write()
    {
        Span<byte> dest = stackalloc byte[PlcFrameProtocol.FrameLength];
        return PlcFrameWriter.Write(dest, _snapshotForWriter);
    }

    // ═══════════════════════════════════════════════════════════
    //  SpcCalculator — stackalloc 计算
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "SpcCalculator: Cpk 计算 (stackalloc)")]
    public (double Cp, double Cpk) Spc_CalculateCpk()
    {
        var result = SpcCalculator.CalculateCpk(_measurements, usl: 23.0, lsl: 21.0);
        return (result.Cp, result.Cpk);
    }

    [Benchmark(Description = "SpcCalculator: 均值计算 (ReadOnlySpan)")]
    public double Spc_Mean()
        => SpcCalculator.CalculateMean(_measurements);

    [Benchmark(Description = "SpcCalculator: 样本标准差 (ReadOnlySpan)")]
    public double Spc_StdDev()
        => SpcCalculator.CalculateSampleStdDev(_measurements);

    // ═══════════════════════════════════════════════════════════
    //  SpcSample.Create — stackalloc + Sort
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "SpcSample.Create: stackalloc 均值-极差")]
    public SpcSample SpcSample_Create()
    {
        return SpcSample.Create(
            "TOR-M6", subgroupIndex: 1, _measurements,
            orderId: Ulid.NewUlid(), orderNumber: "WO-20260705-0001",
            equipmentCode: "EQ-TQ-01");
    }

    // ═══════════════════════════════════════════════════════════
    //  OEE Compute — stackalloc metrics[3]
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "OeeRecord.Compute: stackalloc OEE 计算")]
    public OeeRecord Oee_Compute()
    {
        var snapshot = PlcSnapshot.Create(
            "EQ-TQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running,
            cycleCount: 50, goodCount: 48, defectCount: 2,
            runTimeMs: 500000, processValue: 22.5, processTag: "Torque");
        return OeeRecord.Compute(
            snapshot.EquipmentCode, snapshot.Timestamp,
            availability: 0.95, performance: 0.88, quality: 0.96);
    }

    // ═══════════════════════════════════════════════════════════
    //  MemoryPack 序列化零分配验证
    // ═══════════════════════════════════════════════════════════

    private readonly PlcSnapshot _snapshotForSer = PlcSnapshot.Create(
        "EQ-TQ-01", DateTimeOffset.UtcNow, EquipmentStatus.Running,
        12345, 12000, 345, 86400000, 22.5, "Torque-M6-FL");

    [Benchmark(Description = "MemoryPack: PlcSnapshot 序列化")]
    public byte[] MemoryPack_Serialize()
        => MemoryPack.MemoryPackSerializer.Serialize(_snapshotForSer);

    [Benchmark(Description = "MemoryPack: PlcSnapshot 反序列化")]
    public PlcSnapshot? MemoryPack_Deserialize()
        => MemoryPack.MemoryPackSerializer.Deserialize<PlcSnapshot>(_serializedSnapshot);

    // ═══════════════════════════════════════════════════════════
    //  ArrayPool 租用/归还性能
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Description = "ArrayPool<byte>: Rent 512B + Return")]
    public void ArrayPool_RentReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(512);
        try
        {
            buffer[0] = 0x55;
            buffer[1] = 0xAA;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [Benchmark(Description = "ArrayPool<byte>: Rent FrameLength + Return")]
    public void ArrayPool_RentFrame()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(PlcFrameProtocol.FrameLength);
        try
        {
            PlcFrameWriter.Write(buffer, _snapshotForSer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
