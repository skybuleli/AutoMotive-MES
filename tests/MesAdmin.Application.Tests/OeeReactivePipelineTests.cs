using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesAdmin.Application.Tests;

/// <summary>
/// OeeReactivePipeline 计算测试。
/// 验证按设备窗口增量计算、窗口重置、设备状态隔离与时间门控。
/// </summary>
public class OeeReactivePipelineTests
{
    private readonly OeeReactivePipeline _pipeline;

    public OeeReactivePipelineTests()
    {
        _pipeline = new OeeReactivePipeline(
            null!,
            null!,
            NullLogger<OeeReactivePipeline>.Instance);
    }

    [Fact]
    public void ComputeOeeFromSnapshot_FirstSnapshot_ShouldUseWindowStatusForAvailability()
    {
        var snapshot = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 100, goodCount: 90, defectCount: 10, runTimeMs: 1000);

        var oee = _pipeline.ComputeOeeFromSnapshot(snapshot);

        Assert.Equal("EQ-01", oee.EquipmentCode);
        Assert.Equal(1.0, oee.Availability);
        Assert.Equal(0.0, oee.Performance); // 无增量
        Assert.Equal(1.0, oee.Quality); // 无增量时默认 1.0
    }

    [Fact]
    public void ComputeOeeFromSnapshot_SecondSnapshot_ShouldComputeDeltas()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var first = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 100, goodCount: 90, defectCount: 10, runTimeMs: 1000, baseTime);
        _pipeline.ComputeOeeFromSnapshot(first);

        var second = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 110, goodCount: 99, defectCount: 11, runTimeMs: 11000, baseTime.AddSeconds(5));
        var oee = _pipeline.ComputeOeeFromSnapshot(second);

        // availability = 2 Running / 2 total = 1.0
        Assert.Equal(1.0, oee.Availability);
        // deltaCycle = 10, deltaRunTime = 10000ms => actual cycle = 1000ms, ideal = 10000ms => performance = 1.0
        Assert.Equal(1.0, oee.Performance, precision: 4);
        // deltaGood = 9, deltaDefect = 1 => quality = 0.9
        Assert.Equal(0.9, oee.Quality, precision: 4);
    }

    [Fact]
    public void ComputeOeeFromSnapshot_DifferentEquipment_ShouldIsolateState()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var eq1First = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 100, goodCount: 90, defectCount: 10, runTimeMs: 1000, baseTime);
        var eq2First = CreateSnapshot("EQ-02", EquipmentStatus.Idle, cycleCount: 50, goodCount: 45, defectCount: 5, runTimeMs: 500, baseTime);

        _pipeline.ComputeOeeFromSnapshot(eq1First);
        var eq2Oee = _pipeline.ComputeOeeFromSnapshot(eq2First);

        Assert.Equal(0.0, eq2Oee.Availability);
        Assert.Equal("EQ-02", eq2Oee.EquipmentCode);
    }

    [Fact]
    public void ComputeOeeFromSnapshot_WindowReset_ShouldResetWindowCounters()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var first = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 0, goodCount: 0, defectCount: 0, runTimeMs: 0, baseTime);
        _pipeline.ComputeOeeFromSnapshot(first);

        // 填充窗口至 60 次；首次快照不进入窗口计数，因此 60 次循环后窗口计数为 60，
        // 下一次（resetSnapshot）触发窗口重置。
        for (int i = 1; i <= 60; i++)
        {
            var snapshot = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: i, goodCount: i, defectCount: 0, runTimeMs: i * 1000, baseTime.AddSeconds(i));
            _pipeline.ComputeOeeFromSnapshot(snapshot);
        }

        // 第 62 次应触发窗口重置，新窗口只有 1 个快照
        var resetSnapshot = CreateSnapshot("EQ-01", EquipmentStatus.Idle, cycleCount: 61, goodCount: 61, defectCount: 0, runTimeMs: 61000, baseTime.AddSeconds(61));
        var oee = _pipeline.ComputeOeeFromSnapshot(resetSnapshot);

        // 重置后 RunningSnapshots = 0, TotalSnapshots = 1, status = Idle => availability = 0
        Assert.Equal(0.0, oee.Availability);
    }

    [Fact]
    public void ShouldSample_SameEquipmentWithin5Seconds_ShouldSkip()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var first = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 1, goodCount: 1, defectCount: 0, runTimeMs: 1000, baseTime);
        var second = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 2, goodCount: 2, defectCount: 0, runTimeMs: 2000, baseTime.AddSeconds(3));

        Assert.True(_pipeline.ShouldSample(first));
        Assert.False(_pipeline.ShouldSample(second));
    }

    [Fact]
    public void ShouldSample_DifferentEquipment_ShouldBeIndependent()
    {
        var baseTime = DateTimeOffset.UtcNow;
        var eq1 = CreateSnapshot("EQ-01", EquipmentStatus.Running, cycleCount: 1, goodCount: 1, defectCount: 0, runTimeMs: 1000, baseTime);
        var eq2 = CreateSnapshot("EQ-02", EquipmentStatus.Running, cycleCount: 1, goodCount: 1, defectCount: 0, runTimeMs: 1000, baseTime);

        Assert.True(_pipeline.ShouldSample(eq1));
        Assert.True(_pipeline.ShouldSample(eq2));
    }

    private static PlcSnapshot CreateSnapshot(
        string equipmentCode,
        EquipmentStatus status,
        long cycleCount,
        long goodCount,
        long defectCount,
        long runTimeMs,
        DateTimeOffset? timestamp = null)
    {
        return PlcSnapshot.Create(
            equipmentCode,
            timestamp ?? DateTimeOffset.UtcNow,
            status,
            cycleCount,
            goodCount,
            defectCount,
            runTimeMs,
            processValue: 0,
            processTag: "TEST");
    }
}
