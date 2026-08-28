using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Gauges;

/// <summary>
/// 计量器具端点组（api/v1/gauges，S01）。
/// </summary>
public class GaugeGroup : Group
{
    public GaugeGroup() => Configure("api/v1/gauges", ep => { });
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record GaugeResponse(
    string Id,
    string GaugeNumber,
    string Name,
    string Type,
    string RangeSpec,
    string ResolutionSpec,
    string AccuracyClass,
    int CalibrationCycleDays,
    DateTimeOffset? LastCalibratedAt,
    DateTimeOffset? NextDueAt,
    int DaysToDue,
    string Status,
    string? StorageLocation,
    string? Remarks,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record CalibrationRecordResponse(
    string Id,
    string GaugeId,
    DateTimeOffset CalibratedAt,
    string Result,
    string CertificateNo,
    string OperatorId,
    DateTimeOffset NextDueAfter,
    string? Remarks,
    DateTimeOffset CreatedAt);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateGaugeRequest
{
    public string GaugeNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string RangeSpec { get; set; } = string.Empty;
    public string ResolutionSpec { get; set; } = string.Empty;
    public string AccuracyClass { get; set; } = string.Empty;

    /// <summary>校准周期（天）</summary>
    public int CalibrationCycleDays { get; set; }

    /// <summary>最近一次校准时间（建账必填——台账内不允许存在无校准依据的器具）</summary>
    public DateTimeOffset LastCalibratedAt { get; set; }

    public string? StorageLocation { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class RecordCalibrationRequest
{
    /// <summary>校准日期</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>结论：Pass / Fail / Adjusted</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>校准证书编号</summary>
    public string CertificateNo { get; set; } = string.Empty;

    /// <summary>执行人（员工号）</summary>
    public string OperatorId { get; set; } = string.Empty;

    /// <summary>下次到期日（留空则按 校准日+周期 推算）</summary>
    public DateTimeOffset? NextDueAfter { get; set; }

    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class ScrapGaugeRequest
{
    /// <summary>报废原因（追加至备注留档）</summary>
    public string Reason { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  Mapper
// ═══════════════════════════════════════════

public static class GaugeMapper
{
    private static readonly string[] GaugeTypeNames =
        ["TorqueWrench", "Caliper", "PressureGauge", "Multimeter", "Other"];

    private static readonly string[] GaugeStatusNames = ["InService", "DueSoon", "Overdue", "Scrapped"];

    private static readonly string[] CalibrationResultNames = ["Pass", "Fail", "Adjusted"];

    public static GaugeResponse ToResponse(Gauge g)
    {
        var now = DateTimeOffset.UtcNow;
        var daysToDue = g.NextDueAt is { } due ? (int)(due - now).TotalDays : 0;
        return new GaugeResponse(
            g.Id.ToString(),
            g.GaugeNumber,
            g.Name,
            TypeName(g.Type),
            g.RangeSpec,
            g.ResolutionSpec,
            g.AccuracyClass,
            g.CalibrationCycleDays,
            g.LastCalibratedAt,
            g.NextDueAt,
            daysToDue,
            StatusName(g.Status),
            g.StorageLocation,
            g.Remarks,
            g.CreatedAt);
    }

    public static CalibrationRecordResponse ToRecordResponse(CalibrationRecord r) => new(
        r.Id.ToString(),
        r.GaugeId.ToString(),
        r.CalibratedAt,
        ResultName(r.Result),
        r.CertificateNo,
        r.OperatorId,
        r.NextDueAfter,
        r.Remarks,
        r.CreatedAt);

    private static string TypeName(GaugeType type)
        => (int)type < GaugeTypeNames.Length ? GaugeTypeNames[(int)type] : nameof(GaugeType.Other);

    private static string StatusName(GaugeStatus status)
        => (int)status < GaugeStatusNames.Length ? GaugeStatusNames[(int)status] : status.ToString();

    private static string ResultName(CalibrationResult result)
        => (int)result < CalibrationResultNames.Length ? CalibrationResultNames[(int)result] : result.ToString();
}
