using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.RealTime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 预防性维护集成测试（T2.17）：使用真实 PostgreSQL + MesDbContext 验证全链路。
/// 覆盖维护计划 CRUD → CycleBased 触发 → TimeBased 触发 → 工单创建 → 重复触发防护 → PLC 计数器复位。
/// </summary>
[Collection("DatabaseIntegration")]
public class PreventiveMaintenanceIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public PreventiveMaintenanceIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════════════
    //  MaintenancePlan CRUD
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Plan_AddAndGet_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, 100_000,
            "拧紧机定期标定", "检查扭矩传感器");

        await planRepo.AddAsync(plan, default);

        var loaded = await planRepo.GetByIdAsync(plan.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("EQ-TQ-01", loaded.EquipmentCode);
        Assert.Equal(MaintenanceType.CycleBased, loaded.MaintenanceType);
        Assert.Equal(100_000, loaded.ThresholdValue);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task Plan_GetByEquipment_ShouldReturnMatchingPlans()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压测试台",
            MaintenanceType.TimeBased, 30,
            "液压台密封件更换", "更换密封圈");
        await planRepo.AddAsync(plan, default);

        var plans = await planRepo.GetByEquipmentAsync("EQ-HYD-01", default);
        Assert.NotEmpty(plans);
        Assert.Contains(plans, p => p.Id == plan.Id);
    }

    [Fact]
    public async Task Plan_GetActive_ShouldExcludeInactive()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var active = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        var inactive = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "更换", "内容");
        inactive.IsActive = false;

        await planRepo.AddAsync(active, default);
        await planRepo.AddAsync(inactive, default);

        var allActive = await planRepo.GetActivePlansAsync(default);
        Assert.Contains(allActive, p => p.Id == active.Id);
        Assert.DoesNotContain(allActive, p => p.Id == inactive.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  MaintenanceWorkOrder CRUD
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task WorkOrder_AddAndGet_ShouldPersistAndRetrieve()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 先创建计划
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        await planRepo.AddAsync(plan, default);

        // 创建工单
        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100_000,
            "拧紧机定期标定", "检查扭矩传感器");

        await orderRepo.AddAsync(order, default);

        var loaded = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("EQ-TQ-01", loaded.EquipmentCode);
        Assert.Equal("拧紧机定期标定", loaded.Title);
        Assert.Equal(MaintenanceOrderStatus.Open, loaded.Status);
        Assert.StartsWith("MT-", loaded.OrderNumber);
        Assert.Equal(plan.Id, loaded.MaintenancePlanId);
    }

    [Fact]
    public async Task WorkOrder_GetList_ShouldSupportEquipmentAndStatusFilters()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        await planRepo.AddAsync(plan, default);

        // 创建 2 个工单：一个 Open，一个 Completed
        var open = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100_000,
            "标定", "内容");
        var completed = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, MaintenanceTriggerType.TimeTrigger, 30,
            "密封件更换", "内容");
        completed.Complete("OP-001", "完成");

        await orderRepo.AddAsync(open, default);
        await orderRepo.AddAsync(completed, default);

        // 按设备过滤
        var tqOrders = await orderRepo.GetListAsync(equipmentCode: "EQ-TQ-01", ct: default);
        Assert.Contains(tqOrders, o => o.Id == open.Id);
        Assert.DoesNotContain(tqOrders, o => o.Id == completed.Id);

        // 按状态过滤
        var openOrders = await orderRepo.GetListAsync(status: MaintenanceOrderStatus.Open, ct: default);
        Assert.Contains(openOrders, o => o.Id == open.Id);

        var doneOrders = await orderRepo.GetListAsync(status: MaintenanceOrderStatus.Completed, ct: default);
        Assert.Contains(doneOrders, o => o.Id == completed.Id);
    }

    // ═══════════════════════════════════════════════════════════
    //  CycleBased 触发：拧紧机每 10 万次循环标定
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CycleTrigger_ShouldCreateWorkOrder_WhenCycleCountExceedsThreshold()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建 CycleBased 计划（阈值 100K，初始 LastTriggeredCycleCount=0）
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, 100_000, "拧紧机定期标定", "检查扭矩传感器");
        plan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(plan, default);

        // 模拟达到 100K 循环 → IsCycleOverdue(100_000) = true
        Assert.True(plan.IsCycleOverdue(100_000));

        // 创建工单
        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, plan.EquipmentCode, plan.EquipmentName,
            plan.MaintenanceType, MaintenanceTriggerType.CycleTrigger, 100_000,
            plan.TaskDescription, plan.WorkContent);
        await orderRepo.AddAsync(order, default);

        // 更新计划（标记已触发）
        plan.LastTriggeredAt = DateTimeOffset.UtcNow;
        plan.LastTriggeredCycleCount = 100_000;
        await planRepo.UpdateAsync(plan, default);

        // 验证工单已持久化
        var loaded = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(MaintenanceOrderStatus.Open, loaded.Status);
        Assert.Equal(100_000, loaded.TriggerValue);
        Assert.Equal("EQ-TQ-01", loaded.EquipmentCode);

        // 验证计划已更新
        var updatedPlan = await planRepo.GetByIdAsync(plan.Id, default);
        Assert.NotNull(updatedPlan);
        Assert.Equal(100_000, updatedPlan.LastTriggeredCycleCount);
        Assert.NotNull(updatedPlan.LastTriggeredAt);
    }

    [Fact]
    public async Task CycleTrigger_ShouldNotTrigger_BeforeToleranceBoundary()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        plan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(plan, default);

        // 89,999 < 90,000 (100K × 0.9) → 不应触发
        Assert.False(plan.IsCycleOverdue(89_999));
    }

    [Fact]
    public async Task CycleTrigger_ShouldAccountForLastTriggeredCycleCount()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        plan.LastTriggeredCycleCount = 500_000;
        await planRepo.AddAsync(plan, default);

        // 上次触发后只跑了 50K → 不应触发
        // 540K - 500K = 40K < 90K
        Assert.False(plan.IsCycleOverdue(540_000));

        // 跑了 100K → 应触发
        // 600K - 500K = 100K >= 90K
        Assert.True(plan.IsCycleOverdue(600_000));
    }

    // ═══════════════════════════════════════════════════════════
    //  TimeBased 触发：液压台每月密封件更换
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task TimeTrigger_ShouldCreateWorkOrder_WhenLastTriggeredAtIsAged()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建 TimeBased 计划：30 天阈值，LastTriggeredAt 设为 30 天前
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压测试台",
            MaintenanceType.TimeBased, 30, "液压台密封件更换", "更换密封圈");
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-30);
        await planRepo.AddAsync(plan, default);

        // 30 >= 30 * 0.9 = 27 → 应触发
        Assert.True(plan.IsTimeOverdue());

        // 创建工单
        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, plan.EquipmentCode, plan.EquipmentName,
            plan.MaintenanceType, MaintenanceTriggerType.TimeTrigger, 30,
            plan.TaskDescription, plan.WorkContent);
        await orderRepo.AddAsync(order, default);

        // 更新计划
        plan.LastTriggeredAt = DateTimeOffset.UtcNow;
        await planRepo.UpdateAsync(plan, default);

        // 验证持久化
        var loaded = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal("液压台密封件更换", loaded.Title);
        Assert.Equal(MaintenanceTriggerType.TimeTrigger, loaded.TriggerType);
        Assert.Equal(30, loaded.TriggerValue);

        // 计划已更新
        var updatedPlan = await planRepo.GetByIdAsync(plan.Id, default);
        Assert.NotNull(updatedPlan);
        Assert.True(updatedPlan.LastTriggeredAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task TimeTrigger_ShouldNotTrigger_WithinTolerance()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "密封件更换", "内容");
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-26);
        await planRepo.AddAsync(plan, default);

        // 26 < 27 (30 × 0.9) → 不应触发
        Assert.False(plan.IsTimeOverdue());
    }

    // ═══════════════════════════════════════════════════════════
    //  重复触发防护
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task DuplicateTrigger_ShouldNotCreate_WhenOpenOrderExists()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建计划
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        plan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(plan, default);

        // 创建一个 Open 工单（同一计划）
        var existingOrder = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100_000,
            "标定", "内容");
        await orderRepo.AddAsync(existingOrder, default);

        // 检查是否有未关闭工单（模拟服务中的防重复检查）
        var openOrders = await orderRepo.GetOpenByPlanAsync(plan.Id, default);
        Assert.NotEmpty(openOrders);
        Assert.Contains(openOrders, o => o.Id == existingOrder.Id);

        // Open 工单应阻止新工单重复创建
        // 模拟服务会跳过：if (openOrders.Count > 0) return;
    }

    [Fact]
    public async Task DuplicateTrigger_ShouldAllowCreation_WhenPreviousIsCompleted()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建计划
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "密封件更换", "内容");
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-40);
        await planRepo.AddAsync(plan, default);

        // 创建已完成的工单
        var completed = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, MaintenanceTriggerType.TimeTrigger, 30,
            "密封件更换", "内容");
        completed.Complete("OP-001", "完成");
        await orderRepo.AddAsync(completed, default);

        // 已完成的工单不应阻挡新触发
        var openOrders = await orderRepo.GetOpenByPlanAsync(plan.Id, default);
        Assert.Empty(openOrders);

        // 可以创建新的
        var newOrder = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, MaintenanceTriggerType.TimeTrigger, 30,
            "密封件更换", "内容");
        await orderRepo.AddAsync(newOrder, default);

        var loaded = await orderRepo.GetByIdAsync(newOrder.Id, default);
        Assert.NotNull(loaded);
        Assert.Equal(MaintenanceOrderStatus.Open, loaded.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Edge cases
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task CycleTrigger_PLCCounterReset_ShouldNotTriggerPrematurely()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        plan.LastTriggeredCycleCount = 90_000;
        await planRepo.AddAsync(plan, default);

        // PLC 复位后 cycleCount=100，实际只跑了 100 次
        // 上次已跑 90K，复位后 100 - 90K = -89,900 < 90K
        Assert.False(plan.IsCycleOverdue(100));

        // 但跑了 100K 以上之后应触发
        // 180K - 90K = 90K >= 90K
        Assert.True(plan.IsCycleOverdue(180_000));
    }

    [Fact]
    public async Task WorkOrder_StartCompleteLifecycle_ShouldPersistEachTransition()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        await planRepo.AddAsync(plan, default);

        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100_000,
            "标定", "内容");
        await orderRepo.AddAsync(order, default);

        // Start
        order.Start("OP-001");
        await orderRepo.UpdateAsync(order, default);

        var started = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(started);
        Assert.Equal(MaintenanceOrderStatus.InProgress, started.Status);
        Assert.Equal("OP-001", started.AssignedTo);

        // Complete
        order.Complete("OP-001", "标定完成");
        await orderRepo.UpdateAsync(order, default);

        var completed = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(completed);
        Assert.Equal(MaintenanceOrderStatus.Completed, completed.Status);
        Assert.Equal("OP-001", completed.CompletedBy);
        Assert.Equal("标定完成", completed.CompletionRemarks);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task WorkOrder_Cancel_ShouldPersistCancelledStatus()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "密封件更换", "内容");
        await planRepo.AddAsync(plan, default);

        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, MaintenanceTriggerType.TimeTrigger, 30,
            "密封件更换", "内容");
        await orderRepo.AddAsync(order, default);

        // Cancel
        order.Cancel("计划变更");
        await orderRepo.UpdateAsync(order, default);

        var cancelled = await orderRepo.GetByIdAsync(order.Id, default);
        Assert.NotNull(cancelled);
        Assert.Equal(MaintenanceOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal("计划变更", cancelled.CompletionRemarks);
        Assert.NotNull(cancelled.CompletedAt);
    }

    // ═══════════════════════════════════════════════════════════
    //  Service 端到端测试：模拟 PlcStream + 自动触发
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Service_CycleTrigger_ShouldCreateWorkOrder()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建 CycleBased 计划（阈值 100K）
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机",
            MaintenanceType.CycleBased, 100_000, "拧紧机定期标定", "检查扭矩传感器");
        plan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(plan, default);

        // 创建 Service（使用 _fixture.Services 获取 IServiceScopeFactory 来模拟 DI）
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<PreventiveMaintenanceService>();
        // PlcDataAcquisitionPipeline 无需注册到 DatabaseFixture，
        // 因为 TriggerCheckAsync() 不访问 _pipeline（只在 ExecuteAsync 后台循环中订阅 PlcStream）。
        var svc = new PreventiveMaintenanceService(
            null!,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            logger);

        // 配置为立即检查
        svc.CheckInterval = TimeSpan.FromMilliseconds(100);
        svc.InitialDelay = TimeSpan.FromMilliseconds(10);

        // 注入虚拟 PLC 循环计数（模拟 PlcStream 已推送的数据）
        svc.SetLatestCycleCount("EQ-TQ-01", 100_000);

        // 手动触发一次检查
        await svc.TriggerCheckAsync();

        // 验证工单已创建（按本测试的计划 ID 定位，不受其他测试数据干扰）
        var ordersForPlan = await orderRepo.GetOpenByPlanAsync(plan.Id, ct: default);
        var workOrder = Assert.Single(ordersForPlan);
        Assert.Equal(MaintenanceOrderStatus.Open, workOrder.Status);
        Assert.Equal(100_000, workOrder.TriggerValue);
        Assert.Equal("拧紧机定期标定", workOrder.Title);

        // 验证计划已更新
        var updatedPlan = await planRepo.GetByIdAsync(plan.Id, default);
        Assert.NotNull(updatedPlan);
        Assert.Equal(100_000, updatedPlan.LastTriggeredCycleCount);
        Assert.NotNull(updatedPlan.LastTriggeredAt);
    }

    [Fact]
    public async Task Service_TimeTrigger_ShouldCreateWorkOrder()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建 TimeBased 计划（30 天阈值，LastTriggeredAt 设为 30 天前）
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压测试台",
            MaintenanceType.TimeBased, 30, "液压台密封件更换", "更换密封圈");
        plan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-30);
        await planRepo.AddAsync(plan, default);

        // 创建 Service
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<PreventiveMaintenanceService>();
        // PlcDataAcquisitionPipeline 无需注册到 DatabaseFixture，
        // 因为 TriggerCheckAsync() 不访问 _pipeline（只在 ExecuteAsync 后台循环中订阅 PlcStream）。
        var svc = new PreventiveMaintenanceService(
            null!,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            logger);

        svc.CheckInterval = TimeSpan.FromMilliseconds(100);
        svc.InitialDelay = TimeSpan.FromMilliseconds(10);

        // TimeBased 不依赖 PLC 数据，直接触发检查
        await svc.TriggerCheckAsync();

        // 验证工单已创建（按本测试的计划 ID 定位，不受其他测试数据干扰）
        var ordersForPlan = await orderRepo.GetOpenByPlanAsync(plan.Id, ct: default);
        var workOrder = Assert.Single(ordersForPlan);
        Assert.Equal(MaintenanceOrderStatus.Open, workOrder.Status);
        Assert.Equal("液压台密封件更换", workOrder.Title);
        Assert.Equal(MaintenanceTriggerType.TimeTrigger, workOrder.TriggerType);

        // 验证计划已更新
        var updatedPlan = await planRepo.GetByIdAsync(plan.Id, default);
        Assert.NotNull(updatedPlan);
        Assert.True(updatedPlan.LastTriggeredAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Service_Dedup_ShouldNotCreateDuplicate()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        // 创建计划
        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        plan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(plan, default);

        // 创建一个 Open 工单
        var existing = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(), plan.Id, "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 100_000,
            "第一次标定", "内容");
        await orderRepo.AddAsync(existing, default);

        // 创建 Service
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<PreventiveMaintenanceService>();
        // PlcDataAcquisitionPipeline 无需注册到 DatabaseFixture，
        // 因为 TriggerCheckAsync() 不访问 _pipeline（只在 ExecuteAsync 后台循环中订阅 PlcStream）。
        var svc = new PreventiveMaintenanceService(
            null!,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            logger);

        svc.CheckInterval = TimeSpan.FromMilliseconds(100);
        svc.InitialDelay = TimeSpan.FromMilliseconds(10);

        // 注入 PLC 循环计数
        svc.SetLatestCycleCount("EQ-TQ-01", 100_000);

        // 触发检查
        await svc.TriggerCheckAsync();

        // 按计划 ID 过滤工单（不受其他测试数据干扰）
        var ordersForPlan = await orderRepo.GetOpenByPlanAsync(plan.Id, ct: default);
        // 防重复逻辑生效：已存在的 Open 工单阻止创建新工单，所以应该仍只有 1 个
        Assert.Single(ordersForPlan);
    }

    // ═══════════════════════════════════════════════════════════
    //  Inactive plan check
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task InactivePlan_ShouldNotTriggerAnyCheck()
    {
        using var scope = _fixture.Services.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();

        var cyclePlan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-TQ-01", "拧紧机",
            MaintenanceType.CycleBased, 100_000, "标定", "内容");
        cyclePlan.IsActive = false;
        cyclePlan.LastTriggeredCycleCount = 0;
        await planRepo.AddAsync(cyclePlan, default);

        var timePlan = MaintenancePlan.Create(
            Ulid.NewUlid(), "EQ-HYD-01", "液压台",
            MaintenanceType.TimeBased, 30, "密封件更换", "内容");
        timePlan.IsActive = false;
        timePlan.LastTriggeredAt = DateTimeOffset.UtcNow.AddDays(-60);
        await planRepo.AddAsync(timePlan, default);

        // 不活跃计划不应触发
        Assert.False(cyclePlan.IsCycleOverdue(200_000));
        Assert.False(timePlan.IsTimeOverdue());

        // 不应该出现在活跃计划列表中
        var activePlans = await planRepo.GetActivePlansAsync(default);
        Assert.DoesNotContain(activePlans, p => p.Id == cyclePlan.Id);
        Assert.DoesNotContain(activePlans, p => p.Id == timePlan.Id);
    }
}
