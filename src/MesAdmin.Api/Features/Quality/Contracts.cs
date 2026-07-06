using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality;

// ═══════════════════════════════════════════
//  SPC 质量管理端点组
// ═══════════════════════════════════════════

public class QualityGroup : Group
{
    public QualityGroup() => Configure("api/v1/quality", ep => { });
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record QualityRecordResponse(
    string Id,
    string Stage,
    string? OrderId,
    string? OrderNumber,
    string ProductCode,
    string ProductName,
    string? BatchNumber,
    string? SupplierCode,
    string? SupplierName,
    string InspectionPlanName,
    string? AqlScheme,
    int SampleSize,
    int AcceptNumber,
    int RejectNumber,
    string InspectorId,
    string Verdict,
    List<MeasuredCharacteristicResponse> Characteristics,
    int DefectCount,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

[MemoryPackable]
public partial record MeasuredCharacteristicResponse(
    string CharacteristicCode,
    string CharacteristicName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    double? ActualValue,
    bool IsFailed);

[MemoryPackable]
public partial record InspectionPlanResponse(
    string Id,
    string PlanName,
    string Version,
    string? ProductCode,
    string Stage,
    string SamplingFrequency,
    int SampleSize,
    bool EnableSpcChart,
    int SpcSubgroupSize,
    bool IsEnabled,
    List<PlanCharacteristicResponse> Characteristics,
    DateTimeOffset EffectiveDate,
    DateTimeOffset? ExpirationDate);

[MemoryPackable]
public partial record PlanCharacteristicResponse(
    string CharacteristicCode,
    string CharacteristicName,
    string Type,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool IsCritical,
    bool EnableSpc,
    double? UpperControlLimit,
    double? LowerControlLimit,
    double? CenterLine,
    double? UpperRangeLimit,
    double? CenterRange);

[MemoryPackable]
public partial record SpcSampleResponse(
    string Id,
    string CharacteristicCode,
    string? OrderId,
    string? EquipmentCode,
    int SubgroupIndex,
    int SubgroupSize,
    List<double> Values,
    double Mean,
    double Range,
    double StdDev,
    string Source,
    DateTimeOffset CollectedAt);

[MemoryPackable]
public partial record SpcRuleAlertResponse(
    string Id,
    string CharacteristicCode,
    int RuleType,
    string AlertLevel,
    string? TriggerSubgroupId,
    string? OrderId,
    string? EquipmentCode,
    string Description,
    bool IsAcknowledged,
    string? AcknowledgedBy,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record NcrResponse(
    string Id,
    string NcrNumber,
    string? QualityRecordId,
    string? OrderId,
    string? OrderNumber,
    string ProductCode,
    string ProductName,
    string? BatchNumber,
    string DiscoveredAt,
    string Description,
    int DefectQuantity,
    string Severity,
    string Status,
    string Disposition,
    string DiscoveredBy,
    string? ReviewerId,
    string? ReviewComments,
    DateTimeOffset? DispositionDeadline,
    string? EightDReportId,
    string? CloseRemarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

[MemoryPackable]
public partial record EightDReportResponse(
    string Id,
    string ReportNumber,
    string? NonConformanceReportId,
    string? NcrNumber,
    string Title,
    string ProductCode,
    string ProductName,
    string Status,
    string? TeamLeader,
    string? TeamMembers,
    string? ProblemDescription,
    string? ContainmentAction,
    string? RootCause,
    string? CorrectiveAction,
    string? VerificationResult,
    string? PreventiveAction,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

[MemoryPackable]
public partial record SpcSampleResultResponse(
    SpcSampleResponse Sample,
    List<SpcRuleAlertResponse> Alerts);

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class QualityMapper
{
    public static QualityRecordResponse ToRecordResponse(QualityRecord r)
        => new(
            r.Id.ToString(),
            r.Stage.ToString(),
            r.OrderId?.ToString(),
            r.OrderNumber,
            r.ProductCode,
            r.ProductName,
            r.BatchNumber,
            r.SupplierCode,
            r.SupplierName,
            r.InspectionPlanName,
            r.AqlScheme,
            r.SampleSize,
            r.AcceptNumber,
            r.RejectNumber,
            r.InspectorId,
            r.Verdict.ToString(),
            r.Characteristics.Select(c => new MeasuredCharacteristicResponse(
                c.CharacteristicCode, c.CharacteristicName,
                c.StandardValue, c.UpperSpecLimit, c.LowerSpecLimit,
                c.Unit, c.ActualValue, c.IsFailed)).ToList(),
            r.DefectCount,
            r.Remarks,
            r.CreatedAt,
            r.CompletedAt);

    public static InspectionPlanResponse ToPlanResponse(InspectionPlan p)
        => new(
            p.Id.ToString(),
            p.PlanName,
            p.Version,
            p.ProductCode,
            p.Stage.ToString(),
            p.SamplingFrequency,
            p.SampleSize,
            p.EnableSpcChart,
            p.SpcSubgroupSize,
            p.IsEnabled,
            p.Characteristics.Select(c => new PlanCharacteristicResponse(
                c.CharacteristicCode, c.CharacteristicName,
                c.Type.ToString(), c.StandardValue,
                c.UpperSpecLimit, c.LowerSpecLimit, c.Unit,
                c.IsCritical, c.EnableSpc,
                c.UpperControlLimit, c.LowerControlLimit,
                c.CenterLine, c.UpperRangeLimit, c.CenterRange)).ToList(),
            p.EffectiveDate,
            p.ExpirationDate);

    public static SpcSampleResponse ToSampleResponse(SpcSample s)
        => new(
            s.Id.ToString(),
            s.CharacteristicCode,
            s.OrderId?.ToString(),
            s.EquipmentCode,
            s.SubgroupIndex,
            s.SubgroupSize,
            s.Values,
            s.Mean,
            s.Range,
            s.StdDev,
            s.Source,
            s.CollectedAt);

    public static SpcRuleAlertResponse ToRuleAlertResponse(SpcRuleAlert a)
        => new(
            a.Id.ToString(),
            a.CharacteristicCode,
            (int)a.RuleType,
            a.AlertLevel.ToString(),
            a.TriggerSubgroupId?.ToString(),
            a.OrderId?.ToString(),
            a.EquipmentCode,
            a.Description,
            a.IsAcknowledged,
            a.AcknowledgedBy,
            a.CreatedAt);

    public static NcrResponse ToNcrResponse(NonConformanceReport n)
        => new(
            n.Id.ToString(),
            n.NcrNumber,
            n.QualityRecordId?.ToString(),
            n.OrderId?.ToString(),
            n.OrderNumber,
            n.ProductCode,
            n.ProductName,
            n.BatchNumber,
            n.DiscoveredAt.ToString(),
            n.Description,
            n.DefectQuantity,
            n.Severity.ToString(),
            n.Status.ToString(),
            n.Disposition.ToString(),
            n.DiscoveredBy,
            n.ReviewerId,
            n.ReviewComments,
            n.DispositionDeadline,
            n.EightDReportId?.ToString(),
            n.CloseRemarks,
            n.CreatedAt,
            n.ClosedAt);

    public static EightDReportResponse ToEightDResponse(EightDReport r)
        => new(
            r.Id.ToString(),
            r.ReportNumber,
            r.NonConformanceReportId?.ToString(),
            r.NcrNumber,
            r.Title,
            r.ProductCode,
            r.ProductName,
            r.Status.ToString(),
            r.TeamLeader,
            r.TeamMembers,
            r.ProblemDescription,
            r.ContainmentAction,
            r.RootCause,
            r.CorrectiveAction,
            r.VerificationResult,
            r.PreventiveAction,
            r.Summary,
            r.CreatedAt,
            r.ClosedAt);
}
