using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 计量器具台账测试（S01 · IATF 16949）。
/// 覆盖建账校验、状态机流转（InService→DueSoon→Overdue→校准复位）、报废终态、
/// 校准登记重算到期日、IsWithinCalibration 判定（S02 引用校验的依据）。
/// </summary>
public class GaugeTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);

    // ═══════════════════════════════════════════════════════════
    //  Create — 建账
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_ShouldInitializeCorrectly()
    {
        var lastCal = DateTimeOffset.UtcNow.AddDays(-30);
        var gauge = Gauge.Create(
            Ulid.NewUlid(), "GT-TQ-001", "扭矩扳手", GaugeType.TorqueWrench,
            "0-100 Nm", "0.01 Nm", "0.5 级", 365, lastCal, "计量室 A-02");

        Assert.Equal("GT-TQ-001", gauge.GaugeNumber);
        Assert.Equal("扭矩扳手", gauge.Name);
        Assert.Equal(GaugeType.TorqueWrench, gauge.Type);
        Assert.Equal(365, gauge.CalibrationCycleDays);
        Assert.Equal(lastCal, gauge.LastCalibratedAt);
        // 到期日 = 校准日 + 周期
        Assert.Equal(lastCal.AddDays(365), gauge.NextDueAt);
        // 剩余 335 天 > 30 天窗口 → 在用
        Assert.Equal(GaugeStatus.InService, gauge.Status);
        Assert.Equal("计量室 A-02", gauge.StorageLocation);
    }

    [Fact]
    public void Create_ShouldThrowWhenGaugeNumberEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            Gauge.Create(Ulid.NewUlid(), "", "名", GaugeType.Caliper, "-", "-", "-", 365, BaseTime));
    }

    [Fact]
    public void Create_ShouldThrowWhenNameEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            Gauge.Create(Ulid.NewUlid(), "GT-01", " ", GaugeType.Caliper, "-", "-", "-", 365, BaseTime));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Create_ShouldThrowWhenCycleDaysNotPositive(int cycleDays)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Gauge.Create(Ulid.NewUlid(), "GT-01", "名", GaugeType.Other, "-", "-", "-", cycleDays, BaseTime));
    }

    [Fact]
    public void Create_ShouldTrimValues()
    {
        var gauge = Gauge.Create(
            Ulid.NewUlid(), " GT-01 ", " 卡尺 ", GaugeType.Caliper, " 150mm ", " 0.02mm ", " 1 级 ",
            180, BaseTime, " A柜 ", " 新购 ");

        Assert.Equal("GT-01", gauge.GaugeNumber);
        Assert.Equal("卡尺", gauge.Name);
        Assert.Equal("150mm", gauge.RangeSpec);
        Assert.Equal("0.02mm", gauge.ResolutionSpec);
        Assert.Equal("1 级", gauge.AccuracyClass);
        Assert.Equal("A柜", gauge.StorageLocation);
        Assert.Equal("新购", gauge.Remarks);
    }

    // ═══════════════════════════════════════════════════════════
    //  EvaluateStatus — 状态推导纯函数
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void EvaluateStatus_ShouldReturnOverdue_WhenPastDue()
    {
        var due = BaseTime.AddDays(-1);
        Assert.Equal(GaugeStatus.Overdue, Gauge.EvaluateStatus(due, GaugeStatus.InService, BaseTime));
    }

    [Fact]
    public void EvaluateStatus_ShouldReturnDueSoon_AtExactWindowBoundary()
    {
        // 恰好剩余 30 天 → 临期（<= 窗口即触发）
        var due = BaseTime.AddDays(Gauge.DueSoonWindowDays);
        Assert.Equal(GaugeStatus.DueSoon, Gauge.EvaluateStatus(due, GaugeStatus.InService, BaseTime));
    }

    [Fact]
    public void EvaluateStatus_ShouldReturnInService_BeyondWindow()
    {
        var due = BaseTime.AddDays(Gauge.DueSoonWindowDays).AddSeconds(1);
        Assert.Equal(GaugeStatus.InService, Gauge.EvaluateStatus(due, GaugeStatus.InService, BaseTime));
    }

    [Fact]
    public void EvaluateStatus_ShouldKeepFallback_WhenNextDueIsNull()
    {
        Assert.Equal(GaugeStatus.InService, Gauge.EvaluateStatus(null, GaugeStatus.InService, BaseTime));
    }

    // ═══════════════════════════════════════════════════════════
    //  RecordCalibration — 校准登记与状态复位
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RecordCalibration_ShouldResetOverdueToInService_AndRecomputeDue()
    {
        // 建账时即已过期（上次校准在两个周期之前）
        var gauge = Gauge.Create(
            Ulid.NewUlid(), "GT-TQ-002", "扭矩仪", GaugeType.TorqueWrench,
            "5-50 Nm", "0.01 Nm", "1 级", 365, BaseTime.AddDays(-400));
        Assert.Equal(GaugeStatus.Overdue, gauge.Status);

        var calibratedAt = BaseTime;
        var ok = gauge.RecordCalibration(calibratedAt);

        Assert.True(ok);
        Assert.Equal(calibratedAt, gauge.LastCalibratedAt);
        Assert.Equal(calibratedAt.AddDays(365), gauge.NextDueAt);
        Assert.Equal(GaugeStatus.InService, gauge.Status);
    }

    [Fact]
    public void RecordCalibration_ShouldUseOverrideNextDue_WhenProvided()
    {
        var gauge = CreateDefaultGauge();
        var overrideDue = BaseTime.AddDays(90); // 证书指定缩短周期

        gauge.RecordCalibration(BaseTime, overrideDue);

        Assert.Equal(overrideDue, gauge.NextDueAt);
        // 剩余 90 天 > 30 天 → 在用
        Assert.Equal(GaugeStatus.InService, gauge.Status);
    }

    [Fact]
    public void RecordCalibration_ShouldReject_WhenScrapped()
    {
        var gauge = CreateDefaultGauge();
        gauge.Scrap("表盘碎裂");
        Assert.Equal(GaugeStatus.Scrapped, gauge.Status);

        var ok = gauge.RecordCalibration(BaseTime);

        Assert.False(ok);
        Assert.Equal(GaugeStatus.Scrapped, gauge.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scrap — 报废终态
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Scrap_ShouldAppendReasonToEmptyRemarks()
    {
        var gauge = CreateDefaultGauge();

        Assert.True(gauge.Scrap("精度超差"));
        Assert.Equal(GaugeStatus.Scrapped, gauge.Status);
        Assert.Contains("报废：精度超差", gauge.Remarks);
    }

    [Fact]
    public void Scrap_ShouldAppendReasonToExistingRemarks()
    {
        var gauge = Gauge.Create(
            Ulid.NewUlid(), "GT-CL-003", "千分尺", GaugeType.Caliper,
            "25-50mm", "0.001mm", "0 级", 180, BaseTime.AddDays(-10), remarks: "原厂编号 X");

        gauge.Scrap("螺纹损坏");

        Assert.Equal($"原厂编号 X；报废：螺纹损坏", gauge.Remarks);
    }

    [Fact]
    public void Scrap_ShouldBeIdempotent_FalseOnSecondCall()
    {
        var gauge = CreateDefaultGauge();

        Assert.True(gauge.Scrap("原因一"));
        Assert.False(gauge.Scrap("原因二"));
    }

    [Fact]
    public void Scrap_ShouldThrow_WhenReasonEmpty()
    {
        var gauge = CreateDefaultGauge();

        Assert.Throws<ArgumentException>(() => gauge.Scrap(" "));
    }

    // ═══════════════════════════════════════════════════════════
    //  RefreshStatus — 后台服务状态推进
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RefreshStatus_ShouldTransitionInServiceToOverdue_AsTimePasses()
    {
        var start = DateTimeOffset.UtcNow;
        var gauge = Gauge.Create(
            Ulid.NewUlid(), "GT-PM-001", "压力表", GaugeType.PressureGauge,
            "0-2.5 MPa", "0.01 MPa", "1.6 级", 182, start);

        Assert.Equal(GaugeStatus.InService, gauge.Status);

        // 时间推进到到期后
        gauge.RefreshStatus(start.AddDays(200));
        Assert.Equal(GaugeStatus.Overdue, gauge.Status);
    }

    [Fact]
    public void RefreshStatus_ShouldNotResurrectScrapped()
    {
        var gauge = CreateDefaultGauge();
        gauge.Scrap("已处置");

        gauge.RefreshStatus(BaseTime.AddYears(-5)); // 即使时间回拨也不改变终态

        Assert.Equal(GaugeStatus.Scrapped, gauge.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  IsWithinCalibration — S02 检验引用校验依据
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsWithinCalibration_ShouldBeTrue_WhenInService()
    {
        var gauge = CreateDefaultGauge(); // 到期日远在未来
        Assert.True(gauge.IsWithinCalibration(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsWithinCalibration_ShouldBeFalse_WhenOverdueOrScrapped()
    {
        var overdue = Gauge.Create(
            Ulid.NewUlid(), "GT-X-1", "过期表", GaugeType.Multimeter,
            "-", "-", "-", 30, DateTimeOffset.UtcNow.AddDays(-60));
        Assert.False(overdue.IsWithinCalibration(DateTimeOffset.UtcNow));

        var scrapped = CreateDefaultGauge();
        scrapped.Scrap("报废");
        Assert.False(scrapped.IsWithinCalibration(DateTimeOffset.UtcNow));
    }

    // ═══════════════════════════════════════════════════════════
    //  CalibrationRecord — 校准记录
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CalibrationRecord_CreateShouldInitializeCorrectly()
    {
        var gaugeId = Ulid.NewUlid();
        var record = CalibrationRecord.Create(
            Ulid.NewUlid(), gaugeId, BaseTime, CalibrationResult.Adjusted,
            "CERT-2026-0815", "QE-007", BaseTime.AddDays(365), "更换调零螺钉");

        Assert.Equal(gaugeId, record.GaugeId);
        Assert.Equal(CalibrationResult.Adjusted, record.Result);
        Assert.Equal("CERT-2026-0815", record.CertificateNo);
        Assert.Equal("QE-007", record.OperatorId);
        Assert.Equal(BaseTime.AddDays(365), record.NextDueAfter);
        Assert.Equal("更换调零螺钉", record.Remarks);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void CalibrationRecord_CreateShouldThrow_WhenCertificateNoMissing(string? certNo)
    {
        // null → ArgumentNullException（ArgumentException 派生），空串 → ArgumentException
        Assert.ThrowsAny<ArgumentException>(() =>
            CalibrationRecord.Create(Ulid.NewUlid(), Ulid.NewUlid(), BaseTime,
                CalibrationResult.Pass, certNo!, "OP-1", BaseTime));
    }

    [Fact]
    public void CalibrationRecord_CreateShouldThrow_WhenOperatorMissing()
    {
        Assert.Throws<ArgumentException>(() =>
            CalibrationRecord.Create(Ulid.NewUlid(), Ulid.NewUlid(), BaseTime,
                CalibrationResult.Pass, "C-01", "", BaseTime));
    }

    // ═══════════════════════════════════════════════════════════
    //  完整生命周期场景
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FullLifecycle_Create_DueSoon_Overdue_Recalibrate_Scrap()
    {
        // 建账：周期 40 天，40 天后进入临期窗口（剩 ≤30 天）
        var start = DateTimeOffset.UtcNow;
        var gauge = Gauge.Create(
            Ulid.NewUlid(), "GT-FULL-01", "全周期样例", GaugeType.Other,
            "-", "-", "-", 40, start);
        Assert.Equal(GaugeStatus.InService, gauge.Status);

        // 第 15 天：剩 25 天 → 临期
        gauge.RefreshStatus(start.AddDays(15));
        Assert.Equal(GaugeStatus.DueSoon, gauge.Status);

        // 第 41 天：已过期
        gauge.RefreshStatus(start.AddDays(41));
        Assert.Equal(GaugeStatus.Overdue, gauge.Status);

        // 送检复位
        gauge.RecordCalibration(start.AddDays(42));
        Assert.Equal(GaugeStatus.InService, gauge.Status);

        // 最终报废（终态不可逆）
        Assert.True(gauge.Scrap("寿命到期"));
        Assert.False(gauge.RecordCalibration(start.AddDays(100)));
    }

    // ── 辅助 ──

    /// <summary>默认量具：365 天周期、一年前校准，当前在用且远离临期窗口。</summary>
    private static Gauge CreateDefaultGauge()
        => Gauge.Create(
            Ulid.NewUlid(), "GT-DFT-001", "默认量具", GaugeType.TorqueWrench,
            "0-100 Nm", "0.01 Nm", "0.5 级", 365,
            DateTimeOffset.UtcNow.AddDays(-30));
}
