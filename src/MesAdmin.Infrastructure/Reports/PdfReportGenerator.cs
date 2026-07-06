using MesAdmin.Application.Features.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// 质量报表 PDF 生成器（T2.9）。
/// 使用 QuestPDF 生成专业格式的质量日报/周报/月报 PDF。
/// 包含：表头 + 产量KPI + 不良品分布表 + SPC Cpk 汇总 + 供应商质量（月报）+ 质量成本（月报）。
/// </summary>
public sealed class PdfReportGenerator
{
    static PdfReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>生成质量报表 PDF 字节数组</summary>
    public byte[] GenerateReportPdf(QualityReportData data)
    {
        var periodLabel = data.Period switch
        {
            ReportPeriod.Daily => $"日报 {data.StartDate:yyyy-MM-dd}",
            ReportPeriod.Weekly => $"周报 {data.StartDate:yyyy-MM-dd} 至 {data.EndDate:yyyy-MM-dd}",
            ReportPeriod.Monthly => $"月报 {data.StartDate:yyyy-MM}",
            _ => "质量报告"
        };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Inter Tight"));

                // ── 表头 ──
                page.Header().Element(h => BuildHeader(h, data, periodLabel));

                // ── 内容 ──
                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // 产量概览 KPI
                    col.Item().Element(e => BuildYieldSection(e, data));

                    // 不良品分布
                    if (data.DefectDistribution.Count > 0)
                        col.Item().Element(e => BuildDefectSection(e, data));

                    // SPC 能力汇总
                    if (data.SpcSummaries.Count > 0)
                        col.Item().Element(e => BuildSpcSection(e, data));

                    // 供应商质量（月度）
                    if (data.SupplierSummaries?.Count > 0)
                        col.Item().Element(e => BuildSupplierSection(e, data));

                    // 质量成本（月度）
                    if (data.QualityCosts is not null)
                        col.Item().Element(e => BuildCostSection(e, data));

                    // 8D 闭环
                    col.Item().Element(e => BuildEightDSection(e, data));
                });

                // ── 页脚 ──
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("AutoMES 博世 ESP 质量管理系统 · ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span($"生成时间 {data.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private static void BuildHeader(IContainer container, QualityReportData data, string periodLabel)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Row(row =>
            {
                row.RelativeItem().AlignLeft().Text("AutoMES 质量管理系统").FontSize(16).Bold();
                row.RelativeItem().AlignRight().Text(periodLabel).FontSize(14).Bold().FontColor(Colors.Blue.Medium);
            });
            col.Item().LineHorizontal(1).LineColor(Colors.Blue.Medium);
            col.Item().Text($"报告范围：{data.StartDate.ToLocalTime():yyyy-MM-dd HH:mm} — {data.EndDate.ToLocalTime():yyyy-MM-dd HH:mm}")
                .FontSize(9).FontColor(Colors.Grey.Medium);
        });
    }

    private static void BuildYieldSection(IContainer container, QualityReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("一、产量与合格率").FontSize(12).Bold();

            // KPI 卡片行
            col.Item().Row(row =>
            {
                KpiCell(row.RelativeItem(), "检验总数", data.TotalInspections.ToString("N0"), Colors.Blue.Darken1);
                KpiCell(row.RelativeItem(), "合格", data.PassedInspections.ToString("N0"), Colors.Green.Medium);
                KpiCell(row.RelativeItem(), "不合格", data.FailedInspections.ToString("N0"), Colors.Red.Medium);
                KpiCell(row.RelativeItem(), "一次合格率", $"{data.FirstPassYield:F1}%",
                    data.FirstPassYield >= 98 ? Colors.Green.Medium : Colors.Red.Medium);
                KpiCell(row.RelativeItem(), "NCR", data.TotalNcrs.ToString("N0"), Colors.Orange.Medium);
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Header(h =>
                {
                    h.Cell().Background(Colors.Blue.Darken2).Padding(3).Text("指标").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Blue.Darken2).Padding(3).Text("数值").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Blue.Darken2).Padding(3).Text("指标").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Blue.Darken2).Padding(3).Text("数值").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Blue.Darken2).Padding(3).Text("指标").FontColor(Colors.White).FontSize(9).Bold();
                });

                // 两行三列的数据格
                table.Cell().Padding(2).Text("检验总数").FontSize(8);
                table.Cell().Padding(2).Text(data.TotalInspections.ToString("N0")).FontSize(8).Bold();
                table.Cell().Padding(2).Text("合格数").FontSize(8);
                table.Cell().Padding(2).Text(data.PassedInspections.ToString("N0")).FontSize(8).Bold().FontColor(Colors.Green.Medium);
                table.Cell().Padding(2).Text("不合格数").FontSize(8);
                table.Cell().Padding(2).Text(data.FailedInspections.ToString("N0")).FontSize(8).Bold().FontColor(Colors.Red.Medium);
                table.Cell().Padding(2).Text("一次合格率").FontSize(8);
                table.Cell().Padding(2).Text($"{data.FirstPassYield:F1}%").FontSize(8).Bold();
                table.Cell().Padding(2).Text("未关闭 NCR").FontSize(8);
                table.Cell().Padding(2).Text(data.OpenNcrs.ToString("N0")).FontSize(8).Bold().FontColor(Colors.Orange.Medium);
                table.Cell().Padding(2).Text("").FontSize(8);
                table.Cell().Padding(2).Text("").FontSize(8);
            });
        });
    }

    private static void BuildDefectSection(IContainer container, QualityReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("二、不良品分布").FontSize(12).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                table.Header(h =>
                {
                    h.Cell().Background(Colors.Red.Darken2).Padding(3).Text("不良类型").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Red.Darken2).Padding(3).Text("数量").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Red.Darken2).Padding(3).Text("占比 %").FontColor(Colors.White).FontSize(9).Bold();
                });

                var total = data.DefectDistribution.Values.Sum();
                foreach (var (type, count) in data.DefectDistribution.OrderByDescending(kv => kv.Value))
                {
                    var pct = total > 0 ? (double)count / total * 100 : 0;
                    table.Cell().Padding(2).Text(type).FontSize(8);
                    table.Cell().Padding(2).Text(count.ToString("N0")).FontSize(8);
                    table.Cell().Padding(2).Text($"{pct:F1}%").FontSize(8);
                }
            });
        });
    }

    private static void BuildSpcSection(IContainer container, QualityReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("三、SPC 过程能力汇总").FontSize(12).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                table.Header(h =>
                {
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("特性").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("样本数").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("均值").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("Cpk").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("Ppk").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Purple.Darken2).Padding(3).Text("告警").FontColor(Colors.White).FontSize(9).Bold();
                });

                foreach (var spc in data.SpcSummaries.Values.OrderBy(s => s.CharacteristicCode))
                {
                    var cpkColor = (spc.Cpk ?? 0) switch
                    {
                        >= 1.33 => Colors.Green.Medium,
                        >= 1.0 => Colors.Orange.Medium,
                        _ => Colors.Red.Medium
                    };

                    table.Cell().Padding(2).Text($"{spc.CharacteristicCode} ({spc.CharacteristicName})").FontSize(7);
                    table.Cell().Padding(2).Text(spc.SampleCount.ToString()).FontSize(8);
                    table.Cell().Padding(2).Text(spc.Mean.ToString("F4")).FontSize(8);
                    table.Cell().Padding(2).Text(spc.Cpk?.ToString("F4") ?? "N/A").FontSize(8).FontColor(cpkColor);
                    table.Cell().Padding(2).Text(spc.Ppk?.ToString("F4") ?? "N/A").FontSize(8);
                    table.Cell().Padding(2).Text(spc.UnacknowledgedAlerts > 0
                        ? $"{spc.UnacknowledgedAlerts} ⚠"
                        : "0").FontSize(8).FontColor(spc.UnacknowledgedAlerts > 0 ? Colors.Red.Medium : Colors.Grey.Medium);
                }
            });

            col.Item().Text("注：Cpk ≥ 1.33 绿色（能力充分），Cpk ≥ 1.0 黄色（能力一般），Cpk < 1.0 红色（能力不足）")
                .FontSize(8).FontColor(Colors.Grey.Medium).Italic();
        });
    }

    private static void BuildSupplierSection(IContainer container, QualityReportData data)
    {
        if (data.SupplierSummaries is null || data.SupplierSummaries.Count == 0) return;

        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("四、供应商质量排名").FontSize(12).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                table.Header(h =>
                {
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("供应商").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("总批次").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("合格").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("不合格").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("合格率").FontColor(Colors.White).FontSize(9).Bold();
                    h.Cell().Background(Colors.Orange.Darken2).Padding(3).Text("8D 未关").FontColor(Colors.White).FontSize(9).Bold();
                });

                foreach (var sup in data.SupplierSummaries.Values.OrderBy(s => s.LotAcceptRate))
                {
                    var rateColor = sup.LotAcceptRate >= 99 ? Colors.Green.Medium :
                                    sup.LotAcceptRate >= 95 ? Colors.Orange.Medium : Colors.Red.Medium;

                    table.Cell().Padding(2).Text(sup.SupplierName).FontSize(8);
                    table.Cell().Padding(2).Text(sup.TotalLots.ToString()).FontSize(8);
                    table.Cell().Padding(2).Text(sup.AcceptedLots.ToString()).FontSize(8);
                    table.Cell().Padding(2).Text(sup.RejectedLots.ToString()).FontSize(8).FontColor(sup.RejectedLots > 0 ? Colors.Red.Medium : Colors.Grey.Medium);
                    table.Cell().Padding(2).Text($"{sup.LotAcceptRate:F1}%").FontSize(8).FontColor(rateColor);
                    table.Cell().Padding(2).Text(sup.Open8DCount.ToString()).FontSize(8);
                }
            });
        });
    }

    private static void BuildCostSection(IContainer container, QualityReportData data)
    {
        if (data.QualityCosts is null) return;

        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("五、质量成本（月报）").FontSize(12).Bold();
            col.Item().Row(row =>
            {
                CostCell(row.RelativeItem(), "报废成本", $"{data.QualityCosts.ScrapCost:N0} 元", Colors.Red.Medium);
                CostCell(row.RelativeItem(), "返工成本", $"{data.QualityCosts.ReworkCost:N0} 元", Colors.Orange.Medium);
                CostCell(row.RelativeItem(), "退货成本", $"{data.QualityCosts.ReturnCost:N0} 元", Colors.Blue.Medium);
                CostCell(row.RelativeItem(), "总质量成本", $"{data.QualityCosts.TotalCost:N0} 元", Colors.Purple.Medium);
                CostCell(row.RelativeItem(), "单位成本", $"{data.QualityCosts.CostPerUnit:N2} 元/件", Colors.Grey.Medium);
            });
        });
    }

    private static void BuildEightDSection(IContainer container, QualityReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("六、8D 闭环管理").FontSize(12).Bold();
            col.Item().Row(row =>
            {
                KpiCell(row.RelativeItem(), "8D 总数", data.TotalEightDReports.ToString("N0"), Colors.Blue.Medium);
                KpiCell(row.RelativeItem(), "已关闭", data.ClosedEightDReports.ToString("N0"), Colors.Green.Medium);
                KpiCell(row.RelativeItem(), "关闭率", $"{data.EightDClosureRate:F1}%",
                    data.EightDClosureRate >= 80 ? Colors.Green.Medium : Colors.Orange.Medium);
            });
        });
    }

    private static void KpiCell(IContainer container, string label, string value, string color)
    {
        container.Padding(3).Column(col =>
        {
            col.Spacing(2);
            col.Item().Background(Colors.Grey.Lighten4).Padding(4).Column(c2 =>
            {
                c2.Spacing(2);
                c2.Item().AlignCenter().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                c2.Item().AlignCenter().Text(value).FontSize(14).Bold().FontColor(color);
            });
        });
    }

    private static void CostCell(IContainer container, string label, string value, string color)
    {
        container.Padding(3).Column(col =>
        {
            col.Spacing(2);
            col.Item().Background(Colors.Grey.Lighten4).Padding(4).Column(c2 =>
            {
                c2.Spacing(2);
                c2.Item().AlignCenter().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                c2.Item().AlignCenter().Text(value).FontSize(11).Bold().FontColor(color);
            });
        });
    }
}
