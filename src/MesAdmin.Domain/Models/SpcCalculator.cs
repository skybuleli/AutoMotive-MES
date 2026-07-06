namespace MesAdmin.Domain.Models;

/// <summary>
/// SPC 统计计算工具类（T2.5）。
/// 零分配友好：所有方法使用 stackalloc Span&lt;double&gt; 避免堆分配。
/// 覆盖 Cpk/Ppk 计算、X̄-R 控制限、Western Electric 判异规则。
/// </summary>
public static class SpcCalculator
{
    #region Cpk / Ppk

    /// <summary>
    /// 计算过程能力指数 Cpk 和 Cp。
    /// 使用 ReadOnlySpan 避免分配。
    /// </summary>
    public static (double Cp, double Cpk, double Mean, double StdDev) CalculateCpk(
        ReadOnlySpan<double> values,
        double usl,
        double lsl)
    {
        if (values.Length < 2)
            throw new ArgumentException("至少需要 2 个测量值", nameof(values));
        if (usl <= lsl)
            throw new ArgumentException("USL 必须大于 LSL");

        var mean = CalculateMean(values);
        var stdDev = CalculateSampleStdDev(values, mean);

        if (stdDev == 0)
            return (double.PositiveInfinity, double.PositiveInfinity, mean, 0);

        var cp = (usl - lsl) / (6 * stdDev);
        var cpu = (usl - mean) / (3 * stdDev);
        var cpl = (mean - lsl) / (3 * stdDev);
        var cpk = Math.Min(cpu, cpl);

        return (Math.Round(cp, 4), Math.Round(cpk, 4), Math.Round(mean, 4), Math.Round(stdDev, 4));
    }

    /// <summary>
    /// 计算过程性能指数 Ppk 和 Pp（使用总体标准差）。
    /// </summary>
    public static (double Pp, double Ppk) CalculatePpk(
        ReadOnlySpan<double> values,
        double usl,
        double lsl)
    {
        if (values.Length < 2)
            throw new ArgumentException("至少需要 2 个测量值", nameof(values));

        var mean = CalculateMean(values);
        var stdDev = CalculatePopulationStdDev(values, mean);

        if (stdDev == 0)
            return (double.PositiveInfinity, double.PositiveInfinity);

        var pp = (usl - lsl) / (6 * stdDev);
        var ppu = (usl - mean) / (3 * stdDev);
        var ppl = (mean - lsl) / (3 * stdDev);
        var ppk = Math.Min(ppu, ppl);

        return (Math.Round(pp, 4), Math.Round(ppk, 4));
    }

    /// <summary>
    /// 计算 X̄-R 控制图的控制限。
    /// </summary>
    public static (double GrandMean, double MeanRange, double UclX, double LclX, double UclR, double LclR)
        CalculateXbarRControlLimits(ReadOnlySpan<SpcSample> subgroups)
    {
        if (subgroups.Length < 2)
            throw new ArgumentException("至少需要 2 个子组", nameof(subgroups));

        var n = subgroups.Length;
        double grandMeanSum = 0, meanRangeSum = 0;

        for (int i = 0; i < n; i++)
        {
            grandMeanSum += subgroups[i].Mean;
            meanRangeSum += subgroups[i].Range;
        }

        var grandMean = grandMeanSum / n;
        var meanRange = meanRangeSum / n;
        var subgroupSize = subgroups[0].SubgroupSize;

        var constants = XbarRConstants.Get(subgroupSize);

        var uclX = grandMean + constants.A2 * meanRange;
        var lclX = grandMean - constants.A2 * meanRange;
        var uclR = constants.D4 * meanRange;
        var lclR = constants.D3 * meanRange;

        return (Math.Round(grandMean, 4), Math.Round(meanRange, 4),
                Math.Round(uclX, 4), Math.Round(lclX, 4),
                Math.Round(uclR, 4), Math.Round(lclR, 4));
    }

    #endregion

    #region Western Electric Rules

    /// <summary>
    /// 检查 X̄ 控制图上的 Western Electric 判异规则。
    /// 返回触发的规则列表。
    /// </summary>
    public static List<SpcRuleAlert> CheckWesternElectricRules(
        ReadOnlySpan<SpcSample> recentSubgroups,
        double cl,
        double ucl,
        double lcl,
        string characteristicCode,
        Ulid? orderId = null,
        string? equipmentCode = null)
    {
        var alerts = new List<SpcRuleAlert>();
        if (recentSubgroups.Length == 0) return alerts;

        var sigma = (ucl - cl) / 3; // 估计 σ
        if (sigma <= 0) return alerts;

        // Zone 边界: A=2σ-3σ, B=1σ-2σ, C=0-1σ
        var zoneA = sigma * 2;
        var zoneB = sigma * 1;

        var n = recentSubgroups.Length;
        var lastSubgroup = recentSubgroups[^1];

        // ── Rule 1: 1 点超出 3σ 控制限 ──
        if (lastSubgroup.Mean > ucl || lastSubgroup.Mean < lcl)
        {
            alerts.Add(CreateAlert(SpcRuleType.Beyond3Sigma, characteristicCode,
                lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Critical,
                $"Rule 1: X̄={lastSubgroup.Mean:F4} 超出控制限 [UCL={ucl:F4}, LCL={lcl:F4}]"));
        }

        // ── Rule 2: 连续 3 点中有 2 点在 Zone A（同侧） ──
        if (n >= 3)
        {
            var last3 = recentSubgroups.Slice(n - 3, 3);
            if (CountInZoneA(last3, cl, zoneA, zoneB, upperSide: true) >= 2)
            {
                alerts.Add(CreateAlert(SpcRuleType.TwoOfThreeInZoneA, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.OutOfControl,
                    $"Rule 2: 连续 3 点中有 2 点在 Zone A 上侧"));
            }
            if (CountInZoneA(last3, cl, zoneA, zoneB, upperSide: false) >= 2)
            {
                alerts.Add(CreateAlert(SpcRuleType.TwoOfThreeInZoneA, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.OutOfControl,
                    $"Rule 2: 连续 3 点中有 2 点在 Zone A 下侧"));
            }
        }

        // ── Rule 3: 连续 5 点中有 4 点在 Zone B 或以上（同侧） ──
        if (n >= 5)
        {
            var last5 = recentSubgroups.Slice(n - 5, 5);
            if (CountInZoneBOrBeyond(last5, cl, upperSide: true) >= 4)
            {
                alerts.Add(CreateAlert(SpcRuleType.FourOfFiveInZoneB, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.OutOfControl,
                    $"Rule 3: 连续 5 点中有 4 点在 Zone B 上侧或以上"));
            }
            if (CountInZoneBOrBeyond(last5, cl, upperSide: false) >= 4)
            {
                alerts.Add(CreateAlert(SpcRuleType.FourOfFiveInZoneB, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.OutOfControl,
                    $"Rule 3: 连续 5 点中有 4 点在 Zone B 下侧或以下"));
            }
        }

        // ── Rule 4: 连续 8 点在 CL 同侧 ──
        if (n >= 8)
        {
            var last8 = recentSubgroups.Slice(n - 8, 8);
            var allAbove = true;
            var allBelow = true;
            for (int i = 0; i < 8; i++)
            {
                if (last8[i].Mean <= cl) allAbove = false;
                if (last8[i].Mean >= cl) allBelow = false;
            }
            if (allAbove)
                alerts.Add(CreateAlert(SpcRuleType.EightInZoneCOrBeyond, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Warning,
                    $"Rule 4: 连续 8 点在 CL 上侧"));
            if (allBelow)
                alerts.Add(CreateAlert(SpcRuleType.EightInZoneCOrBeyond, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Warning,
                    $"Rule 4: 连续 8 点在 CL 下侧"));
        }

        // ── Rule 5: 连续 6 点递增或递减 ──
        if (n >= 6)
        {
            var last6 = recentSubgroups.Slice(n - 6, 6);
            var increasing = true;
            var decreasing = true;
            for (int i = 1; i < 6; i++)
            {
                if (last6[i].Mean <= last6[i - 1].Mean) increasing = false;
                if (last6[i].Mean >= last6[i - 1].Mean) decreasing = false;
            }
            if (increasing || decreasing)
                alerts.Add(CreateAlert(SpcRuleType.SixInTrend, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Warning,
                    $"Rule 5: 连续 6 点{(increasing ? "递增" : "递减")}"));
        }

        // ── Rule 7: 连续 15 点在 Zone C（±1σ 内） ──
        if (n >= 15)
        {
            var last15 = recentSubgroups.Slice(n - 15, 15);
            var allInZoneC = true;
            for (int i = 0; i < 15; i++)
            {
                var dev = Math.Abs(last15[i].Mean - cl);
                if (dev > zoneB)
                {
                    allInZoneC = false;
                    break;
                }
            }
            if (allInZoneC)
                alerts.Add(CreateAlert(SpcRuleType.FifteenInZoneC, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Warning,
                    $"Rule 7: 连续 15 点在 Zone C 内"));
        }

        // ── Rule 8: 连续 8 点在 Zone C 以外 ──
        if (n >= 8)
        {
            var last8 = recentSubgroups.Slice(n - 8, 8);
            var allOutsideZoneC = true;
            for (int i = 0; i < 8; i++)
            {
                var dev = Math.Abs(last8[i].Mean - cl);
                if (dev <= zoneB)
                {
                    allOutsideZoneC = false;
                    break;
                }
            }
            if (allOutsideZoneC)
                alerts.Add(CreateAlert(SpcRuleType.EightOutsideZoneC, characteristicCode,
                    lastSubgroup.Id, orderId, equipmentCode, SpcAlertLevel.Warning,
                    $"Rule 8: 连续 8 点在 Zone C 以外"));
        }

        return alerts;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 计算均值。
    /// </summary>
    public static double CalculateMean(ReadOnlySpan<double> values)
    {
        if (values.Length == 0)
            throw new ArgumentException("数组不能为空", nameof(values));

        double sum = 0;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum / values.Length;
    }

    /// <summary>
    /// 计算样本标准差（n-1）。
    /// </summary>
    public static double CalculateSampleStdDev(ReadOnlySpan<double> values, double? mean = null)
    {
        if (values.Length < 2)
            throw new ArgumentException("至少需要 2 个值", nameof(values));

        var m = mean ?? CalculateMean(values);
        double sumSq = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var dev = values[i] - m;
            sumSq += dev * dev;
        }
        return Math.Sqrt(sumSq / (values.Length - 1));
    }

    /// <summary>
    /// 计算总体标准差（n）。
    /// </summary>
    public static double CalculatePopulationStdDev(ReadOnlySpan<double> values, double? mean = null)
    {
        if (values.Length == 0)
            throw new ArgumentException("数组不能为空", nameof(values));

        var m = mean ?? CalculateMean(values);
        double sumSq = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var dev = values[i] - m;
            sumSq += dev * dev;
        }
        return Math.Sqrt(sumSq / values.Length);
    }

    /// <summary>正态分布百分位数（Z 值 → 累积概率）</summary>
    public static double NormalCdf(double z)
    {
        // Abramowitz and Stegun 近似
        var a1 = 0.254829592;
        var a2 = -0.284496736;
        var a3 = 1.421413741;
        var a4 = -1.453152027;
        var a5 = 1.061405429;
        var p = 0.3275911;

        var sign = z < 0 ? -1 : 1;
        z = Math.Abs(z) / Math.Sqrt(2);
        var t = 1.0 / (1.0 + p * z);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-z * z);
        return 0.5 * (1.0 + sign * y);
    }

    private static int CountInZoneA(ReadOnlySpan<SpcSample> samples, double cl, double zoneA, double zoneB, bool upperSide)
    {
        int count = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var dev = upperSide ? samples[i].Mean - cl : cl - samples[i].Mean;
            if (dev > zoneB && dev <= zoneA)
                count++;
        }
        return count;
    }

    private static int CountInZoneBOrBeyond(ReadOnlySpan<SpcSample> samples, double cl, bool upperSide)
    {
        int count = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var dev = upperSide ? samples[i].Mean - cl : cl - samples[i].Mean;
            if (dev >= 0)
                count++;
        }
        return count;
    }

    private static SpcRuleAlert CreateAlert(SpcRuleType rule, string charCode, Ulid? subId,
        Ulid? orderId, string? equip, SpcAlertLevel level, string desc)
        => SpcRuleAlert.Create(rule, charCode, subId, orderId, equip, level, desc);

    #endregion
}
