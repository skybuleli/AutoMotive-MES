using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 班次类型（早/中/晚）。
/// </summary>
public enum ShiftType
{
    Morning = 0,   // 06:00-14:00
    Afternoon = 1, // 14:00-22:00
    Night = 2,     // 22:00-06:00
}

/// <summary>
/// 排程状态。
/// </summary>
public enum ScheduleStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// 排程插单类型。
/// </summary>
public enum RushOrderType
{
    None = 0,
    /// <summary>OEM 紧急插单（优先级最高）</summary>
    OemUrgent = 1,
    /// <summary>质量返工插单</summary>
    QualityRework = 2,
    /// <summary>设备维护插单</summary>
    Maintenance = 3,
}

/// <summary>
/// 生产排程（M09 T3.10）。
/// 将生产工单分配到具体设备 × 班次 × 时间段。
/// </summary>
[MemoryPackable]
public partial class ProductionSchedule
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>产品编码</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>计划数量</summary>
    public int PlannedQuantity { get; set; }

    /// <summary>分配设备编码</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>工站编号</summary>
    public int Station { get; set; }

    /// <summary>班次</summary>
    public ShiftType Shift { get; set; } = ShiftType.Morning;

    /// <summary>排程日期</summary>
    public DateOnly ScheduleDate { get; set; }

    /// <summary>计划开始时间</summary>
    public DateTimeOffset PlannedStartAt { get; set; }

    /// <summary>计划结束时间</summary>
    public DateTimeOffset PlannedEndAt { get; set; }

    /// <summary>标准工时（分钟）</summary>
    public double StandardMinutes { get; set; }

    /// <summary>换型时间（分钟）</summary>
    public double ChangeoverMinutes { get; set; }

    /// <summary>排程状态</summary>
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;

    /// <summary>插单类型</summary>
    public RushOrderType RushType { get; set; } = RushOrderType.None;

    /// <summary>插单原因</summary>
    public string? RushReason { get; set; }

    /// <summary>优先级（1=正常, 2=紧急, 3=最高）</summary>
    public short Priority { get; set; } = 1;

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static ProductionSchedule Create(
        Ulid id,
        Ulid orderId,
        string orderNumber,
        string productCode,
        int plannedQuantity,
        string equipmentCode,
        int station,
        ShiftType shift,
        DateOnly scheduleDate,
        DateTimeOffset plannedStartAt,
        double standardMinutes,
        double changeoverMinutes,
        short priority = 1,
        RushOrderType rushType = RushOrderType.None,
        string? rushReason = null)
    {
        var plannedEndAt = plannedStartAt.AddMinutes(standardMinutes + changeoverMinutes);

        return new ProductionSchedule
        {
            Id = id,
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            ProductCode = productCode.Trim(),
            PlannedQuantity = plannedQuantity,
            EquipmentCode = equipmentCode.Trim(),
            Station = station,
            Shift = shift,
            ScheduleDate = scheduleDate,
            PlannedStartAt = plannedStartAt,
            PlannedEndAt = plannedEndAt,
            StandardMinutes = standardMinutes,
            ChangeoverMinutes = changeoverMinutes,
            Priority = priority,
            RushType = rushType,
            RushReason = rushReason?.Trim(),
            Status = ScheduleStatus.Scheduled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>开始执行</summary>
    public void Start()
    {
        if (Status != ScheduleStatus.Scheduled)
            throw new InvalidOperationException($"排程状态为 {Status}，不能开始");
        Status = ScheduleStatus.InProgress;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>完成排程</summary>
    public void Complete()
    {
        if (Status != ScheduleStatus.InProgress)
            throw new InvalidOperationException($"排程状态为 {Status}，不能完成");
        Status = ScheduleStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>取消排程</summary>
    public void Cancel(string? reason = null)
    {
        if (Status is ScheduleStatus.Completed or ScheduleStatus.Cancelled)
            throw new InvalidOperationException($"排程状态为 {Status}，不能取消");
        Status = ScheduleStatus.Cancelled;
        Remarks = reason?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>重新排程（调整时间/设备）</summary>
    public void Reschedule(DateTimeOffset newStart, string? newEquipmentCode = null, double? newChangeoverMinutes = null)
    {
        if (Status == ScheduleStatus.Cancelled)
            throw new InvalidOperationException("已取消的排程不能重新排程");

        PlannedStartAt = newStart;
        if (newEquipmentCode is not null)
            EquipmentCode = newEquipmentCode.Trim();
        if (newChangeoverMinutes.HasValue)
            ChangeoverMinutes = newChangeoverMinutes.Value;
        PlannedEndAt = PlannedStartAt.AddMinutes(StandardMinutes + ChangeoverMinutes);
        Status = ScheduleStatus.Scheduled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// 产能日历（设备可用性配置，T3.10）。
/// 定义每台设备的班次模板和维护停机时间。
/// </summary>
[MemoryPackable]
public partial class CapacityCalendar
{
    public Ulid Id { get; set; }

    /// <summary>设备编码</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>设备名称</summary>
    public string EquipmentName { get; set; } = string.Empty;

    /// <summary>工站编号</summary>
    public int Station { get; set; }

    /// <summary>班次模板（JSON: {Morning: 06:00-14:00, Afternoon: 14:00-22:00, Night: 22:00-06:00}）</summary>
    public string ShiftTemplate { get; set; } = "{\"Morning\":\"06:00-14:00\",\"Afternoon\":\"14:00-22:00\",\"Night\":\"22:00-06:00\"}";

    /// <summary>标准换型时间（分钟，同产品换型）</summary>
    public double StandardChangeoverMinutes { get; set; } = 15;

    /// <summary>跨产品换型时间（分钟，不同产品切换）</summary>
    public double CrossProductChangeoverMinutes { get; set; } = 45;

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static CapacityCalendar Create(
        Ulid id,
        string equipmentCode,
        string equipmentName,
        int station,
        double standardChangeover = 15,
        double crossProductChangeover = 45)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentName);

        var now = DateTimeOffset.UtcNow;
        return new CapacityCalendar
        {
            Id = id,
            EquipmentCode = equipmentCode.Trim(),
            EquipmentName = equipmentName.Trim(),
            Station = station,
            StandardChangeoverMinutes = standardChangeover,
            CrossProductChangeoverMinutes = crossProductChangeover,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}

/// <summary>
/// 排程冲突报告（T3.11）。
/// </summary>
[MemoryPackable]
public partial class ScheduleConflict
{
    /// <summary>冲突描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>冲突等级（Warning/Error）</summary>
    public string Severity { get; set; } = "Error";

    /// <summary>涉及设备编码</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>冲突时间段开始</summary>
    public DateTimeOffset StartAt { get; set; }

    /// <summary>冲突时间段结束</summary>
    public DateTimeOffset EndAt { get; set; }

    /// <summary>冲突的排程 Id（如有）</summary>
    public string? ConflictingScheduleId { get; set; }
}
