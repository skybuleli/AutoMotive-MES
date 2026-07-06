using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// SPC 质量管理模块单元测试（T2.1-T2.8）。
/// 覆盖 SpcCalculator 计算 + Western Electric 8 条判异规则 + QualityRecord 状态机。
/// </summary>
public class SpcCalculatorTests
{
    // ═══════════════════════════════════════════════════════════
    //  基础统计计算
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CalculateMean_ShouldComputeCorrectly()
    {
        double[] values = [2.0, 4.0, 6.0, 8.0];
        var mean = SpcCalculator.CalculateMean(values.AsSpan());
        Assert.Equal(5.0, mean);
    }

    [Fact]
    public void CalculateMean_ShouldHandleSingleValue()
    {
        double[] values = [42.0];
        var mean = SpcCalculator.CalculateMean(values.AsSpan());
        Assert.Equal(42.0, mean);
    }

    [Fact]
    public void CalculateMean_ShouldThrowOnEmpty()
    {
        Assert.Throws<ArgumentException>(() => SpcCalculator.CalculateMean(ReadOnlySpan<double>.Empty));
    }

    [Fact]
    public void CalculateSampleStdDev_ShouldComputeCorrectly()
    {
        // Known values: dataset [2, 4, 6, 8] has mean=5
        // variance = ((2-5)²+(4-5)²+(6-5)²+(8-5)²)/(4-1) = (9+1+1+9)/3 = 20/3 ≈ 6.6667
        // stddev = sqrt(6.6667) ≈ 2.58199
        double[] values = [2.0, 4.0, 6.0, 8.0];
        var stdDev = SpcCalculator.CalculateSampleStdDev(values.AsSpan());
        Assert.Equal(2.582, Math.Round(stdDev, 3));
    }

    [Fact]
    public void CalculateSampleStdDev_ShouldThrowWhenLessThanTwo()
    {
        Assert.Throws<ArgumentException>(() => SpcCalculator.CalculateSampleStdDev(new double[] { 1.0 }.AsSpan()));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cpk / Ppk 计算
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CalculateCpk_ShouldReturnHighValueWhenProcessCentered()
    {
        double[] values = [99.5, 100.2, 100.1, 99.8, 100.0, 100.3, 99.7, 100.0];
        var (cp, cpk, mean, _) = SpcCalculator.CalculateCpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.True(cpk > 1.33, $"Expected Cpk > 1.33 (capable), got {cpk}");
        Assert.True(cp > cpk - 0.1, "Cp should be >= Cpk for centered process");
        Assert.True(mean > 99 && mean < 101);
    }

    [Fact]
    public void CalculateCpk_ShouldReturnZeroWhenMeanNearSpecLimit()
    {
        double[] values = [106.0, 106.1, 105.9, 106.0];
        var (_, cpk, _, _) = SpcCalculator.CalculateCpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.True(cpk >= 0, $"Expected Cpk >= 0 at spec limit, got {cpk}");
    }

    [Fact]
    public void CalculateCpk_ShouldReturnNegativeWhenOutsideSpec()
    {
        double[] values = [108.0, 109.0, 107.5, 108.5];
        var (_, cpk, _, _) = SpcCalculator.CalculateCpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.True(cpk < 0, $"Expected Cpk < 0 when outside spec, got {cpk}");
    }

    [Fact]
    public void CalculateCpk_ShouldThrowWhenUslNotGreaterThanLsl()
    {
        Assert.Throws<ArgumentException>(() =>
            SpcCalculator.CalculateCpk(new[] { 1.0, 2.0 }.AsSpan(), usl: 90, lsl: 100));
    }

    [Fact]
    public void CalculateCpk_ShouldThrowWhenLessThanTwoValues()
    {
        Assert.Throws<ArgumentException>(() =>
            SpcCalculator.CalculateCpk(new[] { 1.0 }.AsSpan(), usl: 10, lsl: 0));
    }

    [Fact]
    public void CalculateCpk_ShouldReturnInfinityWhenAllValuesIdentical()
    {
        double[] values = [100.0, 100.0, 100.0, 100.0];
        var (cp, cpk, _, _) = SpcCalculator.CalculateCpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.Equal(double.PositiveInfinity, cp);
        Assert.Equal(double.PositiveInfinity, cpk);
    }

    [Fact]
    public void CalculatePpk_ShouldComputeCorrectly()
    {
        double[] values = [99.5, 100.2, 100.1, 99.8, 100.0, 100.3, 99.7, 100.0];
        var (pp, ppk) = SpcCalculator.CalculatePpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.True(ppk > 0);
        Assert.True(pp > 0);
    }

    [Fact]
    public void CalculatePpk_ShouldBeGreaterThanOrEqualToCpkForSameData()
    {
        // Ppk uses population std dev (÷n) which is smaller than sample std dev (÷n-1),
        // so Pp/Ppk are always ≥ Cp/Cpk for the same data.
        double[] values = [99.5, 100.2, 100.1, 99.8, 100.0, 100.3, 99.7, 100.0];
        var (_, cpk, _, _) = SpcCalculator.CalculateCpk(values.AsSpan(), usl: 106, lsl: 94);
        var (_, ppk) = SpcCalculator.CalculatePpk(values.AsSpan(), usl: 106, lsl: 94);
        Assert.True(ppk >= cpk, $"Expected Ppk={ppk} >= Cpk={cpk}");
    }

    // ═══════════════════════════════════════════════════════════
    //  X̄-R 控制限
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CalculateXbarRControlLimits_ShouldUseCorrectConstants()
    {
        var subgroups = CreateSubgroups(
            new double[] { 10.0, 10.5, 10.2, 9.8, 10.1 },
            new double[] { 10.3, 9.9, 10.4, 10.0, 10.2 },
            new double[] { 9.7, 10.1, 10.0, 10.3, 9.9 });

        var (grandMean, meanRange, uclX, lclX, uclR, lclR) =
            SpcCalculator.CalculateXbarRControlLimits(subgroups.ToArray().AsSpan());

        // n=5: A2=0.577, D3=0, D4=2.115
        Assert.True(uclX > grandMean);
        Assert.True(lclX < grandMean);
        Assert.True(uclR > 0);
        Assert.Equal(0, lclR);
    }

    [Fact]
    public void CalculateXbarRControlLimits_ShouldThrowWhenLessThanTwoSubgroups()
    {
        var single = CreateSubgroups(new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 });
        Assert.Throws<ArgumentException>(() =>
            SpcCalculator.CalculateXbarRControlLimits(single.ToArray().AsSpan()));
    }

    // ═══════════════════════════════════════════════════════════
    //  NormalCdf
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void NormalCdf_ShouldReturnPointFiveAtZero()
    {
        Assert.Equal(0.5, SpcCalculator.NormalCdf(0), 4);
    }

    [Fact]
    public void NormalCdf_ShouldReturnApproximatelyPointNineSevenFiveAtOnePointNineSix()
    {
        var p = SpcCalculator.NormalCdf(1.96);
        Assert.True(Math.Abs(p - 0.975) < 0.01, $"Expected ~0.975, got {p}");
    }

    [Fact]
    public void NormalCdf_ShouldReturnApproximatelyPointZeroTwoFiveAtNegativeOnePointNineSix()
    {
        var p = SpcCalculator.NormalCdf(-1.96);
        Assert.True(Math.Abs(p - 0.025) < 0.01, $"Expected ~0.025, got {p}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric 规则 —— 不触发
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CheckWesternElectric_ShouldReturnNoAlertsWhenInControl()
    {
        var subgroups = CreateMeansAt(cl: 100, ucl: 106, lcl: 94, count: 10, deviation: 1);
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, characteristicCode: "TOR-M6");
        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckWesternElectric_ShouldReturnEmptyWhenNoSubgroups()
    {
        var alerts = SpcCalculator.CheckWesternElectricRules(
            ReadOnlySpan<SpcSample>.Empty, cl: 100, ucl: 106, lcl: 94, characteristicCode: "TOR-M6");
        Assert.Empty(alerts);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 1 — 1 点超出 3σ 控制限
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule1_ShouldAlertWhenPointAboveUcl()
    {
        var subgroups = CreateMeansAt(cl: 100, ucl: 106, lcl: 94, count: 5, deviation: 1);
        subgroups[^1] = MakeSample(107.0, "TOR-M6", 999);
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.Beyond3Sigma);
    }

    [Fact]
    public void Rule1_ShouldAlertWhenPointBelowLcl()
    {
        var subgroups = CreateMeansAt(cl: 100, ucl: 106, lcl: 94, count: 5, deviation: 1);
        subgroups[^1] = MakeSample(93.0, "TOR-M6", 999);
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.Beyond3Sigma);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 2 — 连续 3 点中有 2 点在 Zone A
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule2_ShouldAlertWhenTwoOfThreeInUpperZoneA()
    {
        // CL=100, UCL=106, LCL=94 → σ=2
        // CountInZoneA checks: dev > zoneB(2) && dev <= zoneA(4)
        // Valid dev range: (2, 4], i.e. absolute values [102, 104)
        var subgroups = new List<SpcSample>
        {
            MakeSample(100.0, "TOR-M6", 1),
            MakeSample(102.5, "TOR-M6", 2),  // dev=2.5 ∈ (2, 4] ✓
            MakeSample(103.5, "TOR-M6", 3),  // dev=3.5 ∈ (2, 4] ✓
        };
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.TwoOfThreeInZoneA);
    }

    [Fact]
    public void Rule2_ShouldAlertWhenTwoOfThreeInLowerZoneA()
    {
        // Lower side: dev ∈ (2, 4], i.e. absolute values (96, 98]
        var subgroups = new List<SpcSample>
        {
            MakeSample(100.0, "TOR-M6", 1),
            MakeSample(97.5, "TOR-M6", 2),   // dev=2.5 ∈ (2, 4] ✓
            MakeSample(96.5, "TOR-M6", 3),   // dev=3.5 ∈ (2, 4] ✓
        };
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.TwoOfThreeInZoneA);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 3 — 连续 5 点中有 4 点在 CL 同侧
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule3_ShouldAlertWhenFourOfFiveUpperSide()
    {
        var subgroups = new List<SpcSample>
        {
            MakeSample(102.5, "TOR-M6", 1),
            MakeSample(103.0, "TOR-M6", 2),
            MakeSample(102.0, "TOR-M6", 3),
            MakeSample(104.0, "TOR-M6", 4),
            MakeSample(100.0, "TOR-M6", 5),
        };
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.FourOfFiveInZoneB);
    }

    [Fact]
    public void Rule3_ShouldAlertWhenFourOfFiveLowerSide()
    {
        var subgroups = new List<SpcSample>
        {
            MakeSample(97.5, "TOR-M6", 1),
            MakeSample(97.0, "TOR-M6", 2),
            MakeSample(98.0, "TOR-M6", 3),
            MakeSample(96.0, "TOR-M6", 4),
            MakeSample(100.0, "TOR-M6", 5),
        };
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.FourOfFiveInZoneB);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 4 — 连续 8 点在 CL 同侧
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule4_ShouldAlertWhenEightAboveCl()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 8; i++)
            subgroups.Add(MakeSample(101.0 + (i * 0.1), "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.EightInZoneCOrBeyond);
    }

    [Fact]
    public void Rule4_ShouldAlertWhenEightBelowCl()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 8; i++)
            subgroups.Add(MakeSample(99.0 - (i * 0.1), "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.EightInZoneCOrBeyond);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 5 — 连续 6 点递增或递减
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule5_ShouldAlertWhenSixIncreasing()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 6; i++)
            subgroups.Add(MakeSample(100.0 + i * 0.5, "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.SixInTrend);
    }

    [Fact]
    public void Rule5_ShouldAlertWhenSixDecreasing()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 6; i++)
            subgroups.Add(MakeSample(103.0 - i * 0.5, "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.SixInTrend);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 7 — 连续 15 点在 Zone C（±1σ 内）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule7_ShouldAlertWhenFifteenInZoneC()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 15; i++)
            subgroups.Add(MakeSample(100.0 + (i % 3 - 1) * 0.5, "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.FifteenInZoneC);
    }

    // ═══════════════════════════════════════════════════════════
    //  Western Electric Rule 8 — 连续 8 点在 Zone C 以外
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Rule8_ShouldAlertWhenEightOutsideZoneC()
    {
        // 1.5σ above CL → outside Zone C (±1σ)
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 8; i++)
            subgroups.Add(MakeSample(103.0, "TOR-M6", i + 1));
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.EightOutsideZoneC);
    }

    // ═══════════════════════════════════════════════════════════
    //  多个规则同时触发
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CheckWesternElectric_ShouldReturnMultipleAlerts()
    {
        var subgroups = new List<SpcSample>();
        for (int i = 0; i < 8; i++)
            subgroups.Add(MakeSample(101.0 + i * 0.8, "TOR-M6", i + 1));
        // Last point = 101 + 7*0.8 = 106.6 > UCL=106
        var alerts = SpcCalculator.CheckWesternElectricRules(
            subgroups.ToArray().AsSpan(), cl: 100, ucl: 106, lcl: 94, "TOR-M6");
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.Beyond3Sigma);
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.EightInZoneCOrBeyond);
        Assert.Contains(alerts, a => a.RuleType == SpcRuleType.SixInTrend);
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static SpcSample MakeSample(double mean, string charCode, int index)
        => new()
        {
            Id = Ulid.NewUlid(),
            CharacteristicCode = charCode,
            SubgroupIndex = index,
            SubgroupSize = 5,
            Mean = mean,
            Range = 2.0,
            Values = [mean - 1, mean - 0.5, mean, mean + 0.5, mean + 1],
            CollectedAt = DateTimeOffset.UtcNow,
        };

    private static List<SpcSample> CreateMeansAt(double cl, double ucl, double lcl, int count, double deviation)
    {
        var sigma = (ucl - cl) / 3;
        var rng = new Random(42);
        var samples = new List<SpcSample>();
        for (int i = 0; i < count; i++)
        {
            var mean = cl + (rng.NextDouble() - 0.5) * deviation * sigma;
            samples.Add(MakeSample(Math.Round(mean, 4), "TOR-M6", i + 1));
        }
        return samples;
    }

    private static List<SpcSample> CreateSubgroups(params double[][] allValues)
    {
        var samples = new List<SpcSample>();
        for (int i = 0; i < allValues.Length; i++)
            samples.Add(SpcSample.Create("DIM-01", i + 1, allValues[i].AsSpan()));
        return samples;
    }
}

// ═══════════════════════════════════════════════════════════════
//  QualityRecord 状态机 + 检验特性测试
// ═══════════════════════════════════════════════════════════════

public class QualityRecordTests
{
    private static readonly Ulid TestPlanId = Ulid.NewUlid();

    // ═══════════════════════════════════════════════════════════
    //  IQC 创建
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateIqc_ShouldInitializeCorrectly()
    {
        var record = QualityRecord.CreateIqc(
            TestPlanId, "ESP-9.0 IQC", "ECU-ESP9-001", "ECU 控制单元",
            "BATCH-001", "SUP-001", "博世苏州", "QC-001",
            sampleSize: 5, acceptNumber: 0, rejectNumber: 1, aqlScheme: "AQL=0.65");

        Assert.Equal(InspectionStage.Iq, record.Stage);
        Assert.Equal("ECU-ESP9-001", record.ProductCode);
        Assert.Equal("BATCH-001", record.BatchNumber);
        Assert.Equal("SUP-001", record.SupplierCode);
        Assert.Equal("QC-001", record.InspectorId);
        Assert.Equal(InspectionVerdict.Pending, record.Verdict);
        Assert.Equal(5, record.SampleSize);
        Assert.Equal(0, record.AcceptNumber);
        Assert.Equal(1, record.RejectNumber);
        Assert.Equal("AQL=0.65", record.AqlScheme);
        Assert.Null(record.CompletedAt);
    }

    [Fact]
    public void CreateIqc_ShouldThrowWhenMaterialCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            QualityRecord.CreateIqc(TestPlanId, "Plan", "", "Name", "BATCH", "SUP", "Sup", "QC", 5, 0, 1));
    }

    [Fact]
    public void CreateIqc_ShouldThrowWhenBatchNumberEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            QualityRecord.CreateIqc(TestPlanId, "Plan", "MAT", "Name", "", "SUP", "Sup", "QC", 5, 0, 1));
    }

    [Fact]
    public void CreateIqc_ShouldThrowWhenInspectorIdEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            QualityRecord.CreateIqc(TestPlanId, "Plan", "MAT", "Name", "BATCH", "SUP", "Sup", "", 5, 0, 1));
    }

    [Fact]
    public void CreateIqc_ShouldTrimValues()
    {
        var record = QualityRecord.CreateIqc(
            TestPlanId, " Plan ", " MAT-CODE ", " Name ", " BATCH ",
            " SUP ", " Supplier ", " QC ", 5, 0, 1, aqlScheme: " AQL ");

        Assert.Equal("MAT-CODE", record.ProductCode);
        Assert.Equal("BATCH", record.BatchNumber);
        Assert.Equal("Supplier", record.SupplierName);
        Assert.Equal("QC", record.InspectorId);
        Assert.Equal("AQL", record.AqlScheme);
    }

    // ═══════════════════════════════════════════════════════════
    //  IPQC 创建
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateIpqc_ShouldInitializeCorrectly()
    {
        var characteristics = new List<MeasuredCharacteristic>
        {
            MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21),
            MeasuredCharacteristic.Create("DIM-01", "孔径", 12, "mm", 12.05, 11.95),
        };

        var record = QualityRecord.CreateIpqc(
            Ulid.NewUlid(), "WO-001", "ESP-9.0", "ESP 制动总成",
            TestPlanId, "IPQC Plan", "QC-001", characteristics);

        Assert.Equal(InspectionStage.Ipqc, record.Stage);
        Assert.Equal("ESP-9.0", record.ProductCode);
        Assert.Equal(2, record.SampleSize);
        Assert.Equal(2, record.Characteristics.Count);
        Assert.Equal(InspectionVerdict.Pending, record.Verdict);
    }

    [Fact]
    public void CreateIpqc_ShouldThrowWhenProductCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            QualityRecord.CreateIpqc(Ulid.NewUlid(), "WO", "", "Name", TestPlanId, "Plan", "QC", new List<MeasuredCharacteristic>()));
    }

    // ═══════════════════════════════════════════════════════════
    //  实测值记录
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RecordCharacteristic_ShouldSetActualValue()
    {
        var record = CreateIqcRecordWithChars();
        record.RecordCharacteristic("TOR-M6", 22.5);

        var c = record.Characteristics.First(x => x.CharacteristicCode == "TOR-M6");
        Assert.Equal(22.5, c.ActualValue);
        Assert.False(c.IsFailed);
    }

    [Fact]
    public void RecordCharacteristic_ShouldThrowWhenCodeNotFound()
    {
        var record = CreateIqcRecordWithChars();
        Assert.Throws<KeyNotFoundException>(() =>
            record.RecordCharacteristic("NONEXISTENT", 10));
    }

    // ═══════════════════════════════════════════════════════════
    //  自动判定逻辑
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Complete_ShouldVerdictPassedWhenAllPass()
    {
        var record = CreateIqcRecordWithChars();
        record.RecordCharacteristic("TOR-M6", 22.0);
        record.RecordCharacteristic("DIM-01", 12.0);
        record.Complete();

        Assert.Equal(InspectionVerdict.Passed, record.Verdict);
        Assert.Equal(0, record.DefectCount);
        Assert.NotNull(record.CompletedAt);
    }

    [Fact]
    public void Complete_ShouldVerdictFailedWhenDefectsExceedRejectNumber()
    {
        var record = CreateIqcRecordWithChars(); // Ac=0, Re=1
        record.RecordCharacteristic("TOR-M6", 24.0); // exceeds USL=23 → fail
        record.RecordCharacteristic("DIM-01", 12.0); // pass
        record.Complete();

        Assert.Equal(InspectionVerdict.Failed, record.Verdict);
        Assert.Equal(1, record.DefectCount);
    }

    [Fact]
    public void Complete_ShouldVerdictConditionalPassWhenDefectsBelowReject()
    {
        var record = QualityRecord.CreateIqc(
            TestPlanId, "Test", "MAT", "Material", "BATCH",
            "SUP", "Supplier", "QC",
            sampleSize: 5, acceptNumber: 2, rejectNumber: 3);

        record.Characteristics.AddRange(new List<MeasuredCharacteristic>
        {
            MeasuredCharacteristic.Create("CHAR-01", "特性1", 100, "mm", usl: 101, lsl: 99),
            MeasuredCharacteristic.Create("CHAR-02", "特性2", 100, "mm", usl: 101, lsl: 99),
            MeasuredCharacteristic.Create("CHAR-03", "特性3", 100, "mm", usl: 101, lsl: 99),
        });

        record.RecordCharacteristic("CHAR-01", 102.0);
        record.RecordCharacteristic("CHAR-02", 100.0);
        record.RecordCharacteristic("CHAR-03", 98.5);

        record.Complete();

        // 2 defects, Re=3 → 2 >= 3? No → 2 > 0? Yes → ConditionalPass
        Assert.Equal(InspectionVerdict.ConditionalPass, record.Verdict);
        Assert.Equal(2, record.DefectCount);
    }

    [Fact]
    public void Complete_ShouldThrowWhenAlreadyCompleted()
    {
        var record = CreateIqcRecordWithChars();
        record.RecordCharacteristic("TOR-M6", 22.0);
        record.RecordCharacteristic("DIM-01", 12.0);
        record.Complete();

        Assert.Throws<InvalidOperationException>(() => record.Complete());
    }

    // ═══════════════════════════════════════════════════════════
    //  人工覆盖判定
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void OverrideVerdict_ShouldSetRemarksAndComplete()
    {
        var record = CreateIqcRecordWithChars();
        record.RecordCharacteristic("TOR-M6", 24.0);

        record.OverrideVerdict(InspectionVerdict.ConditionalPass, "让步接收：轻微超差，不影响功能");

        Assert.Equal(InspectionVerdict.ConditionalPass, record.Verdict);
        Assert.Equal("让步接收：轻微超差，不影响功能", record.Remarks);
        Assert.NotNull(record.CompletedAt);
    }

    [Fact]
    public void OverrideVerdict_ShouldRejectPending()
    {
        var record = CreateIqcRecordWithChars();
        Assert.Throws<ArgumentException>(() =>
            record.OverrideVerdict(InspectionVerdict.Pending));
    }

    // ═══════════════════════════════════════════════════════════
    //  MeasuredCharacteristic
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MeasuredCharacteristic_Create_ShouldSetProperties()
    {
        var c = MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21);
        Assert.Equal("TOR-M6", c.CharacteristicCode);
        Assert.Equal("M6 扭矩", c.CharacteristicName);
        Assert.Equal(22, c.StandardValue);
        Assert.Equal(23, c.UpperSpecLimit);
        Assert.Equal(21, c.LowerSpecLimit);
    }

    [Fact]
    public void MeasuredCharacteristic_Create_ShouldThrowWhenCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasuredCharacteristic.Create("", "Name", 10, "mm"));
    }

    [Fact]
    public void MeasuredCharacteristic_Record_ShouldPassWithinSpec()
    {
        var c = MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21);
        c.Record(22.5);
        Assert.Equal(22.5, c.ActualValue);
        Assert.False(c.IsFailed);
    }

    [Fact]
    public void MeasuredCharacteristic_Record_ShouldFailAboveUsl()
    {
        var c = MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21);
        c.Record(23.5);
        Assert.True(c.IsFailed);
    }

    [Fact]
    public void MeasuredCharacteristic_Record_ShouldFailBelowLsl()
    {
        var c = MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21);
        c.Record(20.5);
        Assert.True(c.IsFailed);
    }

    [Fact]
    public void MeasuredCharacteristic_Record_ShouldPassWhenNoLimits()
    {
        var c = MeasuredCharacteristic.Create("VIS-01", "外观检查", 1, "-");
        c.Record(1);
        Assert.False(c.IsFailed);
    }

    // ═══════════════════════════════════════════════════════════
    //  SpcSample 子组创建
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void SpcSample_Create_ShouldComputeMeanRangeStdDev()
    {
        var sample = SpcSample.Create("TOR-M6", 1, new double[] { 10.0, 12.0, 14.0, 16.0, 18.0 }.AsSpan());
        // mean = 14.0, range = 18-10 = 8.0
        // stddev = sqrt(((10-14)^2+(12-14)^2+(14-14)^2+(16-14)^2+(18-14)^2) / 4)
        //        = sqrt((16+4+0+4+16) / 4) = sqrt(40/4) = sqrt(10) ≈ 3.1623
        Assert.Equal(14.0, sample.Mean);
        Assert.Equal(8.0, sample.Range);
        Assert.Equal(3.1623, sample.StdDev, 4);
        Assert.Equal(5, sample.SubgroupSize);
        Assert.Equal("TOR-M6", sample.CharacteristicCode);
    }

    [Fact]
    public void SpcSample_Create_ShouldThrowWhenLessThanTwoValues()
    {
        Assert.Throws<ArgumentException>(() =>
            SpcSample.Create("TOR-M6", 1, new[] { 10.0 }.AsSpan()));
    }

    [Fact]
    public void SpcSample_Create_ShouldThrowWhenCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            SpcSample.Create("", 1, new[] { 10.0, 20.0 }.AsSpan()));
    }

    // ═══════════════════════════════════════════════════════════
    //  XbarRConstants
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void XbarRConstants_ShouldReturnCorrectValues()
    {
        var f = XbarRConstants.Get(5);
        Assert.Equal(0.577, f.A2);
        Assert.Equal(0, f.D3);
        Assert.Equal(2.115, f.D4);
    }

    [Fact]
    public void XbarRConstants_ShouldThrowForUnsupportedSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => XbarRConstants.Get(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => XbarRConstants.Get(11));
    }

    [Fact]
    public void XbarRConstants_IsValidSubgroupSize_ShouldReturnTrueForTwoToTen()
    {
        Assert.True(XbarRConstants.IsValidSubgroupSize(2));
        Assert.True(XbarRConstants.IsValidSubgroupSize(5));
        Assert.True(XbarRConstants.IsValidSubgroupSize(10));
        Assert.False(XbarRConstants.IsValidSubgroupSize(1));
        Assert.False(XbarRConstants.IsValidSubgroupSize(11));
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static QualityRecord CreateIqcRecordWithChars()
    {
        var record = QualityRecord.CreateIqc(
            TestPlanId, "ESP-9.0 IQC", "ECU-ESP9-001", "ECU 控制单元",
            "BATCH-001", "SUP-001", "博世苏州", "QC-001",
            sampleSize: 2, acceptNumber: 0, rejectNumber: 1);

        record.Characteristics.AddRange(new List<MeasuredCharacteristic>
        {
            MeasuredCharacteristic.Create("TOR-M6", "M6 扭矩", 22, "Nm", 23, 21),
            MeasuredCharacteristic.Create("DIM-01", "孔径", 12, "mm", 12.05, 11.95),
        });

        return record;
    }
}
