using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Scheduling;

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record ScheduleResponse(
    string Id,
    string OrderId,
    string OrderNumber,
    string ProductCode,
    int PlannedQuantity,
    string EquipmentCode,
    int Station,
    string Shift,
    string ScheduleDate,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    double StandardMinutes,
    double ChangeoverMinutes,
    string Status,
    string RushType,
    string? RushReason,
    short Priority,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record CapacityCalendarResponse(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    int Station,
    double StandardChangeoverMinutes,
    double CrossProductChangeoverMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record ScheduleConflictResponse(
    string Description,
    string Severity,
    string EquipmentCode,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? ConflictingScheduleId);

[MemoryPackable]
public partial record CapacityUtilizationResponse(
    string EquipmentCode,
    string EquipmentName,
    double UtilizationPercent,
    int ScheduledCount);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateScheduleRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string EquipmentCode { get; set; } = string.Empty;
    public string Shift { get; set; } = "Morning";
    public string ScheduleDate { get; set; } = string.Empty;
    public DateTimeOffset PlannedStartAt { get; set; }
    public double StandardMinutes { get; set; }
    public double ChangeoverMinutes { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class InsertRushOrderRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string EquipmentCode { get; set; } = string.Empty;
    public string RushType { get; set; } = "OemUrgent";
    public string? RushReason { get; set; }
    public double StandardMinutes { get; set; }
    public double ChangeoverMinutes { get; set; }
}

[MemoryPackable]
public partial class RescheduleRequest
{
    public DateTimeOffset NewStartAt { get; set; }
    public string? NewEquipmentCode { get; set; }
    public double? NewChangeoverMinutes { get; set; }
}

[MemoryPackable]
public partial class CreateCalendarRequest
{
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public int Station { get; set; }
    public double StandardChangeoverMinutes { get; set; } = 15;
    public double CrossProductChangeoverMinutes { get; set; } = 45;
}

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class SchedulingMapper
{
    public static ScheduleResponse ToScheduleResponse(ProductionSchedule s) => new(
        s.Id.ToString(),
        s.OrderId.ToString(),
        s.OrderNumber,
        s.ProductCode,
        s.PlannedQuantity,
        s.EquipmentCode,
        s.Station,
        s.Shift.ToString(),
        s.ScheduleDate.ToString("yyyy-MM-dd"),
        s.PlannedStartAt,
        s.PlannedEndAt,
        s.StandardMinutes,
        s.ChangeoverMinutes,
        s.Status.ToString(),
        s.RushType.ToString(),
        s.RushReason,
        s.Priority,
        s.Remarks,
        s.CreatedAt,
        s.UpdatedAt);

    public static CapacityCalendarResponse ToCalendarResponse(CapacityCalendar c) => new(
        c.Id.ToString(),
        c.EquipmentCode,
        c.EquipmentName,
        c.Station,
        c.StandardChangeoverMinutes,
        c.CrossProductChangeoverMinutes,
        c.IsActive,
        c.CreatedAt);

    public static ScheduleConflictResponse ToConflictResponse(ScheduleConflict c) => new(
        c.Description,
        c.Severity,
        c.EquipmentCode,
        c.StartAt,
        c.EndAt,
        c.ConflictingScheduleId);
}
