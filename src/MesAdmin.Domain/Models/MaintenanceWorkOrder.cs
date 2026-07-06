using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 维护工单状态。
/// </summary>
public enum MaintenanceOrderStatus
{
    /// <summary>待执行</summary>
    Open = 0,

    /// <summary>执行中</summary>
    InProgress = 1,

    /// <summary>已完成</summary>
    Completed = 2,

    /// <summary>已取消</summary>
    Cancelled = 3,
}

/// <summary>
/// 触发方式。
/// </summary>
public enum MaintenanceTriggerType
{
    /// <summary>基于循环次数触发</summary>
    CycleTrigger = 0,

    /// <summary>基于时间间隔触发</summary>
    TimeTrigger = 1,
}

/// <summary>
/// 预防性维护工单（T2.17）。
/// 由 PreventiveMaintenanceService 在达到维护阈值时自动创建。
/// </summary>
[MemoryPackable]
public partial class MaintenanceWorkOrder
{
    public Ulid Id { get; set; }

    /// <summary>工单编号 MT-YYYYMMDD-NNNN</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>关联维护计划 Id</summary>
    public Ulid MaintenancePlanId { get; set; }

    /// <summary>设备编码</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>设备名称</summary>
    public string EquipmentName { get; set; } = string.Empty;

    /// <summary>维护类型</summary>
    public MaintenanceType MaintenanceType { get; set; }

    /// <summary>触发方式</summary>
    public MaintenanceTriggerType TriggerType { get; set; }

    /// <summary>触发时的计数值（CycleBased: 循环次数, TimeBased: 天数）</summary>
    public double TriggerValue { get; set; }

    /// <summary>任务标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>工作内容描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>当前状态</summary>
    public MaintenanceOrderStatus Status { get; set; } = MaintenanceOrderStatus.Open;

    /// <summary>执行人（员工号）</summary>
    public string? AssignedTo { get; set; }

    /// <summary>完成人</summary>
    public string? CompletedBy { get; set; }

    /// <summary>完成备注</summary>
    public string? CompletionRemarks { get; set; }

    /// <summary>完成时间</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static MaintenanceWorkOrder Create(
        Ulid id,
        Ulid maintenancePlanId,
        string equipmentCode,
        string equipmentName,
        MaintenanceType maintenanceType,
        MaintenanceTriggerType triggerType,
        double triggerValue,
        string title,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var now = DateTimeOffset.UtcNow;
        return new MaintenanceWorkOrder
        {
            Id = id,
            OrderNumber = $"MT-{now:yyyyMMdd}-{Random.Shared.Next(0, 9999):D4}",
            MaintenancePlanId = maintenancePlanId,
            EquipmentCode = equipmentCode.Trim(),
            EquipmentName = equipmentName.Trim(),
            MaintenanceType = maintenanceType,
            TriggerType = triggerType,
            TriggerValue = triggerValue,
            Title = title.Trim(),
            Description = description.Trim(),
            Status = MaintenanceOrderStatus.Open,
            CreatedAt = now,
        };
    }

    /// <summary>开始执行</summary>
    public bool Start(string assignedTo)
    {
        if (Status != MaintenanceOrderStatus.Open)
            return false;

        ArgumentException.ThrowIfNullOrWhiteSpace(assignedTo);
        AssignedTo = assignedTo.Trim();
        Status = MaintenanceOrderStatus.InProgress;
        return true;
    }

    /// <summary>完成工单</summary>
    public bool Complete(string completedBy, string remarks)
    {
        if (Status is MaintenanceOrderStatus.Completed or MaintenanceOrderStatus.Cancelled)
            return false;

        ArgumentException.ThrowIfNullOrWhiteSpace(completedBy);
        CompletedBy = completedBy.Trim();
        CompletionRemarks = remarks.Trim();
        CompletedAt = DateTimeOffset.UtcNow;
        Status = MaintenanceOrderStatus.Completed;
        return true;
    }

    /// <summary>取消工单</summary>
    public bool Cancel(string reason)
    {
        if (Status is MaintenanceOrderStatus.Completed or MaintenanceOrderStatus.Cancelled)
            return false;

        CompletionRemarks = reason.Trim();
        CompletedAt = DateTimeOffset.UtcNow;
        Status = MaintenanceOrderStatus.Cancelled;
        return true;
    }
}
