using MesAdmin.Application.Features.Reports;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Helpers;

namespace MesAdmin.Infrastructure.Reports;

/// <summary>
/// 报表数据源服务（T4.1 + T4.2）。
/// 从各个仓储聚合数据，构造通用 ReportRenderData 供 PdfReportGenerator 使用。
/// T4.2: OEE 日报使用 OeeReportStore + AndonEventRepository + ProductionOrderRepository 聚合。
/// </summary>
public sealed class ReportDataSourceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OeeReportStore _oeeStore;

    public ReportDataSourceService(IServiceScopeFactory scopeFactory, OeeReportStore oeeStore)
    {
        _scopeFactory = scopeFactory;
        _oeeStore = oeeStore;
    }

    /// <summary>按报表类型和模板聚合数据</summary>
    public async Task<ReportRenderData> AggregateAsync(
        ReportTemplate template,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct = default)
    {
        return template.Type switch
        {
            ReportType.OeeDaily => await AggregateOeeDailyAsync(start, end, ct),
            ReportType.ProductionDaily => await AggregateProductionDailyAsync(start, end, ct),
            ReportType.Quality => await AggregateQualityAsync(start, end, ct),
            ReportType.MaintenanceWeekly => await AggregateMaintenanceWeeklyAsync(start, end, ct),
            ReportType.Monthly => await AggregateMonthlyAsync(start, end, ct),
            _ => throw new ArgumentException($"Unknown report type: {template.Type}")
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  OEE 日报聚合（T4.2）
    //  ═══════════════════════════════════════════════════════════
    //  数据来源：
    //  1. OeeReportStore — 实时 OEE 数据（从 PLC 管道订阅）
    //  2. AndonEventRepository — 停机原因（柏拉图分析）
    //  3. IProductionOrderRepository — 产量/合格率
    //  4. MaintenanceWorkOrderRepository — MTBF/MTTR 计算
    // ═══════════════════════════════════════════════════════════

    private async Task<ReportRenderData> AggregateOeeDailyAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var andonRepo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var maintOrderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var allEquipment = Equipment.DefaultEquipment;
        var now = DateTimeOffset.UtcNow;

        // ── 1. 从 OeeReportStore 获取实时 OEE 快照 ──
        var snapshots = _oeeStore.GetAllSnapshots();
        var snapDict = snapshots.ToDictionary(s => s.EquipmentCode);

        var hasData = snapshots.Any(s => s.TotalUpdates > 0);
        var avgOee = hasData ? snapshots.Where(s => s.TotalUpdates > 0).Average(s => s.Oee) : 0.0;
        var avgAvail = hasData ? snapshots.Where(s => s.TotalUpdates > 0).Average(s => s.Availability) : 0.0;
        var avgPerf = hasData ? snapshots.Where(s => s.TotalUpdates > 0).Average(s => s.Performance) : 0.0;
        var avgQual = hasData ? snapshots.Where(s => s.TotalUpdates > 0).Average(s => s.Quality) : 0.0;
        var targetMet = hasData ? snapshots.Count(s => s.TotalUpdates > 0 && s.Oee >= 0.85) : 0;
        var totalEquip = allEquipment.Count;

        // ── 2. 从 Andon 事件获取停机原因统计 ──
        var allAndonEvents = await andonRepo.GetListAsync(limit: 200, ct: ct);
        var periodAndons = allAndonEvents
            .Where(a => a.CreatedAt >= start && a.CreatedAt <= end)
            .ToList();

        var stopReasonGroups = periodAndons
            .GroupBy(a => a.AlarmType)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // ── 3. 从工单数据获取产量统计 ──
        var allOrders = await orderRepo.GetAllAsync(ct);
        var periodOrders = allOrders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end).ToList();
        var totalQualified = periodOrders.Sum(o => o.QualifiedQuantity);
        var totalDefective = periodOrders.Sum(o => o.DefectiveQuantity);
        var totalProduced = totalQualified + totalDefective;
        var yieldRate = totalProduced > 0 ? (double)totalQualified / totalProduced * 100 : 100.0;

        // ── 4. 维护工单统计（用于 MTBF/MTTR 计算参考）──
        var allMaintOrders = await maintOrderRepo.GetListAsync(limit: 200, ct: ct);
        var periodMaint = allMaintOrders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end).ToList();
        var maintCompleted = periodMaint.Count(o => o.Status == MaintenanceOrderStatus.Completed);

        // ── 5. 构建 KPI 卡片 ──
        var kpi = new List<KpiCard>
        {
            new("综合 OEE", hasData ? $"{avgOee * 100:F1}" : "—", "%",
                avgOee >= 0.85 ? Colors.Green.Medium : avgOee >= 0.70 ? Colors.Orange.Medium : Colors.Red.Medium,
                hasData ? (avgOee >= 0.85 ? "↑" : "↓") : null),
            new("可用率", hasData ? $"{avgAvail * 100:F1}" : "—", "%", Colors.Green.Medium),
            new("性能率", hasData ? $"{avgPerf * 100:F1}" : "—", "%", Colors.Blue.Medium),
            new("良品率", hasData ? $"{avgQual * 100:F1}" : "—", "%", Colors.Purple.Medium),
            new("达标设备", hasData ? $"{targetMet}/{totalEquip}" : "—", "台",
                targetMet >= 6 ? Colors.Green.Medium : Colors.Orange.Medium),
        };

        // ── 6. 构建设备 OEE 明细表 ──
        var oeeColumns = new List<TableColumnDef>
        {
            new("设备编码", "code", 1.5),
            new("设备名称", "name", 2, true),
            new("OEE", "oee", 1, true, "oee_color"),
            new("可用率", "avail", 1),
            new("性能率", "perf", 1),
            new("良品率", "quality", 1),
            new("等级", "grade", 1),
        };

        var oeeRows = new List<TableRow>();
        foreach (var eq in allEquipment)
        {
            var snap = snapDict.GetValueOrDefault(eq.EquipmentCode);
            var oeePct = snap?.Oee ?? 0;
            var hasDeviceData = snap?.TotalUpdates > 0;

            oeeRows.Add(new TableRow(new Dictionary<string, string>
            {
                ["code"] = eq.EquipmentCode,
                ["name"] = eq.Name,
                ["oee"] = hasDeviceData ? $"{oeePct * 100:F1}%" : "—",
                ["oee_color"] = oeePct >= 0.85 ? Colors.Green.Medium :
                               oeePct >= 0.70 ? Colors.Orange.Medium : Colors.Red.Medium,
                ["avail"] = hasDeviceData ? $"{snap!.Availability * 100:F1}%" : "—",
                ["perf"] = hasDeviceData ? $"{snap!.Performance * 100:F1}%" : "—",
                ["quality"] = hasDeviceData ? $"{snap!.Quality * 100:F1}%" : "—",
                ["grade"] = oeePct >= 0.85 ? "S" : oeePct >= 0.70 ? "A" : hasDeviceData ? "B" : "—",
            }));
        }

        // ── 7. 构建停机原因柏拉图（按次数降序排列）──
        var stopColumns = new List<TableColumnDef>
        {
            new("停机原因", "reason", 3),
            new("发生次数", "count", 1, true),
            new("占比 %", "pct", 1),
        };

        var totalStops = stopReasonGroups.Values.Sum();
        var stopRows = stopReasonGroups
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                var pct = totalStops > 0 ? (double)kv.Value / totalStops * 100 : 0;
                return new TableRow(new Dictionary<string, string>
                {
                    ["reason"] = kv.Key switch
                    {
                        "TorqueExceeded" => "扭矩超差",
                        "LeakRateHigh" => "泄漏超标",
                        "FlashFailed" => "刷写失败",
                        "CanCommunicationError" => "CAN 通信异常",
                        "EquipmentAlarm" => "设备报警",
                        "ProcessDeviation" => "过程偏差",
                        "MaterialDefect" => "物料缺陷",
                        _ => kv.Key
                    },
                    ["count"] = kv.Value.ToString(),
                    ["pct"] = $"{pct:F1}%",
                });
            })
            .ToList();

        // ── 8. 生产概览文字段（底部注释）──
        var productionText = $"本日产量：合格 {totalQualified:N0} 件，不良 {totalDefective:N0} 件，合格率 {yieldRate:F1}%。"
            + $"工单 {periodOrders.Count} 单，Andon 报警 {periodAndons.Count} 次，"
            + $"维护工单 {periodMaint.Count} 单（完成 {maintCompleted} 单）。"
            + (hasData
                ? $"实时 OEE 数据基于 {snapshots.Sum(s => s.TotalUpdates)} 次 PLC 采样。"
                : "注：OEE 数据需 PLC 连接后自动采集，当前显示占位数据。");

        return new ReportRenderData(
            ReportType.OeeDaily, start, end, now,
            $"OEE 日报 {start:yyyy-MM-dd}",
            new() { ["oee_kpi"] = kpi },
            new()
            {
                ["oee_detail"] = oeeRows,
                ["stop_reasons"] = stopRows,
            },
            new()
            {
                ["oee_detail"] = oeeColumns,
                ["stop_reasons"] = stopColumns,
            },
            new() { ["production_note"] = productionText }
        );
    }

    // ═══════════════════════════════════════════════════════════
    //  生产日报聚合
    // ═══════════════════════════════════════════════════════════

    private async Task<ReportRenderData> AggregateProductionDailyAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();

        var allOrders = await orderRepo.GetAllAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // 时间范围过滤
        var periodOrders = allOrders
            .Where(o => o.CreatedAt >= start && o.CreatedAt <= end)
            .ToList();

        var completed = periodOrders.Count(o => o.Status == OrderStatus.Completed);
        var inProgress = periodOrders.Count(o => o.Status == OrderStatus.InProgress);
        var totalQualified = periodOrders.Sum(o => o.QualifiedQuantity);
        var totalDefective = periodOrders.Sum(o => o.DefectiveQuantity);
        var totalProduced = totalQualified + totalDefective;
        var yieldRate = totalProduced > 0 ? (double)totalQualified / totalProduced * 100 : 100.0;

        // KPI 卡片
        var kpi = new List<KpiCard>
        {
            new("完成工单", completed.ToString(), "单", Colors.Green.Medium),
            new("进行中", inProgress.ToString(), "单", Colors.Blue.Medium),
            new("合格产量", totalQualified.ToString("N0"), "件", Colors.Green.Medium),
            new("不良数", totalDefective.ToString("N0"), "件", totalDefective > 0 ? Colors.Red.Medium : Colors.Grey.Medium),
            new("合格率", $"{yieldRate:F1}", "%", yieldRate >= 98 ? Colors.Green.Medium : Colors.Red.Medium),
        };

        return new ReportRenderData(
            ReportType.ProductionDaily, start, end, now,
            $"生产日报 {start:yyyy-MM-dd}",
            new() { ["prod_kpi"] = kpi },
            new() { ["prod_by_product"] = [] },
            new() { ["prod_by_product"] = [new TableColumnDef("产品编码", "product", 2), new TableColumnDef("产量", "qty", 1, true)] },
            new() { }
        );
    }

    // ═══════════════════════════════════════════════════════════
    //  质量报表聚合（沿用 T2.9 逻辑，增强版）
    // ═══════════════════════════════════════════════════════════

    private async Task<ReportRenderData> AggregateQualityAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var recordRepo = scope.ServiceProvider.GetRequiredService<IQualityRecordRepository>();
        var ncrRepo = scope.ServiceProvider.GetRequiredService<INonConformanceReportRepository>();
        var sampleRepo = scope.ServiceProvider.GetRequiredService<ISpcSampleRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<ISpcRuleAlertRepository>();
        var eightDRepo = scope.ServiceProvider.GetRequiredService<IEightDReportRepository>();
        var planRepo = scope.ServiceProvider.GetRequiredService<IInspectionPlanRepository>();

        var now = DateTimeOffset.UtcNow;

        // ── 产量数据 ──
        var allRecords = await recordRepo.GetByStageAsync(InspectionStage.Ipqc, ct);
        var dateFiltered = allRecords
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end)
            .ToList();
        var totalInspections = dateFiltered.Count;
        var passed = dateFiltered.Count(r => r.Verdict == InspectionVerdict.Passed);
        var failed = dateFiltered.Count(r => r.Verdict == InspectionVerdict.Failed);
        var firstPassYield = totalInspections > 0 ? (double)passed / totalInspections * 100 : 100.0;

        // ── 不良品分布 ──
        var allNcrs = await ncrRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var periodNcrs = allNcrs.Where(n => n.CreatedAt >= start && n.CreatedAt <= end).ToList();
        var openNcrs = periodNcrs.Count(n => n.Status != NcrStatus.Closed);

        var defectDist = new Dictionary<string, int>();
        foreach (var ncr in periodNcrs)
        {
            if (!defectDist.ContainsKey(ncr.Description))
                defectDist[ncr.Description] = 0;
            defectDist[ncr.Description]++;
        }

        // ── SPC 能力汇总 ──
        var plans = await planRepo.GetEnabledAsync(ct);
        var charCodes = plans
            .SelectMany(p => p.Characteristics)
            .Where(c => c.EnableSpc)
            .Select(c => c.CharacteristicCode)
            .Distinct()
            .ToList();

        var spcRows = new List<TableRow>();
        foreach (var code in charCodes)
        {
            var samples = await sampleRepo.GetByCharacteristicAsync(code, 100, ct);
            var periodSamples = samples.Where(s => s.CollectedAt >= start && s.CollectedAt <= end).ToList();
            if (periodSamples.Count == 0) continue;

            var values = periodSamples.SelectMany(s => s.Values).ToArray();
            var mean = values.Average();
            var stdDev = CalculateStdDev(values, mean);

            var planChar = plans
                .SelectMany(p => p.Characteristics)
                .FirstOrDefault(c => c.CharacteristicCode == code);

            var usl = planChar?.UpperSpecLimit;
            var lsl = planChar?.LowerSpecLimit;

            double? cpk = null;
            if (usl.HasValue && lsl.HasValue && stdDev > 0)
            {
                var cpu = (usl.Value - mean) / (3 * stdDev);
                var cpl = (mean - lsl.Value) / (3 * stdDev);
                cpk = Math.Min(cpu, cpl);
            }

            var alerts = await alertRepo.GetByCharacteristicAsync(code, 50, ct);
            var periodAlerts = alerts.Where(a => a.CreatedAt >= start && a.CreatedAt <= end).ToList();
            var cpkStr = cpk?.ToString("F4") ?? "N/A";
            var cpkColor = cpk switch
            {
                >= 1.33 => Colors.Green.Medium,
                >= 1.0 => Colors.Orange.Medium,
                _ => Colors.Red.Medium
            };

            spcRows.Add(new TableRow(new Dictionary<string, string>
            {
                ["code"] = code,
                ["name"] = planChar?.CharacteristicName ?? code,
                ["samples"] = periodSamples.Count.ToString(),
                ["mean"] = mean.ToString("F4"),
                ["cpk"] = cpkStr,
                ["cpk_color"] = cpkColor,
                ["alerts"] = periodAlerts.Count.ToString(),
            }));
        }

        // ── 8D 状态 ──
        var all8Ds = await eightDRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var period8Ds = all8Ds.Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToList();
        var closed8Ds = period8Ds.Count(r => r.Status == EightDStatus.Closed);
        var closureRate = period8Ds.Count > 0 ? (double)closed8Ds / period8Ds.Count * 100 : 100.0;

        // KPI 卡片
        var yieldKpi = new List<KpiCard>
        {
            new("检验总数", totalInspections.ToString("N0"), "次", Colors.Blue.Darken1),
            new("合格", passed.ToString("N0"), "次", Colors.Green.Medium),
            new("不合格", failed.ToString("N0"), "次", failed > 0 ? Colors.Red.Medium : Colors.Grey.Medium),
            new("一次合格率", $"{firstPassYield:F1}", "%", firstPassYield >= 98 ? Colors.Green.Medium : Colors.Red.Medium),
            new("NCR 未关", openNcrs.ToString(), "件", openNcrs > 0 ? Colors.Orange.Medium : Colors.Grey.Medium),
        };

        var eightDKpi = new List<KpiCard>
        {
            new("8D 总数", period8Ds.Count.ToString(), "件", Colors.Blue.Medium),
            new("已关闭", closed8Ds.ToString(), "件", Colors.Green.Medium),
            new("关闭率", $"{closureRate:F1}", "%", closureRate >= 80 ? Colors.Green.Medium : Colors.Orange.Medium),
        };

        // 不良品分布表格
        var defectRows = new List<TableRow>();
        var defectTotal = defectDist.Values.Sum();
        foreach (var (type, count) in defectDist.OrderByDescending(kv => kv.Value))
        {
            var pct = defectTotal > 0 ? (double)count / defectTotal * 100 : 0;
            defectRows.Add(new TableRow(new Dictionary<string, string>
            {
                ["type"] = type,
                ["count"] = count.ToString("N0"),
                ["pct"] = $"{pct:F1}%",
            }));
        }

        // SPC 列定义
        var spcColumns = new List<TableColumnDef>
        {
            new("特性", "code", 1.5),
            new("特性名称", "name", 1.5),
            new("样本数", "samples", 0.8),
            new("均值", "mean", 1),
            new("Cpk", "cpk", 0.8, true, "cpk_color"),
            new("告警", "alerts", 0.8),
        };

        var defectColumns = new List<TableColumnDef>
        {
            new("不良类型", "type", 3),
            new("数量", "count", 1, true),
            new("占比", "pct", 1),
        };

        return new ReportRenderData(
            ReportType.Quality, start, end, now,
            $"质量报告 {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}",
            new() { ["yield_kpi"] = yieldKpi, ["eightd_kpi"] = eightDKpi },
            new() { ["defect_dist"] = defectRows, ["spc_summary"] = spcRows },
            new() { ["defect_dist"] = defectColumns, ["spc_summary"] = spcColumns },
            new() { }
        );
    }

    // ═══════════════════════════════════════════════════════════
    //  维护周报聚合
    // ═══════════════════════════════════════════════════════════

    private async Task<ReportRenderData> AggregateMaintenanceWeeklyAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var now = DateTimeOffset.UtcNow;

        // 简化：获取所有工单进行统计
        var allOrders = await orderRepo.GetListAsync(limit: 200, ct: ct);
        var periodOrders = allOrders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end).ToList();
        var completed = periodOrders.Count(o => o.Status == MaintenanceOrderStatus.Completed);
        var overdue = periodOrders.Count(o => o.Status == MaintenanceOrderStatus.Open && o.CreatedAt < end.AddDays(-7)); // 超过 7 天未完成视为超期
        var completionRate = periodOrders.Count > 0 ? (double)completed / periodOrders.Count * 100 : 100.0;

        var kpi = new List<KpiCard>
        {
            new("创建工单", periodOrders.Count.ToString(), "单", Colors.Blue.Medium),
            new("已完成", completed.ToString(), "单", Colors.Green.Medium),
            new("超期", overdue.ToString(), "单", overdue > 0 ? Colors.Red.Medium : Colors.Grey.Medium),
            new("完成率", $"{completionRate:F1}", "%", completionRate >= 90 ? Colors.Green.Medium : Colors.Orange.Medium),
        };

        return new ReportRenderData(
            ReportType.MaintenanceWeekly, start, end, now,
            $"维护周报 {start:yyyy-MM-dd} 至 {end:yyyy-MM-dd}",
            new() { ["maint_kpi"] = kpi },
            new() { ["maint_by_type"] = [] },
            new() { ["maint_by_type"] = [new TableColumnDef("维护类型", "type", 2), new TableColumnDef("数量", "count", 1, true)] },
            new() { }
        );
    }

    // ═══════════════════════════════════════════════════════════
    //  综合月报聚合（T4.3）
    //  ═══════════════════════════════════════════════════════════
    //  数据来源：
    //  1. IProductionOrderRepository — 工单/产量/合格率
    //  2. IQualityRecordRepository — 一次合格率（FPY）
    //  3. INonConformanceReportRepository — NCR/PPM/质量成本
    //  4. ISpcSampleRepository — Cpk 趋势
    //  5. IEightDReportRepository — 8D 闭环
    //  6. IAndonEventRepository — Andon 月度统计
    //  7. IMaintenanceWorkOrderRepository — 维护统计
    //  8. OeeReportStore — OEE 月度平均
    // ═══════════════════════════════════════════════════════════

    private async Task<ReportRenderData> AggregateMonthlyAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var recordRepo = scope.ServiceProvider.GetRequiredService<IQualityRecordRepository>();
        var ncrRepo = scope.ServiceProvider.GetRequiredService<INonConformanceReportRepository>();
        var sampleRepo = scope.ServiceProvider.GetRequiredService<ISpcSampleRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<ISpcRuleAlertRepository>();
        var eightDRepo = scope.ServiceProvider.GetRequiredService<IEightDReportRepository>();
        var planRepo = scope.ServiceProvider.GetRequiredService<IInspectionPlanRepository>();
        var andonRepo = scope.ServiceProvider.GetRequiredService<IAndonEventRepository>();
        var maintOrderRepo = scope.ServiceProvider.GetRequiredService<IMaintenanceWorkOrderRepository>();

        var now = DateTimeOffset.UtcNow;

        // ── 1. 工单统计 ──
        var allOrders = await orderRepo.GetAllAsync(ct);
        var periodOrders = allOrders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end).ToList();
        var completed = periodOrders.Count(o => o.Status is OrderStatus.Completed or OrderStatus.Closed);
        var totalQualified = periodOrders.Sum(o => o.QualifiedQuantity);
        var totalDefective = periodOrders.Sum(o => o.DefectiveQuantity);
        var totalProduced = totalQualified + totalDefective;
        var yieldRate = totalProduced > 0 ? (double)totalQualified / totalProduced * 100 : 100.0;
        var completionRate = periodOrders.Count > 0 ? (double)completed / periodOrders.Count * 100 : 100.0;

        // ── 2. 质量统计（FPY + PPM）──
        var allRecords = await recordRepo.GetByStageAsync(InspectionStage.Ipqc, ct);
        var periodRecords = allRecords.Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToList();
        var passed = periodRecords.Count(r => r.Verdict == InspectionVerdict.Passed);
        var failed = periodRecords.Count(r => r.Verdict == InspectionVerdict.Failed);
        var totalInsp = periodRecords.Count;
        var fpy = totalInsp > 0 ? (double)passed / totalInsp * 100 : 100.0;
        var ppm = totalProduced > 0 ? (double)totalDefective / totalProduced * 1_000_000 : 0;

        // ── 3. NCR 统计 + 质量成本估算 ──
        var allNcrs = await ncrRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var periodNcrs = allNcrs.Where(n => n.CreatedAt >= start && n.CreatedAt <= end).ToList();
        var openNcrs = periodNcrs.Count(n => n.Status != NcrStatus.Closed);

        // 质量成本估算（严重 NCR 约 500 元，一般 200 元，轻微 50 元）
        var scrapCost = periodNcrs.Where(n => n.Disposition == NcrDisposition.Scrap).Sum(n => n.Severity switch
        {
            NcrSeverity.Critical => 500m,
            NcrSeverity.Major => 200m,
            _ => 50m
        });
        var reworkCost = periodNcrs.Where(n => n.Disposition is NcrDisposition.Rework or NcrDisposition.Repair)
            .Sum(n => n.Severity switch { NcrSeverity.Critical => 300m, NcrSeverity.Major => 100m, _ => 30m });
        var returnCost = periodNcrs.Where(n => n.Disposition == NcrDisposition.ReturnToSupplier)
            .Sum(n => 100m);
        var totalCost = scrapCost + reworkCost + returnCost;
        var costPerUnit = totalProduced > 0 ? (double)totalCost / totalProduced : 0;

        // ── 4. SPC Cpk 能力汇总 ──
        var plans = await planRepo.GetEnabledAsync(ct);
        var charCodes = plans
            .SelectMany(p => p.Characteristics)
            .Where(c => c.EnableSpc)
            .Select(c => c.CharacteristicCode)
            .Distinct()
            .ToList();

        var spcRowsAgg = new List<TableRow>();
        double? avgCpk = null;
        var cpkValues = new List<double>();

        foreach (var code in charCodes)
        {
            var samples = await sampleRepo.GetByCharacteristicAsync(code, 100, ct);
            var periodSamples = samples.Where(s => s.CollectedAt >= start && s.CollectedAt <= end).ToList();
            if (periodSamples.Count == 0) continue;

            var values = periodSamples.SelectMany(s => s.Values).ToArray();
            var mean = values.Average();
            var stdDev = CalculateStdDev(values, mean);

            var planChar = plans
                .SelectMany(p => p.Characteristics)
                .FirstOrDefault(c => c.CharacteristicCode == code);

            var usl = planChar?.UpperSpecLimit;
            var lsl = planChar?.LowerSpecLimit;

            double? cpk = null;
            if (usl.HasValue && lsl.HasValue && stdDev > 0)
            {
                var cpu = (usl.Value - mean) / (3 * stdDev);
                var cpl = (mean - lsl.Value) / (3 * stdDev);
                cpk = Math.Min(cpu, cpl);
            }

            if (cpk.HasValue) cpkValues.Add(cpk.Value);

            var alerts = await alertRepo.GetByCharacteristicAsync(code, 50, ct);
            var periodAlerts = alerts.Where(a => a.CreatedAt >= start && a.CreatedAt <= end).ToList();
            var cpkStr = cpk?.ToString("F4") ?? "N/A";
            var cpkColor = cpk switch
            {
                >= 1.33 => Colors.Green.Medium,
                >= 1.0 => Colors.Orange.Medium,
                _ => Colors.Red.Medium
            };

            spcRowsAgg.Add(new TableRow(new Dictionary<string, string>
            {
                ["code"] = code,
                ["name"] = planChar?.CharacteristicName ?? code,
                ["samples"] = periodSamples.Count.ToString(),
                ["mean"] = mean.ToString("F4"),
                ["cpk"] = cpkStr,
                ["cpk_color"] = cpkColor,
                ["alerts"] = periodAlerts.Count.ToString(),
            }));
        }
        avgCpk = cpkValues.Count > 0 ? cpkValues.Average() : null;

        // ── 5. 8D 统计 ──
        var all8Ds = await eightDRepo.GetByProductCodeAsync("ESP-9.0", ct);
        var period8Ds = all8Ds.Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToList();
        var closed8Ds = period8Ds.Count(r => r.Status == EightDStatus.Closed);
        var closureRate = period8Ds.Count > 0 ? (double)closed8Ds / period8Ds.Count * 100 : 100.0;
        var overdued8ds = period8Ds.Count(r => r.CorrectiveActionDueDate.HasValue && r.CorrectiveActionDueDate.Value < now && r.Status != EightDStatus.Closed);

        // ── 6. OEE 月度平均 ──
        var oeeSnapshots = _oeeStore.GetAllSnapshots();
        var hasOeeData = oeeSnapshots.Any(s => s.TotalUpdates > 0);
        var avgMonthlyOee = hasOeeData ? oeeSnapshots.Where(s => s.TotalUpdates > 0).Average(s => s.Oee) : 0.0;
        var oeeTargetMet = hasOeeData ? oeeSnapshots.Count(s => s.TotalUpdates > 0 && s.Oee >= 0.85) : 0;
        var totalOeeEquip = oeeSnapshots.Count;

        // ── 7. Andon 月度统计 ──
        var allAndonEvts = await andonRepo.GetListAsync(limit: 200, ct: ct);
        var periodAndons = allAndonEvts.Where(a => a.CreatedAt >= start && a.CreatedAt <= end).ToList();
        var openAndons = periodAndons.Count(a => a.Status is AndonEventStatus.Active or AndonEventStatus.EscalatedL2 or AndonEventStatus.EscalatedL3);
        var resolvedAndons = periodAndons.Count(a => a.Status is AndonEventStatus.Resolved or AndonEventStatus.Closed);

        var andonByType = periodAndons
            .GroupBy(a => a.AlarmType)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // ── 8. 维护统计 ──
        var allMaintOrders = await maintOrderRepo.GetListAsync(limit: 200, ct: ct);
        var periodMaint = allMaintOrders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end).ToList();
        var maintCompleted = periodMaint.Count(o => o.Status == MaintenanceOrderStatus.Completed);
        var maintCompletionRate = periodMaint.Count > 0 ? (double)maintCompleted / periodMaint.Count * 100 : 100.0;

        // ════════════════════════════════════════════════
        //  构建 KPI 卡片 — 月度总览
        // ════════════════════════════════════════════════

        var monthlyKpi = new List<KpiCard>
        {
            new("总工单", periodOrders.Count.ToString(), "单", Colors.Blue.Medium),
            new("完成率", $"{completionRate:F1}", "%", completionRate >= 95 ? Colors.Green.Medium : Colors.Orange.Medium),
            new("总产量", totalProduced.ToString("N0"), "件", Colors.Blue.Darken1),
            new("合格率", $"{yieldRate:F1}", "%", yieldRate >= 98 ? Colors.Green.Medium : Colors.Red.Medium),
            new("FPY", $"{fpy:F1}", "%", fpy >= 98 ? Colors.Green.Medium : Colors.Red.Medium),
        };

        // 质量成本 KPI
        var costKpi = new List<KpiCard>
        {
            new("报废成本", $"{scrapCost:N0}", "元", scrapCost > 0 ? Colors.Red.Medium : Colors.Grey.Medium),
            new("返工成本", $"{reworkCost:N0}", "元", reworkCost > 0 ? Colors.Orange.Medium : Colors.Grey.Medium),
            new("总质量成本", $"{totalCost:N0}", "元", Colors.Purple.Medium),
            new("单位成本", $"{costPerUnit:N2}", "元/件", Colors.Grey.Medium),
            new("PPM", $"{ppm:F0}", "PPM", ppm <= 5000 ? Colors.Green.Medium : ppm <= 20000 ? Colors.Orange.Medium : Colors.Red.Medium),
        };

        // ════════════════════════════════════════════════
        //  构建质量趋势表
        // ════════════════════════════════════════════════

        var qualityColumns = new List<TableColumnDef>
        {
            new("指标", "metric", 2, true),
            new("本月值", "current", 1.5),
            new("上月值", "previous", 1.5),
            new("趋势", "trend", 1),
        };

        var qualityRows = new List<TableRow>
        {
            new(new() { ["metric"] = "一次合格率 (FPY)", ["current"] = $"{fpy:F1}%", ["previous"] = "—", ["trend"] = fpy >= 98 ? "↑" : fpy >= 95 ? "→" : "↓" }),
            new(new() { ["metric"] = "合格率", ["current"] = $"{yieldRate:F1}%", ["previous"] = "—", ["trend"] = yieldRate >= 98 ? "↑" : "→" }),
            new(new() { ["metric"] = "PPM", ["current"] = $"{ppm:F0}", ["previous"] = "—", ["trend"] = ppm <= 5000 ? "↑" : "↓" }),
            new(new() { ["metric"] = "不良数", ["current"] = totalDefective.ToString("N0"), ["previous"] = "—", ["trend"] = "→" }),
            new(new() { ["metric"] = "完成工单", ["current"] = completed.ToString(), ["previous"] = "—", ["trend"] = "→" }),
            new(new() { ["metric"] = "总产量", ["current"] = totalProduced.ToString("N0"), ["previous"] = "—", ["trend"] = "→" }),
            new(new() { ["metric"] = "平均 Cpk", ["current"] = avgCpk?.ToString("F4") ?? "N/A", ["previous"] = "—", ["trend"] = avgCpk >= 1.33 ? "↑" : avgCpk >= 1.0 ? "→" : "↓" }),
        };

        // ════════════════════════════════════════════════
        //  SPC 能力汇总表
        // ════════════════════════════════════════════════

        var spcColumns = new List<TableColumnDef>
        {
            new("特性", "code", 1.5),
            new("名称", "name", 1.5),
            new("样本", "samples", 0.8),
            new("均值", "mean", 1),
            new("Cpk", "cpk", 0.8, true, "cpk_color"),
            new("告警", "alerts", 0.8),
        };

        // ════════════════════════════════════════════════
        //  OEE 月度趋势表
        // ════════════════════════════════════════════════

        var oeeColumns = new List<TableColumnDef>
        {
            new("设备", "equip", 1.5),
            new("设备名称", "name", 2, true),
            new("OEE", "oee", 1, true, "oee_color"),
            new("可用率", "avail", 1),
            new("性能率", "perf", 1),
            new("良品率", "quality", 1),
            new("等级", "grade", 1),
        };

        var oeeRows = new List<TableRow>();
        var oeeDict = oeeSnapshots.ToDictionary(s => s.EquipmentCode);

        foreach (var eq in Equipment.DefaultEquipment)
        {
            var snap = oeeDict.GetValueOrDefault(eq.EquipmentCode);
            var oeePct = snap?.Oee ?? 0;
            var hasData = snap?.TotalUpdates > 0;

            oeeRows.Add(new TableRow(new Dictionary<string, string>
            {
                ["equip"] = eq.EquipmentCode,
                ["name"] = eq.Name,
                ["oee"] = hasData ? $"{oeePct * 100:F1}%" : "—",
                ["oee_color"] = oeePct >= 0.85 ? Colors.Green.Medium :
                               oeePct >= 0.70 ? Colors.Orange.Medium : Colors.Red.Medium,
                ["avail"] = hasData ? $"{snap!.Availability * 100:F1}%" : "—",
                ["perf"] = hasData ? $"{snap!.Performance * 100:F1}%" : "—",
                ["quality"] = hasData ? $"{snap!.Quality * 100:F1}%" : "—",
                ["grade"] = oeePct >= 0.85 ? "S" : oeePct >= 0.70 ? "A" : hasData ? "B" : "—",
            }));
        }

        // ════════════════════════════════════════════════
        //  8D 统计表
        // ════════════════════════════════════════════════

        var eightDColumns = new List<TableColumnDef>
        {
            new("指标", "metric", 2, true),
            new("数值", "value", 1),
        };

        var eightDRows = new List<TableRow>
        {
            new(new() { ["metric"] = "8D 总数", ["value"] = period8Ds.Count.ToString() }),
            new(new() { ["metric"] = "已关闭", ["value"] = closed8Ds.ToString() }),
            new(new() { ["metric"] = "关闭率", ["value"] = $"{closureRate:F1}%" }),
            new(new() { ["metric"] = "超期未关", ["value"] = overdued8ds.ToString() }),
        };

        // ════════════════════════════════════════════════
        //  Andon 月度统计表
        // ════════════════════════════════════════════════

        var andonColumns = new List<TableColumnDef>
        {
            new("报警类型", "type", 2),
            new("次数", "count", 1, true),
        };

        var andonRows = andonByType.Select(kv => new TableRow(new Dictionary<string, string>
        {
            ["type"] = kv.Key switch
            {
                "TorqueExceeded" => "扭矩超差",
                "LeakRateHigh" => "泄漏超标",
                "FlashFailed" => "刷写失败",
                "CanCommunicationError" => "CAN 异常",
                "EquipmentAlarm" => "设备报警",
                "ProcessDeviation" => "过程偏差",
                "MaterialDefect" => "物料缺陷",
                _ => kv.Key
            },
            ["count"] = kv.Value.ToString(),
        })).ToList();

        // ════════════════════════════════════════════════
        //  返回完整渲染数据
        // ════════════════════════════════════════════════

        return new ReportRenderData(
            ReportType.Monthly, start, end, now,
            $"综合管理月报 {start:yyyy-MM}",
            new()
            {
                ["monthly_kpi"] = monthlyKpi,
                ["quality_cost"] = costKpi,
            },
            new()
            {
                ["quality_trend"] = qualityRows,
                ["spc_summary"] = spcRowsAgg,
                ["oee_trend"] = oeeRows,
                ["eightd_summary"] = eightDRows,
                ["andon_summary"] = andonRows,
            },
            new()
            {
                ["quality_trend"] = qualityColumns,
                ["spc_summary"] = spcColumns,
                ["oee_trend"] = oeeColumns,
                ["eightd_summary"] = eightDColumns,
                ["andon_summary"] = andonColumns,
            },
            new() { }
        );
    }

    private static double CalculateStdDev(double[] values, double mean)
    {
        if (values.Length < 2) return 0;
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Length - 1));
    }
}
