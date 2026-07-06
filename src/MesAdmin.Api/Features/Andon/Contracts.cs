using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Andon;

/// <summary>
/// Andon 端点组（api/v1/andon）。
/// </summary>
public class AndonGroup : Group
{
    public AndonGroup() => Configure("api/v1/andon", ep => { });
}

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class AcknowledgeAndonRequest
{
    public string AcknowledgedBy { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ResolveAndonRequest
{
    public string ResolvedBy { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CloseAndonRequest
{
    public string CloseRemarks { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record AndonEventResponse(
    string Id,
    string EventNumber,
    string EquipmentCode,
    int Station,
    string AlarmType,
    string Severity,
    string Status,
    int EscalationLevel,
    string Description,
    double ProcessValue,
    string? ProcessTag,
    double? UpperLimit,
    double? LowerLimit,
    string? OrderId,
    string? NonConformanceReportId,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
    string? ResolvedBy,
    string? Resolution,
    DateTimeOffset? ResolvedAt,
    string? CloseRemarks,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record AndonStatsResponse(
    int ActiveCount,
    int EscalatedL2Count,
    int EscalatedL3Count,
    int TodayCount);

// ═══════════════════════════════════════════
//  Mapper
// ═══════════════════════════════════════════

public static class AndonMapper
{
    public static AndonEventResponse ToResponse(AndonEvent ev)
        => new(
            ev.Id.ToString(),
            ev.EventNumber,
            ev.EquipmentCode,
            ev.Station,
            ev.AlarmType.ToString(),
            ev.Severity.ToString(),
            ev.Status.ToString(),
            ev.EscalationLevel,
            ev.Description,
            ev.ProcessValue,
            ev.ProcessTag,
            ev.UpperLimit,
            ev.LowerLimit,
            ev.OrderId?.ToString(),
            ev.NonConformanceReportId?.ToString(),
            ev.AcknowledgedBy,
            ev.AcknowledgedAt,
            ev.ResolvedBy,
            ev.Resolution,
            ev.ResolvedAt,
            ev.CloseRemarks,
            ev.ClosedAt,
            ev.EscalatedAt,
            ev.OccurredAt,
            ev.CreatedAt);
}
