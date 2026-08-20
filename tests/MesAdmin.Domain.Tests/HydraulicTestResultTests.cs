using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 液压功能测试结果判定单元测试（P0-1 修复）。
/// 回归：Complete() 曾因 SolenoidTests.Count > 0 守卫导致无电磁阀数据时必判不合格。
/// </summary>
public class HydraulicTestResultTests
{
    // ═══════════════════════════════════════════════════════════
    //  Complete() 最终判定
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Complete_WithoutSolenoidData_ShouldPassWhenCyclesPass()
    {
        // 回归：数据链路（PLC→管道）只携带压力/泄漏标签，从不写入 SolenoidTests。
        // 电磁阀数据缺失时，判定应只看实际测量的建压/保压/泄压/泄漏率。
        var result = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-HYD-01", 1);
        result.RecordPressureBuild(200);   // ✓ ≤250ms
        result.RecordHoldPressure(180);    // ✓ 175-185
        result.RecordPressureRelease(250); // ✓ ≤300ms
        result.RecordLeakRate(0.3);        // ✓ ≤0.5
        result.Complete();

        Assert.True(result.OverallPass);
        Assert.Equal(HydraulicTestStatus.Passed, result.Status);
        Assert.False(result.EquipmentLocked);
    }

    [Fact]
    public void Complete_WithFailingCycle_ShouldFailEvenWithoutSolenoidData()
    {
        // 建压超时 → 即使无电磁阀数据也必须判不合格
        var result = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-HYD-02", 1);
        result.RecordPressureBuild(400);   // ✗ >250ms
        result.RecordHoldPressure(180);
        result.RecordPressureRelease(250);
        result.RecordLeakRate(0.3);
        result.Complete();

        Assert.False(result.OverallPass);
        Assert.Equal(HydraulicTestStatus.Failed, result.Status);
        Assert.True(result.EquipmentLocked);
    }

    [Fact]
    public void Complete_WithSolenoidData_ShouldRequireAllSolenoidsPass()
    {
        // 有电磁阀数据时必须全部合格才判通过（既有行为保持）
        var result = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-HYD-03", 1);
        result.RecordPressureBuild(200);
        result.RecordHoldPressure(180);
        result.RecordPressureRelease(250);
        result.RecordLeakRate(0.3);
        result.AddSolenoidTest(new SolenoidValveTest(1, true, 12.5, null, null));
        result.AddSolenoidTest(new SolenoidValveTest(2, false, 89.2, 4.1, "F002"));
        result.Complete();

        Assert.False(result.OverallPass);
        Assert.Equal(HydraulicTestStatus.Failed, result.Status);
        Assert.Contains("电磁阀#2", result.FailureReason);
    }

    [Fact]
    public void Complete_AllPassing_ShouldNotLockEquipment()
    {
        var result = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-HYD-04", 1);
        result.RecordPressureBuild(200);
        result.RecordHoldPressure(180);
        result.RecordPressureRelease(250);
        result.RecordLeakRate(0.3);
        result.Complete();

        result.Complete();

        Assert.True(result.OverallPass);
        Assert.False(result.EquipmentLocked);
    }
}