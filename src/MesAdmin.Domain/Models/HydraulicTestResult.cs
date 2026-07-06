using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 电磁阀测试结果（12路 x 3 cycles）
/// </summary>
[MemoryPackable]
public sealed partial record SolenoidValveTest(
    int ValveIndex,          // 1-12 电磁阀编号
    bool ActuationPass,      // 动作响应是否合格
    double ResponseTimeMs,   // 响应时间 (ms)
    double? CoilResistance,  // 线圈电阻 (Ω)，可选
    string? FaultCode        // 故障代码（如 F001=开路, F002=短路）
);

/// <summary>
/// 液压功能测试状态枚举
/// </summary>
public enum HydraulicTestStatus
{
    /// <summary>等待测试</summary>
    Pending,
    /// <summary>测试进行中</summary>
    InProgress,
    /// <summary>建压阶段</summary>
    Pressurizing,
    /// <summary>保压阶段</summary>
    HoldingPressure,
    /// <summary>泄压阶段</summary>
    ReleasingPressure,
    /// <summary>测试完成 — 合格</summary>
    Passed,
    /// <summary>测试完成 — 不合格</summary>
    Failed,
    /// <summary>设备已锁定（不合格后）</summary>
    EquipmentLocked
}

/// <summary>
/// 100% 在线液压功能测试结果（T2.6）。
/// ESP 制动系统每件产品必经站4液压测试台，全自动测试：
///   1. 12 路电磁阀逐一动作测试（响应时间 + 线圈电阻检测）
///   2. 建压→保压→泄压循环（3 cycles）
///   3. 泄漏率检测（≤0.5 CC/hr）
///   4. 不合格自动锁止设备
/// </summary>
[MemoryPackable]
public partial class HydraulicTestResult
{
    /// <summary>主键（Ulid）</summary>
    public Ulid Id { get; set; }

    /// <summary>设备编码（EQ-HYD-01）</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>关联工单 Id</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>产品序列号（ESP 成品 S/N）</summary>
    public string? ProductSerial { get; set; }

    /// <summary>测试状态</summary>
    public HydraulicTestStatus Status { get; set; } = HydraulicTestStatus.Pending;

    /// <summary>测试循环编号（1-3）</summary>
    public int CycleNumber { get; set; }

    // ── 建压测试（cycle 1）──
    /// <summary>建压时间 (ms)，目标 ≤250ms</summary>
    public double? PressureBuildTimeMs { get; set; }
    /// <summary>建压时间是否合格</summary>
    public bool? PressureBuildPass { get; set; }

    // ── 保压测试（cycle 2）──
    /// <summary>保压压力 (bar)，目标 175-185 bar</summary>
    public double? HoldPressureBar { get; set; }
    /// <summary>保压压力是否合格</summary>
    public bool? HoldPressurePass { get; set; }

    // ── 泄压测试（cycle 3）──
    /// <summary>泄压时间 (ms)</summary>
    public double? PressureReleaseTimeMs { get; set; }
    /// <summary>泄压是否合格</summary>
    public bool? PressureReleasePass { get; set; }

    // ── 泄漏率 ──
    /// <summary>泄漏率 (CC/hr)，目标 ≤0.5</summary>
    public double? LeakRateCcHr { get; set; }
    /// <summary>泄漏率是否合格</summary>
    public bool? LeakRatePass { get; set; }

    // ── 12 路电磁阀测试结果 ──
    /// <summary>电磁阀测试列表（12路 × 3 cycles，JSONB 存储）</summary>
    public List<SolenoidValveTest> SolenoidTests { get; set; } = [];

    // ── 最终判定 ──
    /// <summary>总体是否合格</summary>
    public bool OverallPass { get; set; }
    /// <summary>失败原因（若有）</summary>
    public string? FailureReason { get; set; }

    // ── 设备锁定 ──
    /// <summary>是否触发设备锁</summary>
    public bool EquipmentLocked { get; set; }
    /// <summary>解锁人</summary>
    public string? UnlockedBy { get; set; }
    /// <summary>解锁时间</summary>
    public DateTimeOffset? UnlockedAt { get; set; }

    // ── 时间戳 ──
    /// <summary>测试开始时间</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>测试结束时间</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 创建新的液压测试记录。
    /// </summary>
    public static HydraulicTestResult Create(
        string equipmentCode,
        Ulid? orderId,
        string? productSerial,
        int cycleNumber)
    {
        var now = DateTimeOffset.UtcNow;
        return new HydraulicTestResult
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = equipmentCode,
            OrderId = orderId,
            ProductSerial = productSerial,
            Status = HydraulicTestStatus.InProgress,
            CycleNumber = cycleNumber,
            StartedAt = now,
            CreatedAt = now,
            SolenoidTests = new List<SolenoidValveTest>(12),
        };
    }

    /// <summary>记录建压测试结果</summary>
    public void RecordPressureBuild(double buildTimeMs)
    {
        PressureBuildTimeMs = buildTimeMs;
        PressureBuildPass = buildTimeMs <= 250.0;
        Status = HydraulicTestStatus.Pressurizing;
    }

    /// <summary>记录保压测试结果</summary>
    public void RecordHoldPressure(double pressureBar)
    {
        HoldPressureBar = pressureBar;
        HoldPressurePass = pressureBar >= 175.0 && pressureBar <= 185.0;
        Status = HydraulicTestStatus.HoldingPressure;
    }

    /// <summary>记录泄压测试结果</summary>
    public void RecordPressureRelease(double releaseTimeMs)
    {
        PressureReleaseTimeMs = releaseTimeMs;
        PressureReleasePass = releaseTimeMs <= 300.0; // 泄压时间 ≤300ms
        Status = HydraulicTestStatus.ReleasingPressure;
    }

    /// <summary>记录泄漏率</summary>
    public void RecordLeakRate(double leakRateCcHr)
    {
        LeakRateCcHr = leakRateCcHr;
        LeakRatePass = leakRateCcHr <= 0.5;
    }

    /// <summary>添加电磁阀测试结果</summary>
    public void AddSolenoidTest(SolenoidValveTest test)
    {
        SolenoidTests.Add(test);
    }

    /// <summary>
    /// 完成测试，执行最终判定。
    /// 所有项合格 → Passed；任一项不合格 → Failed + 设备锁定。
    /// </summary>
    public void Complete()
    {
        var allSolenoidPass = SolenoidTests.Count > 0 && SolenoidTests.All(t => t.ActuationPass);
        var allCyclesPass = (PressureBuildPass ?? true)
                          && (HoldPressurePass ?? true)
                          && (PressureReleasePass ?? true)
                          && (LeakRatePass ?? true);

        OverallPass = allSolenoidPass && allCyclesPass;
        CompletedAt = DateTimeOffset.UtcNow;

        if (!OverallPass)
        {
            // 收集失败原因
            var reasons = new List<string>();
            if (PressureBuildPass == false)
                reasons.Add($"建压时间 {PressureBuildTimeMs:F1}ms 超限");
            if (HoldPressurePass == false)
                reasons.Add($"保压压力 {HoldPressureBar:F1}bar 超限");
            if (PressureReleasePass == false)
                reasons.Add($"泄压时间 {PressureReleaseTimeMs:F1}ms 超限");
            if (LeakRatePass == false)
                reasons.Add($"泄漏率 {LeakRateCcHr:F2}CC/hr 超限");

            var failedSolenoids = SolenoidTests.Where(t => !t.ActuationPass).ToList();
            foreach (var s in failedSolenoids)
            {
                reasons.Add($"电磁阀#{s.ValveIndex} 响应 {s.ResponseTimeMs:F1}ms 超限 (故障:{s.FaultCode ?? "无"})");
            }

            FailureReason = string.Join("; ", reasons);
            Status = HydraulicTestStatus.Failed;
            EquipmentLocked = true; // 不合格自动锁设备
        }
        else
        {
            Status = HydraulicTestStatus.Passed;
        }
    }

    /// <summary>
    /// 质量工程师解锁设备。
    /// 设备锁定后必须由质量工程师确认解锁，Status 恢复为 Passed 以体现问题已解决。
    /// </summary>
    public void UnlockEquipment(string unlockedBy)
    {
        if (!EquipmentLocked)
            throw new InvalidOperationException("设备未锁定，无需解锁");

        UnlockedBy = unlockedBy;
        UnlockedAt = DateTimeOffset.UtcNow;
        EquipmentLocked = false;
        Status = HydraulicTestStatus.Passed;
    }
}
