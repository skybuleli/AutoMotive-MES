using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Plc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using ZLogger;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// 预防性维护服务（T2.17）。
/// 每 30 分钟检查维护计划，在达到阈值时自动创建维护工单。
///
/// 内置维护计划（种子数据）：
/// - EQ-TQ-01 / EQ-TQ-02：拧紧机标定（每 100,000 次循环）
/// - EQ-HYD-01：液压台密封件更换（每 30 天）
/// - EQ-FT-01：终检台传感器标定（每 50,000 次循环）
/// - EQ-FLS-01：刷写台固件检查（每 30 天）
///
/// CycleBased 计划：订阅 PlcStream 跟踪每台设备的最新循环次数。
/// TimeBased 计划：基于 LastTriggeredAt + 阈值天数判断。
/// </summary>
public sealed class PreventiveMaintenanceService : BackgroundService, IAsyncDisposable
{
    private readonly PlcDataAcquisitionPipeline _pipeline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreventiveMaintenanceService> _logger;
    /// <summary>检查间隔（测试时可缩短）</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(30);
    /// <summary>首次启动延迟（测试时可缩短）</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(60);

    // 从 PlcStream 累计的最新循环次数（每设备）
    internal readonly Dictionary<string, long> _latestCycleCounts = new();
    internal readonly object _lock = new();
    internal IDisposable? _subscription;
    private volatile bool _disposed;

    /// <summary>测试用：注入虚拟 PLC 循环计数（无需真实的 PlcStream 订阅）</summary>
    public void SetLatestCycleCount(string equipmentCode, long cycleCount)
    {
        lock (_lock)
        {
            _latestCycleCounts[equipmentCode] = cycleCount;
        }
    }

    public PreventiveMaintenanceService(
        PlcDataAcquisitionPipeline pipeline,
        IServiceScopeFactory scopeFactory,
        ILogger<PreventiveMaintenanceService> logger)
    {
        _pipeline = pipeline;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 首次启动后延迟再开始检查，给 PLC 管道充分初始化时间
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        // 订阅 PlcStream 跟踪循环次数
        _subscription = _pipeline.PlcStream
            .Subscribe(OnPlcSnapshot);

        _logger.LogInformation("预防性维护服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
                var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

                var plans = await planRepo.GetActivePlansAsync(stoppingToken);

                // 如果没有任何维护计划，创建种子数据
                if (plans.Count == 0)
                {
                    await SeedPlansAsync(planRepo, stoppingToken);
                    plans = await planRepo.GetActivePlansAsync(stoppingToken);
                }

                // 锁定获取当前循环计数快照
                Dictionary<string, long> currentCycleCounts;
                lock (_lock)
                {
                    currentCycleCounts = new Dictionary<string, long>(_latestCycleCounts);
                }

                foreach (var plan in plans)
                {
                    await CheckAndTriggerAsync(plan, currentCycleCounts, orderRepo, planRepo, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "预防性维护服务检查异常: {Message}", ex.Message);
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }

            if (_disposed) break;
        }
    }

    private async Task CheckAndTriggerAsync(
        MaintenancePlan plan,
        Dictionary<string, long> cycleCounts,
        IMaintenanceWorkOrderRepository orderRepo,
        IMaintenancePlanRepository planRepo,
        CancellationToken ct)
    {
        // 检查是否已有未关闭的工单（防止重复触发）
        var openOrders = await orderRepo.GetOpenByPlanAsync(plan.Id, ct);
        if (openOrders.Count > 0)
            return;

        bool shouldTrigger = false;
        double triggerValue = 0;
        MaintenanceTriggerType triggerType;

        if (plan.MaintenanceType == MaintenanceType.CycleBased)
        {
            if (!cycleCounts.TryGetValue(plan.EquipmentCode, out var currentCycle) || currentCycle <= 0)
                return; // 尚无 PLC 数据

            if (!plan.IsCycleOverdue(currentCycle))
                return;

            shouldTrigger = true;
            triggerValue = currentCycle;
            triggerType = MaintenanceTriggerType.CycleTrigger;

            // 更新计划的上次触发信息
            plan.LastTriggeredAt = DateTimeOffset.UtcNow;
            plan.LastTriggeredCycleCount = currentCycle;
            await planRepo.UpdateAsync(plan, ct);
        }
        else if (plan.MaintenanceType == MaintenanceType.TimeBased)
        {
            if (!plan.IsTimeOverdue())
                return;

            shouldTrigger = true;
            triggerValue = plan.ThresholdValue;
            triggerType = MaintenanceTriggerType.TimeTrigger;

            plan.LastTriggeredAt = DateTimeOffset.UtcNow;
            await planRepo.UpdateAsync(plan, ct);
        }
        else
        {
            return;
        }

        if (!shouldTrigger)
            return;

        // 创建维护工单
        var order = MaintenanceWorkOrder.Create(
            Ulid.NewUlid(),
            plan.Id,
            plan.EquipmentCode,
            plan.EquipmentName,
            plan.MaintenanceType,
            triggerType,
            triggerValue,
            plan.TaskDescription,
            plan.WorkContent);

        await orderRepo.AddAsync(order, ct);

        _logger.LogInformation("预防性维护工单已创建: {OrderNumber} - {EquipmentCode} - {Task}",
            order.OrderNumber, plan.EquipmentCode, plan.TaskDescription);
    }

    /// <summary>
    /// 创建默认的预防性维护计划（种子数据）。
    /// </summary>
    private static async Task SeedPlansAsync(
        IMaintenancePlanRepository planRepo,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // 拧紧机标定：每 100,000 次循环
        await planRepo.AddAsync(new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-TQ-01",
            EquipmentName = "螺栓拧紧机",
            MaintenanceType = MaintenanceType.CycleBased,
            ThresholdValue = 100_000,
            TaskDescription = "拧紧机定期标定",
            WorkContent = "1. 检查扭矩传感器零点偏移\n2. 使用标定仪测量 M6/M8 实际扭矩 vs 设定值（5 次取均值）\n3. 偏差 >3% 时调整扭矩系数\n4. 记录标定前后数据\n5. 填写标定记录表",
            IsActive = true,
            LastTriggeredAt = now,
            LastTriggeredCycleCount = 0,
            CreatedAt = now,
        });

        await planRepo.AddAsync(new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-TQ-02",
            EquipmentName = "备用拧紧机",
            MaintenanceType = MaintenanceType.CycleBased,
            ThresholdValue = 100_000,
            TaskDescription = "拧紧机定期标定",
            WorkContent = "1. 检查扭矩传感器零点偏移\n2. 使用标定仪测量 M6/M8 实际扭矩 vs 设定值（5 次取均值）\n3. 偏差 >3% 时调整扭矩系数\n4. 记录标定前后数据\n5. 填写标定记录表",
            IsActive = true,
            LastTriggeredAt = now,
            LastTriggeredCycleCount = 0,
            CreatedAt = now,
        });

        // 液压台密封件更换：每 30 天
        await planRepo.AddAsync(new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-HYD-01",
            EquipmentName = "液压测试台",
            MaintenanceType = MaintenanceType.TimeBased,
            ThresholdValue = 30,
            TaskDescription = "液压台密封件更换",
            WorkContent = "1. 关闭液压系统并泄压\n2. 更换所有 O 型密封圈（规格 12×2mm / 18×2.5mm）\n3. 更换高压油管密封垫\n4. 检查油位，补充液压油（ISO VG 46）\n5. 空载循环 10 次确认无泄漏\n6. 填写维护记录表",
            IsActive = true,
            LastTriggeredAt = now,
            LastTriggeredCycleCount = null,
            CreatedAt = now,
        });

        // 功能终检台标定：每 50,000 次循环
        await planRepo.AddAsync(new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-FT-01",
            EquipmentName = "功能终检台",
            MaintenanceType = MaintenanceType.CycleBased,
            ThresholdValue = 50_000,
            TaskDescription = "终检台传感器标定",
            WorkContent = "1. 检查 CAN 通信延迟测量精度\n2. 使用标准泄漏测试件验证泄漏率测量\n3. 检查压力传感器零点\n4. 记录标定数据\n5. 填写标定记录表",
            IsActive = true,
            LastTriggeredAt = now,
            LastTriggeredCycleCount = 0,
            CreatedAt = now,
        });

        // ECU 刷写台维护：每 30 天
        await planRepo.AddAsync(new MaintenancePlan
        {
            Id = Ulid.NewUlid(),
            EquipmentCode = "EQ-FLS-01",
            EquipmentName = "ECU 刷写台",
            MaintenanceType = MaintenanceType.TimeBased,
            ThresholdValue = 30,
            TaskDescription = "刷写台固件检查与维护",
            WorkContent = "1. 检查 ECU 刷写夹具接触状态\n2. 验证最新固件版本已部署\n3. 刷写测试件并校验 CRC\n4. 清洁刷写接口触点\n5. 填写维护记录表",
            IsActive = true,
            LastTriggeredAt = now,
            LastTriggeredCycleCount = null,
            CreatedAt = now,
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _subscription?.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>处理 PLC 快照，更新循环计数。</summary>
    private void OnPlcSnapshot(PlcSnapshot snapshot)
    {
        lock (_lock)
        {
            _latestCycleCounts[snapshot.EquipmentCode] = snapshot.CycleCount;
        }
    }

    /// <summary>测试用：手动触发一次检查和工单创建。</summary>
    public async Task TriggerCheckAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var planRepo = scope.ServiceProvider.GetRequiredService<IMaintenancePlanRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var plans = await planRepo.GetActivePlansAsync(ct);
        if (plans.Count == 0)
        {
            await SeedPlansAsync(planRepo, ct);
            plans = await planRepo.GetActivePlansAsync(ct);
        }

        Dictionary<string, long> currentCycleCounts;
        lock (_lock)
        {
            currentCycleCounts = new Dictionary<string, long>(_latestCycleCounts);
        }

        foreach (var plan in plans)
        {
            await CheckAndTriggerAsync(plan, currentCycleCounts, orderRepo, planRepo, ct);
        }
    }
}
