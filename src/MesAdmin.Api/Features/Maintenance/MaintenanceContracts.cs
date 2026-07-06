using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Maintenance;

/// <summary>
/// 预防性维护端点组（api/v1/maintenance）。
/// </summary>
public class MaintenanceGroup : Group
{
    public MaintenanceGroup() => Configure("api/v1/maintenance", ep => { });
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record MaintenancePlanResponse(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    string MaintenanceType,
    double ThresholdValue,
    string TaskDescription,
    string WorkContent,
    bool IsActive,
    DateTimeOffset? LastTriggeredAt,
    long? LastTriggeredCycleCount,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record MaintenanceOrderResponse(
    string Id,
    string OrderNumber,
    string MaintenancePlanId,
    string EquipmentCode,
    string EquipmentName,
    string MaintenanceType,
    string TriggerType,
    double TriggerValue,
    string Title,
    string Description,
    string Status,
    string? AssignedTo,
    string? CompletedBy,
    string? CompletionRemarks,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class StartOrderRequest
{
    public string AssignedTo { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CompleteOrderRequest
{
    public string CompletedBy { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CancelOrderRequest
{
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CreatePlanRequest
{
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string MaintenanceType { get; set; } = string.Empty;
    public double ThresholdValue { get; set; }
    public string TaskDescription { get; set; } = string.Empty;
    public string WorkContent { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class MaintenanceMapper
{
    public static MaintenancePlanResponse ToPlanResponse(MaintenancePlan plan) => new(
        plan.Id.ToString(),
        plan.EquipmentCode,
        plan.EquipmentName,
        plan.MaintenanceType.ToString(),
        plan.ThresholdValue,
        plan.TaskDescription,
        plan.WorkContent,
        plan.IsActive,
        plan.LastTriggeredAt,
        plan.LastTriggeredCycleCount,
        plan.CreatedAt);

    public static MaintenanceOrderResponse ToOrderResponse(MaintenanceWorkOrder order) => new(
        order.Id.ToString(),
        order.OrderNumber,
        order.MaintenancePlanId.ToString(),
        order.EquipmentCode,
        order.EquipmentName,
        order.MaintenanceType.ToString(),
        order.TriggerType.ToString(),
        order.TriggerValue,
        order.Title,
        order.Description,
        order.Status.ToString(),
        order.AssignedTo,
        order.CompletedBy,
        order.CompletionRemarks,
        order.CompletedAt,
        order.CreatedAt);
}
