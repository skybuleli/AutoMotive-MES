using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.SupplierQuality;

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record SupplierResponse(
    string Id,
    string SupplierCode,
    string SupplierName,
    string? ShortName,
    string? ContactPerson,
    string? ContactPhone,
    string? ContactEmail,
    string MaterialCategory,
    string MaterialCodes,
    string Tier,
    bool IsCritical,
    double LatestScore,
    DateTimeOffset? LatestScoreAt,
    string? IsoCertification,
    DateTimeOffset? IsoExpiryDate,
    bool IsActive,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record SupplierScoreCardResponse(
    string Id,
    string SupplierId,
    string SupplierCode,
    string Period,
    double IncomingQualityScore,
    string IncomingQualityData,
    double OnTimeDeliveryScore,
    string OnTimeDeliveryData,
    double EightDResponseScore,
    string EightDResponseData,
    double PpapPassRateScore,
    string PpapPassRateData,
    double PriceCompetitivenessScore,
    string PriceCompetitivenessData,
    double WeightedTotal,
    string EvaluatedBy,
    string? Remarks,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record PpapDocumentResponse(
    string Id,
    string SupplierId,
    string SupplierCode,
    string MaterialCode,
    string MaterialName,
    int PpapLevel,
    string Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiryDate,
    string Version,
    string? ApprovedBy,
    string? RejectionReason,
    string? Remarks,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record CriticalSupplierSettingResponse(
    string Id,
    string MaterialCode,
    string MaterialName,
    int ControlLevel,
    bool RequiresFullInspection,
    bool RequiresOnSiteAudit,
    int AuditIntervalMonths,
    bool RequiresSpcDataSubmission,
    bool RequiresComplianceReport,
    bool IsActive,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateSupplierRequest
{
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string MaterialCategory { get; set; } = string.Empty;
    public string MaterialCodes { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public string? IsoCertification { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class UpdateSupplierRequest
{
    public string? ShortName { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? MaterialCategory { get; set; }
    public string? MaterialCodes { get; set; }
    public bool? IsCritical { get; set; }
    public string? IsoCertification { get; set; }
    public DateTimeOffset? IsoExpiryDate { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class CreateScoreCardRequest
{
    public string SupplierId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public double IncomingQualityScore { get; set; }
    public string IncomingQualityData { get; set; } = string.Empty;
    public double OnTimeDeliveryScore { get; set; }
    public string OnTimeDeliveryData { get; set; } = string.Empty;
    public double EightDResponseScore { get; set; }
    public string EightDResponseData { get; set; } = string.Empty;
    public double PpapPassRateScore { get; set; }
    public string PpapPassRateData { get; set; } = string.Empty;
    public double PriceCompetitivenessScore { get; set; }
    public string PriceCompetitivenessData { get; set; } = string.Empty;
    public string EvaluatedBy { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class CreatePpapDocumentRequest
{
    public string SupplierId { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public int PpapLevel { get; set; } = 3;
    public DateTimeOffset? ExpiryDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class SubmitPpapRequest
{
    // PPAP submit is a route action with no body needed
}

[MemoryPackable]
public partial class ApprovePpapRequest
{
    public string ApprovedBy { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RejectPpapRequest
{
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CreateCriticalSettingRequest
{
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public int ControlLevel { get; set; } = 3;
    public bool RequiresFullInspection { get; set; } = true;
    public bool RequiresOnSiteAudit { get; set; } = true;
    public int AuditIntervalMonths { get; set; } = 6;
    public bool RequiresSpcDataSubmission { get; set; } = true;
    public bool RequiresComplianceReport { get; set; } = true;
    public string? Remarks { get; set; }
}

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class SupplierMapper
{
    public static SupplierResponse ToResponse(Supplier s) => new(
        s.Id.ToString(),
        s.SupplierCode,
        s.SupplierName,
        s.ShortName,
        s.ContactPerson,
        s.ContactPhone,
        s.ContactEmail,
        s.MaterialCategory,
        s.MaterialCodes,
        s.Tier.ToString(),
        s.IsCritical,
        s.LatestScore,
        s.LatestScoreAt,
        s.IsoCertification,
        s.IsoExpiryDate,
        s.IsActive,
        s.Remarks,
        s.CreatedAt,
        s.UpdatedAt);

    public static SupplierScoreCardResponse ToScoreCardResponse(SupplierScoreCard c) => new(
        c.Id.ToString(),
        c.SupplierId.ToString(),
        c.SupplierCode,
        c.Period,
        c.IncomingQualityScore,
        c.IncomingQualityData,
        c.OnTimeDeliveryScore,
        c.OnTimeDeliveryData,
        c.EightDResponseScore,
        c.EightDResponseData,
        c.PpapPassRateScore,
        c.PpapPassRateData,
        c.PriceCompetitivenessScore,
        c.PriceCompetitivenessData,
        c.WeightedTotal,
        c.EvaluatedBy,
        c.Remarks,
        c.CreatedAt);

    public static PpapDocumentResponse ToPpapResponse(PpapDocument d) => new(
        d.Id.ToString(),
        d.SupplierId.ToString(),
        d.SupplierCode,
        d.MaterialCode,
        d.MaterialName,
        d.PpapLevel,
        d.Status.ToString(),
        d.SubmittedAt,
        d.ApprovedAt,
        d.ExpiryDate,
        d.Version,
        d.ApprovedBy,
        d.RejectionReason,
        d.Remarks,
        d.CreatedBy,
        d.CreatedAt,
        d.UpdatedAt);

    public static CriticalSupplierSettingResponse ToCriticalResponse(CriticalSupplierSetting s) => new(
        s.Id.ToString(),
        s.MaterialCode,
        s.MaterialName,
        s.ControlLevel,
        s.RequiresFullInspection,
        s.RequiresOnSiteAudit,
        s.AuditIntervalMonths,
        s.RequiresSpcDataSubmission,
        s.RequiresComplianceReport,
        s.IsActive,
        s.Remarks,
        s.CreatedAt,
        s.UpdatedAt);
}
