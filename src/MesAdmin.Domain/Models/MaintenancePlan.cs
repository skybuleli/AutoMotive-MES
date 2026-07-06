using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 预防性维护计划（T2.17）。
/// 定义每台设备的维护规则：
/// - CycleBased: 拧紧机每 10 万次循环标定
/// - TimeBased:  液压台每月密封件更换
/// </summary>
public enum MaintenanceType
{
    /// <summary>基于循环次数触发（拧紧机 10 万次标定）</summary>
    CycleBased = 0,

    /// <summary>基于时间间隔触发（液压台每月密封件更换）</summary>
    TimeBased = 1,
}

/// <summary>
/// 预防性维护计划实体。
/// 每个计划定义一台设备的特定维护规则（类型 + 阈值 + 任务描述）。
/// </summary>
[MemoryPackable]
public partial class MaintenancePlan
{
    public Ulid Id { get; set; }

    /// <summary>设备编码（如 EQ-TQ-01）</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>设备名称</summary>
    public string EquipmentName { get; set; } = string.Empty;

    /// <summary>维护类型</summary>
    public MaintenanceType MaintenanceType { get; set; }

    /// <summary>阈值（循环次数 100000 或 天数 30）</summary>
    public double ThresholdValue { get; set; }

    /// <summary>维护任务描述（如 "拧紧机定期标定"）</summary>
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>具体工作内容</summary>
    public string WorkContent { get; set; } = string.Empty;

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>上次触发时间（防止重复创建工单）</summary>
    public DateTimeOffset? LastTriggeredAt { get; set; }

    /// <summary>上次触发时的循环计数（CycleBased 用）</summary>
    public long? LastTriggeredCycleCount { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static MaintenancePlan Create(
        Ulid id,
        string equipmentCode,
        string equipmentName,
        MaintenanceType maintenanceType,
        double thresholdValue,
        string taskDescription,
        string workContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDescription);

        return new MaintenancePlan
        {
            Id = id,
            EquipmentCode = equipmentCode.Trim(),
            EquipmentName = equipmentName.Trim(),
            MaintenanceType = maintenanceType,
            ThresholdValue = thresholdValue,
            TaskDescription = taskDescription.Trim(),
            WorkContent = workContent.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// 判断是否需要触发新的维护工单（CycleBased: 循环次数超过阈值+波动范围）。
    /// </summary>
    public bool IsCycleOverdue(long currentCycleCount, double tolerance = 0.9)
    {
        if (!IsActive || MaintenanceType != MaintenanceType.CycleBased)
            return false;

        var cyclesSinceLast = LastTriggeredCycleCount.HasValue
            ? currentCycleCount - LastTriggeredCycleCount.Value
            : currentCycleCount;

        return cyclesSinceLast >= ThresholdValue * tolerance;
    }

    /// <summary>
    /// 判断是否需要触发新的维护工单（TimeBased: 上次触发或创建以来的天数）。
    /// </summary>
    public bool IsTimeOverdue()
    {
        if (!IsActive || MaintenanceType != MaintenanceType.TimeBased)
            return false;

        var since = LastTriggeredAt ?? CreatedAt;
        var elapsedDays = (DateTimeOffset.UtcNow - since).TotalDays;
        return elapsedDays >= ThresholdValue * 0.9;
    }
}
