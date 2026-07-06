using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 预防性维护工单状态机测试（T2.17）。
/// 覆盖 Create → Start → Complete / Cancel 完整生命周期 + 前置条件校验。
/// </summary>
public class MaintenanceWorkOrderTests
{
    private static readonly Ulid TestPlanId = Ulid.NewUlid();

    // ═══════════════════════════════════════════════════════════
    //  Create
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_ShouldInitializeWithOpenStatus()
    {
        var order = CreateTestOrder();

        Assert.Equal(MaintenanceOrderStatus.Open, order.Status);
        Assert.StartsWith("MT-", order.OrderNumber);
        Assert.Equal("EQ-TQ-01", order.EquipmentCode);
        Assert.Equal("拧紧机定期标定", order.Title);
        Assert.Equal(TestPlanId, order.MaintenancePlanId);
        Assert.Equal(MaintenanceType.CycleBased, order.MaintenanceType);
        Assert.Equal(MaintenanceTriggerType.CycleTrigger, order.TriggerType);
        Assert.Equal(100000.0, order.TriggerValue);
        Assert.Null(order.AssignedTo);
        Assert.Null(order.CompletedBy);
        Assert.Null(order.CompletedAt);
    }

    [Fact]
    public void Create_ShouldThrowWhenEquipmentCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenanceWorkOrder.Create(
                Ulid.NewUlid(), TestPlanId, "", "Name",
                MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100,
                "Title", "Desc"));
    }

    [Fact]
    public void Create_ShouldThrowWhenTitleEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenanceWorkOrder.Create(
                Ulid.NewUlid(), TestPlanId, "EQ-TQ-01", "Name",
                MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100,
                "", "Desc"));
    }

    [Fact]
    public void Create_ShouldTrimValues()
    {
        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), TestPlanId, " EQ-TQ-01 ", " 拧紧机 ",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100,
            " 标定 ", " 内容 ");

        Assert.Equal("EQ-TQ-01", order.EquipmentCode);
        Assert.Equal("拧紧机", order.EquipmentName);
        Assert.Equal("标定", order.Title);
        Assert.Equal("内容", order.Description);
    }

    // ═══════════════════════════════════════════════════════════
    //  Start
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Start_ShouldTransitionToInProgress()
    {
        var order = CreateTestOrder();
        var result = order.Start("OP-001");

        Assert.True(result);
        Assert.Equal(MaintenanceOrderStatus.InProgress, order.Status);
        Assert.Equal("OP-001", order.AssignedTo);
    }

    [Fact]
    public void Start_ShouldRejectWhenAlreadyInProgress()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");

        var result = order.Start("OP-002");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.InProgress, order.Status);
    }

    [Fact]
    public void Start_ShouldRejectWhenCompleted()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");
        order.Complete("OP-001", "完成");

        var result = order.Start("OP-002");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Start_ShouldRejectWhenCancelled()
    {
        var order = CreateTestOrder();
        order.Cancel("不需要");

        var result = order.Start("OP-001");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Start_ShouldThrowWhenAssignedToEmpty()
    {
        var order = CreateTestOrder();
        Assert.Throws<ArgumentException>(() => order.Start(""));
    }

    // ═══════════════════════════════════════════════════════════
    //  Complete
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Complete_ShouldTransitionToCompleted()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");

        var before = DateTimeOffset.UtcNow;
        var result = order.Complete("OP-001", "标定完成，偏差<1%");
        var after = DateTimeOffset.UtcNow;

        Assert.True(result);
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
        Assert.Equal("OP-001", order.CompletedBy);
        Assert.Equal("标定完成，偏差<1%", order.CompletionRemarks);
        Assert.NotNull(order.CompletedAt);
        Assert.True(order.CompletedAt >= before && order.CompletedAt <= after);
    }

    [Fact]
    public void Complete_ShouldWorkFromOpenDirectly()
    {
        // 允许从 Open 直接到 Completed（跳过 InProgress）
        var order = CreateTestOrder();
        var result = order.Complete("OP-001", "直接完成");

        Assert.True(result);
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Complete_ShouldRejectWhenAlreadyCompleted()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");
        order.Complete("OP-001", "完成");

        var result = order.Complete("OP-002", "再次完成");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Complete_ShouldRejectWhenCancelled()
    {
        var order = CreateTestOrder();
        order.Cancel("不需要");

        var result = order.Complete("OP-001", "完成");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Complete_ShouldThrowWhenCompletedByEmpty()
    {
        var order = CreateTestOrder();
        Assert.Throws<ArgumentException>(() => order.Complete("", "备注"));
    }

    // ═══════════════════════════════════════════════════════════
    //  Cancel
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Cancel_ShouldTransitionToCancelled()
    {
        var order = CreateTestOrder();
        var result = order.Cancel("计划变更，无需维护");

        Assert.True(result);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
        Assert.Equal("计划变更，无需维护", order.CompletionRemarks);
        Assert.NotNull(order.CompletedAt);
    }

    [Fact]
    public void Cancel_ShouldWorkFromInProgress()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");
        var result = order.Cancel("执行中发现无需标定");

        Assert.True(result);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_ShouldRejectWhenAlreadyCancelled()
    {
        var order = CreateTestOrder();
        order.Cancel("不需要");

        var result = order.Cancel("再次取消");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_ShouldRejectWhenCompleted()
    {
        var order = CreateTestOrder();
        order.Start("OP-001");
        order.Complete("OP-001", "完成");

        var result = order.Cancel("取消");

        Assert.False(result);
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  完整生命周期
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FullLifecycle_Open_Start_Complete_ShouldSucceed()
    {
        var order = CreateTestOrder();

        Assert.Equal(MaintenanceOrderStatus.Open, order.Status);
        Assert.True(order.Start("OP-001"));
        Assert.Equal(MaintenanceOrderStatus.InProgress, order.Status);
        Assert.True(order.Complete("OP-001", "维护完成，一切正常"));
        Assert.Equal(MaintenanceOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void FullLifecycle_Open_Cancel_ShouldSucceed()
    {
        var order = CreateTestOrder();

        Assert.Equal(MaintenanceOrderStatus.Open, order.Status);
        Assert.True(order.Cancel("取消"));
        Assert.Equal(MaintenanceOrderStatus.Cancelled, order.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static MaintenanceWorkOrder CreateTestOrder()
        => MaintenanceWorkOrder.Create(
            Ulid.NewUlid(),
            TestPlanId,
            "EQ-TQ-01",
            "螺栓拧紧机",
            MaintenanceType.CycleBased,
            MaintenanceTriggerType.CycleTrigger,
            100_000,
            "拧紧机定期标定",
            "1. 检查扭矩传感器零点偏移\n2. 使用标定仪进行测量");
}

// ═══════════════════════════════════════════════════════════════
//  MaintenancePlan 触发判定测试
// ═══════════════════════════════════════════════════════════════

public class MaintenancePlanTests
{
    // ═══════════════════════════════════════════════════════════
    //  Create
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Create_ShouldInitializeCorrectly()
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, 100_000,
            "拧紧机定期标定", "检查扭矩传感器");

        Assert.Equal("EQ-TQ-01", plan.EquipmentCode);
        Assert.Equal("螺栓拧紧机", plan.EquipmentName);
        Assert.Equal(MaintenanceType.CycleBased, plan.MaintenanceType);
        Assert.Equal(100_000, plan.ThresholdValue);
        Assert.True(plan.IsActive);
        Assert.Null(plan.LastTriggeredAt);
        Assert.Null(plan.LastTriggeredCycleCount);
    }

    [Fact]
    public void Create_ShouldThrowWhenEquipmentCodeEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenancePlan.Create(Ulid.NewUlid(), "", "Name",
                MaintenanceType.CycleBased, 100, "Task", "Content"));
    }

    [Fact]
    public void Create_ShouldThrowWhenTaskDescriptionEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            MaintenancePlan.Create(Ulid.NewUlid(), "EQ-01", "Name",
                MaintenanceType.CycleBased, 100, "", "Content"));
    }

    [Fact]
    public void Create_ShouldTrimValues()
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), " EQ-TQ-01 ", " 拧紧机 ",
            MaintenanceType.CycleBased, 100_000,
            " 标定 ", " 内容 ");

        Assert.Equal("EQ-TQ-01", plan.EquipmentCode);
        Assert.Equal("拧紧机", plan.EquipmentName);
        Assert.Equal("标定", plan.TaskDescription);
        Assert.Equal("内容", plan.WorkContent);
    }

    [Fact]
    public void Create_ShouldSetActiveByDefault()
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-01", "Name",
            MaintenanceType.TimeBased, 30, "Task", "Content");

        Assert.True(plan.IsActive);
    }

    // ═══════════════════════════════════════════════════════════
    //  IsCycleOverdue — CycleBased
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsCycleOverdue_ShouldReturnTrueWhenThresholdExceeded()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        // LastTriggeredCycleCount=0, currentCycleCount=100_000 → overdue at 90,000 (100K × 0.9)
        Assert.True(plan.IsCycleOverdue(100_000));
    }

    [Fact]
    public void IsCycleOverdue_ShouldReturnFalseBeforeTolerance()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        // 89,999 < 90,000 (100K × 0.9)
        Assert.False(plan.IsCycleOverdue(89_999));
    }

    [Fact]
    public void IsCycleOverdue_ShouldReturnTrueAtToleranceBoundary()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        // 90,000 == 90,000 (100K × 0.9)
        Assert.True(plan.IsCycleOverdue(90_000));
    }

    [Fact]
    public void IsCycleOverdue_ShouldAccountForLastTriggeredCycleCount()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        plan.LastTriggeredCycleCount = 50_000;

        // cycles since last = 120_000 - 50_000 = 70_000 < 90_000
        Assert.False(plan.IsCycleOverdue(120_000));

        // cycles since last = 140_000 - 50_000 = 90_000 >= 90_000
        Assert.True(plan.IsCycleOverdue(140_000));
    }

    [Fact]
    public void IsCycleOverdue_ShouldReturnFalseWhenNotActive()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        plan.IsActive = false;

        Assert.False(plan.IsCycleOverdue(200_000));
    }

    [Fact]
    public void IsCycleOverdue_ShouldReturnFalseForTimeBased()
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "Task", "Content");

        Assert.False(plan.IsCycleOverdue(100));
    }

    [Fact]
    public void IsCycleOverdue_ShouldHandleFirstTriggerWithNullCycleCount()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        plan.LastTriggeredCycleCount = null;

        // When LastTriggeredCycleCount is null, uses currentCycleCount directly
        Assert.False(plan.IsCycleOverdue(89_999));
        Assert.True(plan.IsCycleOverdue(90_000));
    }

    // ═══════════════════════════════════════════════════════════
    //  IsTimeOverdue — TimeBased
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsTimeOverdue_ShouldReturnTrueWhenPastThreshold()
    {
        var plan = CreateTimePlan(thresholdDays: 30);
        // 创建时间是 UtcNow，所以立即检查不会过期
        // 模拟未来的场景：将 LastTriggeredAt 设为 27 天前
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-27);

        // 27 >= 30 * 0.9 = 27 → 刚好在边界
        Assert.True(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsTimeOverdue_ShouldReturnFalseBeforeTolerance()
    {
        var plan = CreateTimePlan(thresholdDays: 30);
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-26);

        // 26 < 27 (30 × 0.9)
        Assert.False(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsTimeOverdue_ShouldUseCreatedAtWhenLastTriggeredIsNull()
    {
        // CreatedAt 回退路径：当 LastTriggeredAt 为 null 时，使用 CreatedAt
        // 模拟一个 30 天前创建的从未触发过的计划
        var plan = new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-HYD-01",
            EquipmentName = "液压测试台",
            MaintenanceType = MaintenanceType.TimeBased,
            ThresholdValue = 30,
            TaskDescription = "密封件更换",
            WorkContent = "更换密封圈",
            IsActive = true,
            LastTriggeredAt = null,
            LastTriggeredCycleCount = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };

        // 30 >= 30 * 0.9 = 27
        Assert.True(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsTimeOverdue_ShouldReturnFalseWhenNotActive()
    {
        var plan = CreateTimePlan(thresholdDays: 30);
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-30);
        plan.IsActive = false;

        Assert.False(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsTimeOverdue_ShouldReturnFalseForCycleBased()
    {
        var plan = CreateCyclePlan(threshold: 100_000);

        Assert.False(plan.IsTimeOverdue());
    }

    // ═══════════════════════════════════════════════════════════
    //  Edge cases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void IsCycleOverdue_CustomTolerance_ShouldBeRespected()
    {
        var plan = CreateCyclePlan(threshold: 100_000);

        // 默认 tolerance=0.9 → 90,000
        // 自定义 tolerance=0.95 → 95,000
        Assert.False(plan.IsCycleOverdue(92_000, tolerance: 0.95));
        Assert.True(plan.IsCycleOverdue(95_000, tolerance: 0.95));
    }

    [Fact]
    public void IsTimeOverdue_ShouldUseLastTriggeredAtWhenSet()
    {
        var plan = CreateTimePlan(thresholdDays: 30);
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-28);

        // 28 >= 27 (30 × 0.9) → 过期
        Assert.True(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsTimeOverdue_JustBeforeTolerance_ShouldReturnFalse()
    {
        var plan = CreateTimePlan(thresholdDays: 30);
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-26).AddHours(-23);

        // 26.958 < 27
        Assert.False(plan.IsTimeOverdue());
    }

    [Fact]
    public void IsCycleOverdue_ZeroCycleCount_ShouldNotTrigger()
    {
        var plan = CreateCyclePlan(threshold: 100_000);
        plan.LastTriggeredCycleCount = 0;

        // 还没有任何循环
        Assert.False(plan.IsCycleOverdue(0));
    }

    [Fact]
    public void IsCycleOverdue_ShouldReturnFalseWhenCounterReset()
    {
        // PLC 计数器复位后的情况：currentCycleCount < LastTriggeredCycleCount
        // 这会导致负数差值，负数 >= 阈值 返回 false，正确行为
        var plan = CreateCyclePlan(threshold: 100_000);
        plan.LastTriggeredCycleCount = 90_000;

        // 计数器复位到 100 → 100 - 90_000 = -89_900 < 90_000
        Assert.False(plan.IsCycleOverdue(100));

        // 即使增加到刚好到达旧值，差值仍为 0
        Assert.False(plan.IsCycleOverdue(90_000));

        // 超过旧值后才开始累积
        Assert.True(plan.IsCycleOverdue(180_000));  // 180K - 90K = 90K >= 90K
    }

    // ═══════════════════════════════════════════════════════════
    //  测试辅助方法
    // ═══════════════════════════════════════════════════════════

    private static MaintenancePlan CreateCyclePlan(double threshold)
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, threshold,
            "拧紧机定期标定", "检查扭矩传感器");
        plan.LastTriggeredCycleCount = 0;
        return plan;
    }

    private static MaintenancePlan CreateTimePlan(int thresholdDays)
    {
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压测试台",
            MaintenanceType.TimeBased, thresholdDays,
            "液压台密封件更换", "更换密封圈");
        // LastTriggeredAt 默认就是 null，此处显式保留
        return plan;
    }
}
