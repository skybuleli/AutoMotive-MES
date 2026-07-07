namespace MesAdmin.Application.Features.Reports;

/// <summary>
/// 预置报表模板注册表（T4.1）。
/// 所有报表模板在此集中定义，供 ReportEngine 使用。
/// </summary>
public static class ReportTemplates
{
    private static readonly Dictionary<string, ReportTemplate> _templates = new();

    static ReportTemplates()
    {
        RegisterDefaults();
    }

    /// <summary>注册所有默认模板</summary>
    private static void RegisterDefaults()
    {
        Register(OeeDailyTemplate);
        Register(ProductionDailyTemplate);
        Register(QualityReportTemplate);
        Register(MaintenanceWeeklyTemplate);
        Register(MonthlyTemplate);
    }

    /// <summary>注册一个模板</summary>
    public static void Register(ReportTemplate template)
    {
        _templates[template.Id] = template;
    }

    /// <summary>获取所有模板</summary>
    public static IReadOnlyList<ReportTemplate> GetAll() => _templates.Values.ToList();

    /// <summary>按 ID 获取模板</summary>
    public static ReportTemplate? Get(string id) =>
        _templates.TryGetValue(id, out var t) ? t : null;

    // ═══════════════════════════════════════════════════════════
    //  预置模板定义
    // ═══════════════════════════════════════════════════════════

    /// <summary>OEE 日报模板</summary>
    public static readonly ReportTemplate OeeDailyTemplate = new(
        Id: "oee-daily",
        Name: "OEE 日报",
        Description: "8 台设备 OEE 综合效率 + MTBF/MTTR + 停机原因柏拉图分析",
        Type: ReportType.OeeDaily,
        Sections:
        [
            new("oee-kpi", "OEE 概览", SectionLayout.KpiRow, "oee_kpi"),
            new("oee-table", "设备 OEE 明细", SectionLayout.Table, "oee_detail"),
            new("oee-stops", "停机原因分析", SectionLayout.Table, "stop_reasons"),
        ],
        SupportsEmail: true,
        SupportsSchedule: true
    );

    /// <summary>生产日报模板</summary>
    public static readonly ReportTemplate ProductionDailyTemplate = new(
        Id: "production-daily",
        Name: "生产日报",
        Description: "当日产量、合格率、工单完成情况",
        Type: ReportType.ProductionDaily,
        Sections:
        [
            new("prod-kpi", "产量概览", SectionLayout.KpiRow, "prod_kpi"),
            new("prod-by-product", "产品产量分布", SectionLayout.Table, "prod_by_product"),
        ],
        SupportsEmail: true,
        SupportsSchedule: true
    );

    /// <summary>质量报表模板（T2.9 增强版）</summary>
    public static readonly ReportTemplate QualityReportTemplate = new(
        Id: "quality",
        Name: "质量报告",
        Description: "产量合格率 + 不良品分布 + SPC Cpk + 8D 闭环",
        Type: ReportType.Quality,
        Sections:
        [
            new("yield-kpi", "产量与合格率", SectionLayout.KpiRow, "yield_kpi"),
            new("defect-table", "不良品分布", SectionLayout.Table, "defect_dist"),
            new("spc-table", "SPC 过程能力", SectionLayout.Table, "spc_summary"),
            new("eightd-kpi", "8D 闭环管理", SectionLayout.KpiRow, "eightd_kpi"),
        ],
        SupportsEmail: true,
        SupportsSchedule: true
    );

    /// <summary>维护周报模板</summary>
    public static readonly ReportTemplate MaintenanceWeeklyTemplate = new(
        Id: "maintenance-weekly",
        Name: "维护周报",
        Description: "维护工单完成率、备件消耗、维护类型分布",
        Type: ReportType.MaintenanceWeekly,
        Sections:
        [
            new("maint-kpi", "维护概览", SectionLayout.KpiRow, "maint_kpi"),
            new("maint-table", "维护类型分布", SectionLayout.Table, "maint_by_type"),
        ],
        SupportsEmail: false,
        SupportsSchedule: false
    );

    /// <summary>综合管理月报模板（T4.3）</summary>
    public static readonly ReportTemplate MonthlyTemplate = new(
        Id: "monthly",
        Name: "综合管理月报",
        Description: "Cpk 趋势 + 一次合格率 + PPM + 质量成本 + OEE 趋势 + 8D/维护/Andon 月度总览",
        Type: ReportType.Monthly,
        Sections:
        [
            new("monthly-kpi", "月度总览", SectionLayout.KpiRow, "monthly_kpi"),
            new("monthly-quality-trend", "质量趋势（合格率/Cpk/PPM）", SectionLayout.Table, "quality_trend"),
            new("monthly-spc", "SPC 能力汇总", SectionLayout.Table, "spc_summary"),
            new("monthly-oee", "OEE 月度趋势", SectionLayout.Table, "oee_trend"),
            new("monthly-quality-cost", "质量成本分析", SectionLayout.KpiRow, "quality_cost"),
            new("monthly-8d", "8D 闭环比", SectionLayout.Table, "eightd_summary"),
            new("monthly-andon", "Andon 月度统计", SectionLayout.Table, "andon_summary"),
        ],
        SupportsEmail: true,
        SupportsSchedule: true
    );
}
