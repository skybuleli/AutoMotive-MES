using MemoryPack;

namespace MesAdmin.Application.Features.Reports;

/// <summary>报表类型标识</summary>
public enum ReportType
{
    /// <summary>质量日报/周报/月报</summary>
    Quality,
    /// <summary>OEE 日报</summary>
    OeeDaily,
    /// <summary>生产产量日报</summary>
    ProductionDaily,
    /// <summary>维护工单周报</summary>
    MaintenanceWeekly,
    /// <summary>综合管理月报</summary>
    Monthly,
}

/// <summary>报表模板定义</summary>
public sealed record ReportTemplate(
    string Id,                            // 模板唯一标识，如 "oee-daily"
    string Name,                          // 显示名称，如 "OEE 日报"
    string Description,                   // 描述
    ReportType Type,                      // 报表类型
    List<ReportSectionDef> Sections,      // 章节定义（顺序排列）
    bool SupportsEmail = false,           // 是否支持邮件推送
    bool SupportsSchedule = false         // 是否支持定时自动生成
);

/// <summary>报表章节定义</summary>
public sealed record ReportSectionDef(
    string Id,                            // 章节标识，如 "yield-summary"
    string Title,                         // 章节标题，如 "产量概览"
    SectionLayout Layout,                 // 布局类型
    string? DataSourceKey = null          // 数据源键，如 "production_summary"
);

/// <summary>章节布局类型</summary>
public enum SectionLayout
{
    /// <summary>KPI 卡片行（数值型指标，2-5 列）</summary>
    KpiRow,
    /// <summary>数据表格</summary>
    Table,
    /// <summary>图表占位（预留 ECharts 集成）</summary>
    Chart,
    /// <summary>文本段落</summary>
    Text,
}

/// <summary>KPI 卡片数据</summary>
[MemoryPackable]
public sealed partial record KpiCard(
    string Label,
    string Value,
    string Unit,
    string Color,        // QuestPDF 颜色常量，如 Colors.Green.Medium
    string? Trend = null // ↑/↓/→ 趋势箭头
);

/// <summary>报表表格列定义</summary>
public sealed record TableColumnDef(
    string Header,
    string DataKey,      // 数据行中的键
    double WidthRatio,   // 列宽比例
    bool IsBold = false,
    string? ColorKey = null  // 数据行中用于着色字段的键
);

/// <summary>报表表格数据行</summary>
[MemoryPackable]
public sealed partial record TableRow(
    Dictionary<string, string> Cells  // DataKey → 显示值
);

/// <summary>报表渲染数据（通用容器）</summary>
[MemoryPackable]
public sealed partial record ReportRenderData(
    ReportType Type,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    DateTimeOffset GeneratedAt,
    string Title,
    
    // ── KPI 卡片组 ──
    Dictionary<string, List<KpiCard>> KpiCards,  // sectionId → cards
    
    // ── 表格数据 ──
    Dictionary<string, List<TableRow>> Tables,    // sectionId → rows
    
    // ── 表格列定义 ──
    Dictionary<string, List<TableColumnDef>> TableColumns,  // sectionId → column defs
    
    // ── 文本段落 ──
    Dictionary<string, string> Texts              // sectionId → text content
);

/// <summary>OEE 日报特有聚合数据</summary>
[MemoryPackable]
public sealed partial record OeeDailyData(
    string EquipmentCode,
    string EquipmentName,
    double Availability,
    double Performance,
    double Quality,
    double Oee,
    string Grade,
    double RunningHours,
    double PlannedHours,
    double? Mtbf,        // Mean Time Between Failures (hours)
    double? Mttr,        // Mean Time To Repair (hours)
    int TotalStops,
    Dictionary<string, int> StopReasons  // 停机原因 → 分钟数
);

/// <summary>生产日报聚合数据</summary>
[MemoryPackable]
public sealed partial record ProductionDailyData(
    DateTimeOffset Date,
    int OrdersCompleted,
    int TotalProduced,
    int TotalQualified,
    int TotalDefective,
    double YieldRate,
    int ActiveOrders,
    Dictionary<string, int> ProductionByProduct  // 产品编码 → 数量
);

/// <summary>维护周报聚合数据</summary>
[MemoryPackable]
public sealed partial record MaintenanceWeeklyData(
    DateTimeOffset WeekStart,
    DateTimeOffset WeekEnd,
    int WorkOrdersCreated,
    int WorkOrdersCompleted,
    int OverdueOrders,
    double CompletionRate,
    int SparePartsUsed,
    Dictionary<string, int> MaintenanceByType  // 维护类型 → 数量
);

/// <summary>综合月报聚合数据</summary>
[MemoryPackable]
public sealed partial record MonthlySummaryData(
    int Year,
    int Month,
    int TotalOrders,
    int CompletedOrders,
    double CompletionRate,
    int TotalProduction,
    double YieldRate,
    double AvgOee,
    double? AvgCpk,
    int TotalNcrs,
    int Closed8Ds,
    double QualityCostPercent,  // 质量成本占比 (%)
    int MaintenanceOrders,
    int SparePartsCost           // 备件成本 (元)
);
