using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Reports;

/// <summary>报表时间范围类型</summary>
public enum ReportPeriod
{
    Daily,
    Weekly,
    Monthly,
}

/// <summary>质量报表聚合数据（日报/周报/月报通用）</summary>
public sealed record QualityReportData(
    ReportPeriod Period,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    DateTimeOffset GeneratedAt,
    
    // ── 产量数据 ──
    int TotalInspections,
    int PassedInspections,
    int FailedInspections,
    double FirstPassYield,  // 一次合格率 (%)

    // ── 不良品分布 ──
    int TotalNcrs,
    int OpenNcrs,
    Dictionary<string, int> DefectDistribution,  // defect type → count

    // ── SPC 能力 ──
    Dictionary<string, SpcSummary> SpcSummaries,  // characteristic → Cpk/Ppk stats
    
    // ── 供应商质量（月度） ──
    Dictionary<string, SupplierQualitySummary>? SupplierSummaries,

    // ── 质量成本（月度） ──
    QualityCostSummary? QualityCosts,

    // ── 8D 状态 ──
    int TotalEightDReports,
    int ClosedEightDReports,
    double EightDClosureRate
);

/// <summary>SPC 特性汇总</summary>
public sealed record SpcSummary(
    string CharacteristicCode,
    string CharacteristicName,
    int SampleCount,
    double Mean,
    double StdDev,
    double? Cpk,
    double? Ppk,
    double? UpperControlLimit,
    double? LowerControlLimit,
    int AlertCount,
    int UnacknowledgedAlerts
);

/// <summary>供应商质量汇总（月度）</summary>
public sealed record SupplierQualitySummary(
    string SupplierCode,
    string SupplierName,
    int TotalLots,
    int AcceptedLots,
    int RejectedLots,
    double LotAcceptRate,   // 批次合格率 (%)
    int TotalDefects,
    int Open8DCount
);

/// <summary>质量成本汇总（月度）</summary>
public sealed record QualityCostSummary(
    decimal ScrapCost,
    decimal ReworkCost,
    decimal ReturnCost,
    decimal TotalCost,
    double CostPerUnit
);
