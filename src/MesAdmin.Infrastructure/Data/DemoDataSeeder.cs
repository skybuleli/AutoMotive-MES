using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Data;

/// <summary>
/// 可重复执行的演示业务数据种子。
/// 所有记录使用 DEMO-* 业务编号作为幂等边界，避免与真实生产数据混淆。
/// </summary>
public static class DemoDataSeeder
{
    private const string SeedOrderPrefix = "DEMO-WO-";
    private const string SeedVersion = "DEMO-SEED-V1";

    public static async Task SeedAsync(MesDbContext db, ILogger logger)
    {
        if (await db.ProductionOrders.AnyAsync(o => o.OrderNumber.StartsWith(SeedOrderPrefix)))
        {
            logger.ZLogInformation($"演示数据：{SeedVersion} 已存在，跳过");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();

        var routing90 = CreateRouting("ESP-9.0", "DEMO-1.0", "DEMO-ECO-90", now.AddDays(-45));
        var routing91 = CreateRouting("ESP-9.1", "DEMO-1.0", "DEMO-ECO-91", now.AddDays(-30));
        db.Routings.AddRange(routing90, routing91);

        var orders = CreateOrders(routing90, routing91, now);
        db.ProductionOrders.AddRange(orders);
        db.WorkOrderOperations.AddRange(orders.SelectMany(o => CreateOperations(o, o.RoutingId == routing91.Id ? routing91 : routing90, now)));

        var calendars = CreateCalendars();
        db.CapacityCalendars.AddRange(calendars);
        db.ProductionSchedules.AddRange(CreateSchedules(orders, now));

        var gauges = CreateGauges(now);
        db.Gauges.AddRange(gauges);
        db.CalibrationRecords.AddRange(CreateCalibrationRecords(gauges, now));

        var plans = CreateInspectionPlans(now);
        db.InspectionPlans.AddRange(plans);
        var qualityRecords = CreateQualityRecords(orders, plans, gauges, now);
        db.QualityRecords.AddRange(qualityRecords);

        var spcSamples = CreateSpcSamples(orders[1], gauges[0], now);
        db.SpcSamples.AddRange(spcSamples);
        var spcAlert = SpcRuleAlert.Create(
            SpcRuleType.SixInTrend, "TOR-M6", spcSamples[^1].Id, orders[1].Id,
            "EQ-TQ-01", SpcAlertLevel.Warning, "Rule 5：M6 扭矩连续 6 点上升趋势，请检查拧紧枪校准和螺纹状态");
        db.SpcRuleAlerts.Add(spcAlert);

        var firstArticle = CreateFirstArticle(orders[1], gauges[0], now);
        db.FirstArticleInspections.Add(firstArticle);

        var hydraulic = CreateHydraulicTests(orders, now);
        db.HydraulicTestResults.AddRange(hydraulic);

        var ncr = NonConformanceReport.CreateFromQualityRecord(
            qualityRecords[2], "IPQC 抽检发现 M6 扭矩偏高，最大值 23.6 Nm，超出规格上限 23.0 Nm", 2, NcrSeverity.Major);
        ncr.NcrNumber = "NCR-DEMO-001";
        ncr.DispositionDeadline = now.AddDays(3);
        ncr.ResponsibleDept = "装配制造部";
        ncr.SubmitForReview("qe");
        ncr.SetDisposition(NcrDisposition.Rework, "隔离 2 件，返工后重新执行扭矩确认和液压测试");
        db.NonConformanceReports.Add(ncr);

        var eightD = EightDReport.Create("M6 扭矩偏高导致过程能力下降", "ESP-9.0", "ESP 制动总成 9.0");
        eightD.ReportNumber = "8D-DEMO-001";
        eightD.SetTeam("qe", "leader,ee,operator-031");
        eightD.DescribeProblem("2026-08-26 早班 IPQC 发现 M6-FL 扭矩 2 件超上限，影响工序 12");
        eightD.SetContainment("立即隔离 DEMO-WO-20260826-001 已产 2 件，暂停 EQ-TQ-01 后续批量生产");
        eightD.SetRootCause("拧紧枪反力臂松动，导致末端扭矩重复性变差", "反力臂锁紧螺钉未按点检表复核");
        eightD.SetCorrectiveAction("更换反力臂并增加班前首件复核项目", "ee", now.AddDays(5));
        eightD.SetPreventiveAction("将反力臂点检纳入每日设备点检，连续三天记录扭矩 Cpk");
        db.EightDReports.Add(eightD);
        ncr.LinkEightDReport(eightD.Id);

        var andonEvents = CreateAndonEvents(orders[1], ncr.Id, now);
        db.AndonEvents.AddRange(andonEvents);

        var maintenancePlans = CreateMaintenancePlans(now);
        db.MaintenancePlans.AddRange(maintenancePlans);
        var maintenanceOrders = CreateMaintenanceOrders(maintenancePlans, now);
        db.MaintenanceWorkOrders.AddRange(maintenanceOrders);

        var spareParts = CreateSpareParts();
        db.SpareParts.AddRange(spareParts);
        var usage = SparePartUsage.Create(Ulid.NewUlid(), spareParts[0].Id, maintenanceOrders[0].Id, 1, 380, "更换拧紧枪反力臂锁紧组件");
        db.SparePartUsages.Add(usage);
        spareParts[0].Consume(1);
        var purchaseRequest = PurchaseRequest.Create(Ulid.NewUlid(), spareParts[1].Id, 20, "安全库存不足，保障液压测试台连续运行", "ee");
        purchaseRequest.RequestNumber = "PR-DEMO-001";
        db.PurchaseRequests.Add(purchaseRequest);

        var suppliers = CreateSuppliers(now);
        db.Suppliers.AddRange(suppliers);
        db.SupplierScoreCards.AddRange(CreateScoreCards(suppliers));
        db.PpapDocuments.AddRange(CreatePpapDocuments(suppliers, now));
        db.CriticalSupplierSettings.AddRange(CreateCriticalSupplierSettings());

        var traceLinks = CreateTraceability(orders[0], now);
        db.TraceabilityLinks.AddRange(traceLinks);
        var demoBatches = await db.MaterialBatches
            .Where(b => new[] { "BAT-ECU-001", "BAT-HCU-001" }.Contains(b.BatchNumber))
            .ToDictionaryAsync(b => b.BatchNumber);
        if (!demoBatches.TryGetValue("BAT-ECU-001", out var ecuBatch)
            || !demoBatches.TryGetValue("BAT-HCU-001", out var hcuBatch))
        {
            throw new InvalidOperationException("演示数据所需的基础物料批次不存在");
        }
        db.MaterialBindings.AddRange(CreateBindings(orders[0], ecuBatch.Id, hcuBatch.Id));
        db.MaterialConsumptions.AddRange(CreateConsumptions(orders[0], now));
        db.ConsumptionVarianceReports.Add(ConsumptionVarianceReport.Create(
            orders[0].Id, orders[0].OrderNumber, "SEAL-ORING-18", "O 型密封圈 18×2.5 NBR",
            120, 126, 6, 5, "PCS"));
        db.JitPullSignals.Add(CreateJitSignal(orders[1]));
        db.GoodsReceipts.Add(GoodsReceipt.Create(orders[0].Id, orders[0].OrderNumber, orders[0].ProductCode, orders[0].QualifiedQuantity, "qe", now.AddDays(-1)));

        var documents = CreateDocuments(now);
        db.ControlledDocuments.AddRange(documents.Documents);
        db.DocumentVersions.AddRange(documents.Versions);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        logger.ZLogInformation($"演示数据：{SeedVersion} 创建完成，工单 {orders.Count} 个、SPC 子组 {spcSamples.Count} 个");
    }

    private static Routing CreateRouting(string productCode, string version, string eco, DateTimeOffset createdAt)
    {
        var operations = new List<RoutingOperation>();
        var names = new[]
        {
            "来料扫码与防错", "ECU/HCU 合装", "电机预装", "壳体清洁", "密封圈安装",
            "HCU 阀体装配", "泵组件安装", "压力传感器安装", "阀体视觉检查", "上壳体合盖",
            "M6-FL 螺栓拧紧", "M6-FR 螺栓拧紧", "M6-RL 螺栓拧紧", "M6-RR 螺栓拧紧", "M8 主螺栓拧紧",
            "扭矩结果复核", "电气连接检查", "ECU 上电自检", "固件版本校验", "标定参数下载",
            "CAN 通信测试", "建压测试", "保压测试", "泄漏率测试", "12 路阀动作测试",
            "液压测试结果判定", "外观终检", "铭牌打印", "VIN 绑定", "成品标签打印", "成品放行"
        };
        for (var i = 0; i < names.Length; i++)
        {
            var sequence = i + 1;
            var station = sequence switch
            {
                <= 2 => 1,
                <= 5 => 2,
                <= 16 => 3,
                <= 26 => 4,
                <= 28 => 5,
                <= 29 => 6,
                _ => 7,
            };
            var code = station == 3 && sequence is >= 11 and <= 15 ? $"TQ-{sequence - 10:D2}" : "ST" + station.ToString("D2") + "-" + sequence.ToString("D2");
            var parameters = sequence switch
            {
                11 => new List<ParameterTemplate> { new() { ParameterCode = "TOR-M6", ParameterName = "M6 螺栓扭矩", StandardValue = 21.5, LowerSpecLimit = 20, UpperSpecLimit = 23, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 } },
                15 => new List<ParameterTemplate> { new() { ParameterCode = "TOR-M8", ParameterName = "M8 主螺栓扭矩", StandardValue = 34, LowerSpecLimit = 32, UpperSpecLimit = 36, Unit = "Nm", EnableSpc = true, SpcSubgroupSize = 5 } },
                22 => new List<ParameterTemplate> { new() { ParameterCode = "HYD-BUILD", ParameterName = "建压时间", StandardValue = 180, LowerSpecLimit = 0, UpperSpecLimit = 250, Unit = "ms", EnableSpc = true, SpcSubgroupSize = 5 } },
                24 => new List<ParameterTemplate> { new() { ParameterCode = "LEAK-01", ParameterName = "泄漏率", StandardValue = 0.2, LowerSpecLimit = 0, UpperSpecLimit = 0.5, Unit = "CC/hr", EnableSpc = true, SpcSubgroupSize = 5 } },
                _ => new List<ParameterTemplate>()
            };
            operations.Add(new RoutingOperation
            {
                Sequence = sequence,
                Station = station,
                OperationCode = code,
                OperationName = names[i],
                StandardTimeSeconds = station == 4 ? 55 : 35,
                FixtureCode = "FX-ST" + station.ToString("D2"),
                FixtureName = "ST" + station + " 专用工装",
                EquipmentCode = station switch { 3 => "EQ-TQ-01", 4 => "EQ-HYD-01", 5 => "EQ-FLS-01", 6 => "EQ-FT-01", 7 => "EQ-VN-01", _ => "EQ-ASM-01" },
                IsStationSentinel = sequence is 5 or 16 or 26 or 28 or 29 or 31,
                TargetComponent = sequence is >= 11 and <= 15 ? new[] { "M6-FL", "M6-FR", "M6-RL", "M6-RR", "M8-MAIN" }[sequence - 11] : null,
                ParameterTemplates = parameters,
            });
        }

        var routing = Routing.Create(Ulid.NewUlid(), productCode, productCode + " 标准工艺路线 V" + version, version, "manager", operations, eco, "演示环境标准工艺路线");
        routing.SubmitForApproval();
        routing.Approve("manager");
        routing.Release();
        routing.CreatedAt = createdAt;
        routing.UpdatedAt = createdAt;
        routing.ApprovedAt = createdAt.AddHours(4);
        routing.EffectiveDate = createdAt.AddDays(1);
        return routing;
    }

    private static List<ProductionOrder> CreateOrders(Routing routing90, Routing routing91, DateTimeOffset now)
    {
        var completed = ProductionOrder.Create(Ulid.NewUlid(), "DEMO-WO-20260824-001", "ESP-9.0", routing90.Id, "V1.0", 120, 1, now.AddDays(-4));
        completed.Release();
        completed.Start();
        completed.Complete(118, 2, now.AddDays(-1));
        completed.Close();
        completed.PlannedStartAt = now.AddDays(-4);
        completed.PlannedEndAt = now.AddDays(-3);
        completed.ActualStartAt = now.AddDays(-4).AddHours(1);
        completed.ActualEndAt = now.AddDays(-1);

        var inProgress = ProductionOrder.Create(Ulid.NewUlid(), "DEMO-WO-20260826-001", "ESP-9.0", routing90.Id, "V1.0", 240, 2, now.AddDays(-2));
        inProgress.Release();
        inProgress.Start();
        inProgress.QualifiedQuantity = 146;
        inProgress.DefectiveQuantity = 2;
        inProgress.PlannedStartAt = now.AddDays(-1);
        inProgress.PlannedEndAt = now.AddHours(8);
        inProgress.ActualStartAt = now.AddDays(-1).AddHours(1);

        var released = ProductionOrder.Create(Ulid.NewUlid(), "DEMO-WO-20260829-001", "ESP-9.1", routing91.Id, "V1.0", 180, 1, now.AddHours(-2));
        released.Release();
        released.PlannedStartAt = now.AddDays(1);
        released.PlannedEndAt = now.AddDays(1).AddHours(8);
        return [completed, inProgress, released];
    }

    private static IEnumerable<WorkOrderOperation> CreateOperations(ProductionOrder order, Routing routing, DateTimeOffset now)
    {
        var operations = routing.Operations.Select(o => WorkOrderOperation.Create(order.Id, o.Sequence, o.Station, o.OperationCode, o.OperationName)).ToList();
        if (order.Status is OrderStatus.Completed or OrderStatus.Closed)
        {
            foreach (var operation in operations)
            {
                operation.Start("operator-031", operation.OperationCode.StartsWith("TQ") ? "EQ-TQ-01" : "EQ-ASM-01", now.AddDays(-2).AddMinutes(operation.Sequence * 3));
                operation.Complete(now.AddDays(-2).AddMinutes(operation.Sequence * 3 + 2));
            }
        }
        else if (order.Status == OrderStatus.InProgress)
        {
            foreach (var operation in operations.Where(o => o.Sequence <= 18))
            {
                operation.Start("operator-031", operation.OperationCode.StartsWith("TQ") ? "EQ-TQ-01" : "EQ-ASM-01", now.AddHours(-5).AddMinutes(operation.Sequence));
                operation.Complete(now.AddHours(-5).AddMinutes(operation.Sequence + 2));
            }
            operations[18].Start("operator-031", "EQ-FLS-01", now.AddMinutes(-20));
        }
        return operations;
    }

    private static List<CapacityCalendar> CreateCalendars()
    {
        var definitions = new[]
        {
            ("EQ-ASM-01", "合装工作站", 2), ("EQ-TQ-01", "螺栓拧紧机", 3), ("EQ-HYD-01", "液压测试台", 4),
            ("EQ-FLS-01", "ECU 刷写台", 5), ("EQ-FT-01", "功能终检台", 6), ("EQ-VN-01", "VIN 绑定台", 7),
        };
        return definitions.Select(x => CapacityCalendar.Create(Ulid.NewUlid(), x.Item1, x.Item2, x.Item3)).ToList();
    }

    private static IEnumerable<ProductionSchedule> CreateSchedules(List<ProductionOrder> orders, DateTimeOffset now)
    {
        var first = ProductionSchedule.Create(Ulid.NewUlid(), orders[0].Id, orders[0].OrderNumber, orders[0].ProductCode, 120, "EQ-ASM-01", 2, ShiftType.Morning, DateOnly.FromDateTime(now.AddDays(-4).UtcDateTime), now.AddDays(-4), 420, 30);
        first.Start(); first.Complete();
        var current = ProductionSchedule.Create(Ulid.NewUlid(), orders[1].Id, orders[1].OrderNumber, orders[1].ProductCode, 240, "EQ-TQ-01", 3, ShiftType.Morning, DateOnly.FromDateTime(now.UtcDateTime), now.AddHours(-4), 480, 30, 2, RushOrderType.OemUrgent, "OEM 客户加急交付");
        current.Start();
        var next = ProductionSchedule.Create(Ulid.NewUlid(), orders[2].Id, orders[2].OrderNumber, orders[2].ProductCode, 180, "EQ-ASM-01", 2, ShiftType.Afternoon, DateOnly.FromDateTime(now.AddDays(1).UtcDateTime), now.AddDays(1).AddHours(8), 420, 45);
        return [first, current, next];
    }

    private static List<Gauge> CreateGauges(DateTimeOffset now)
    {
        return
        [
            Gauge.Create(Ulid.NewUlid(), "GT-TQ-001", "数字扭矩仪", GaugeType.TorqueWrench, "0-50 Nm", "0.01 Nm", "0.5级", 180, now.AddDays(-150), "计量室 A-01"),
            Gauge.Create(Ulid.NewUlid(), "GT-PRS-001", "液压压力校验仪", GaugeType.PressureGauge, "0-250 bar", "0.1 bar", "0.25级", 365, now.AddDays(-340), "计量室 A-02"),
            Gauge.Create(Ulid.NewUlid(), "GT-DIM-001", "数显卡尺", GaugeType.Caliper, "0-150 mm", "0.01 mm", "0.02 mm", 365, now.AddDays(-20), "质量室 B-03"),
        ];
    }

    private static IEnumerable<CalibrationRecord> CreateCalibrationRecords(List<Gauge> gauges, DateTimeOffset now)
    {
        return gauges.Select((g, i) => CalibrationRecord.Create(Ulid.NewUlid(), g.Id, now.AddDays(-30 - i), CalibrationResult.Pass, $"CAL-DEMO-{i + 1:000}", "qe", g.NextDueAt!.Value, "演示校准证书"));
    }

    private static List<InspectionPlan> CreateInspectionPlans(DateTimeOffset now)
    {
        var iqc = InspectionPlan.Create("ESP-9.0 IQC 关键件来料检验", "V1.0", InspectionStage.Iq, "每批抽5件", 5, 0, 1, now.AddDays(-30));
        iqc.ProductCode = "MCU-TC3X7"; iqc.AqlValue = 0.65; iqc.InspectionLevel = "II";
        iqc.AddCharacteristic(PlanCharacteristic.CreateVariable("DIM-MCU", "封装外形尺寸", 10.0, "mm", 10.2, 9.8, true));
        var ipqc = InspectionPlan.Create("ESP-9.0 IPQC 扭矩过程控制计划", "V2.1", InspectionStage.Ipqc, "每50件抽5件", 5, 0, 1, now.AddDays(-30));
        ipqc.ProductCode = "ESP-9.0"; ipqc.Station = 3; ipqc.EnableSpcChart = true; ipqc.SpcSubgroupSize = 5;
        var torque = PlanCharacteristic.CreateVariable("TOR-M6", "M6 螺栓扭矩", 21.5, "Nm", 23, 20, true, true);
        torque.SetXbarRControlLimits(21.5, 0.9, 5); ipqc.AddCharacteristic(torque);
        var oqc = InspectionPlan.Create("ESP 总成 OQC 出货检验", "V1.0", InspectionStage.Oqc, "每批抽5件", 5, 0, 1, now.AddDays(-30));
        oqc.ProductCode = "ESP-9.0";
        oqc.AddCharacteristic(PlanCharacteristic.CreateAttribute("VIS-001", "外观及铭牌", "合格/不合格"));
        return [iqc, ipqc, oqc];
    }

    private static List<QualityRecord> CreateQualityRecords(List<ProductionOrder> orders, List<InspectionPlan> plans, List<Gauge> gauges, DateTimeOffset now)
    {
        var iqc = QualityRecord.CreateIqc(plans[0].Id, plans[0].PlanName, "MCU-TC3X7", "TC3x7 主控 MCU", "BAT-MCU-001", "SUP-INFINEON", "Infineon 无锡", "qe", 5, 0, 1, "AQL=0.65, Level II", [MeasuredCharacteristic.Create("DIM-MCU", "封装外形尺寸", 10, "mm", 10.2, 9.8)], gauges[2].Id);
        iqc.RecordCharacteristic("DIM-MCU", 10.01); iqc.Complete(); iqc.CreatedAt = now.AddDays(-3); iqc.CompletedAt = now.AddDays(-3).AddHours(1);
        var ipqcPass = QualityRecord.CreateIpqc(orders[0].Id, orders[0].OrderNumber, orders[0].ProductCode, "ESP 制动总成 9.0", plans[1].Id, plans[1].PlanName, "qe", [MeasuredCharacteristic.Create("TOR-M6", "M6 螺栓扭矩", 21.5, "Nm", 23, 20)], gaugeId: gauges[0].Id);
        ipqcPass.RecordCharacteristic("TOR-M6", 21.6); ipqcPass.Complete(); ipqcPass.CreatedAt = now.AddDays(-2);
        var ipqcFail = QualityRecord.CreateIpqc(orders[1].Id, orders[1].OrderNumber, orders[1].ProductCode, "ESP 制动总成 9.0", plans[1].Id, plans[1].PlanName, "qe", [MeasuredCharacteristic.Create("TOR-M6", "M6 螺栓扭矩", 21.5, "Nm", 23, 20)], gaugeId: gauges[0].Id);
        ipqcFail.RecordCharacteristic("TOR-M6", 23.6); ipqcFail.Complete(); ipqcFail.CreatedAt = now.AddHours(-3);
        return [iqc, ipqcPass, ipqcFail];
    }

    private static FirstArticleInspection CreateFirstArticle(ProductionOrder order, Gauge gauge, DateTimeOffset now)
    {
        var fai = FirstArticleInspection.Create(order.Id, order.OrderNumber, order.ProductCode, "班次首件", "operator-031", [
            InspectionItem.Create("TOR-M6", "M6 螺栓扭矩", 21.5, "Nm", 23, 20),
            InspectionItem.Create("HYD-BUILD", "建压时间", 180, "ms", 250, 0),
            InspectionItem.Create("LEAK-01", "泄漏率", 0.2, "CC/hr", 0.5, 0),
        ], gauge.Id);
        fai.Start();
        foreach (var item in fai.Items) item.RecordValue(item.StandardValue);
        fai.Complete("qe", true, "首件三项参数全部合格，允许批量生产");
        fai.CreatedAt = now.AddHours(-5);
        return fai;
    }

    private static List<SpcSample> CreateSpcSamples(ProductionOrder order, Gauge gauge, DateTimeOffset now)
    {
        var random = new Random(20260828);
        var result = new List<SpcSample>();
        for (var i = 1; i <= 12; i++)
        {
            var center = 21.15 + i * 0.045;
            var values = new[] { center - 0.22, center - 0.06, center + 0.02, center + 0.08, center + 0.16 + random.NextDouble() * 0.04 };
            result.Add(SpcSample.Create("TOR-M6", 9000 + i, values, order.Id, order.OrderNumber, "EQ-TQ-01", now.AddMinutes(-i * 20), gauge.Id));
        }
        return result;
    }

    private static List<HydraulicTestResult> CreateHydraulicTests(List<ProductionOrder> orders, DateTimeOffset now)
    {
        var passed = HydraulicTestResult.Create("EQ-HYD-01", orders[0].Id, "ESP9-20260824-0001", 3);
        passed.RecordPressureBuild(182); passed.RecordHoldPressure(180); passed.RecordPressureRelease(215); passed.RecordLeakRate(0.18);
        for (var i = 1; i <= 12; i++) passed.AddSolenoidTest(new SolenoidValveTest(i, true, 42 + i * 0.3, 8.2, null));
        passed.Complete(); passed.StartedAt = now.AddDays(-1); passed.CompletedAt = now.AddDays(-1).AddMinutes(4);
        var failed = HydraulicTestResult.Create("EQ-HYD-01", orders[1].Id, "ESP9-20260826-0147", 3);
        failed.RecordPressureBuild(188); failed.RecordHoldPressure(178); failed.RecordPressureRelease(220); failed.RecordLeakRate(0.72);
        for (var i = 1; i <= 11; i++) failed.AddSolenoidTest(new SolenoidValveTest(i, true, 45, 8.1, null));
        failed.AddSolenoidTest(new SolenoidValveTest(12, false, 420, 0, "F001")); failed.Complete(); failed.StartedAt = now.AddHours(-2);
        var second = HydraulicTestResult.Create("EQ-HYD-01", orders[0].Id, "ESP9-20260824-0002", 3);
        second.RecordPressureBuild(175); second.RecordHoldPressure(181); second.RecordPressureRelease(205); second.RecordLeakRate(0.22); second.Complete();
        return [passed, failed, second];
    }

    private static List<AndonEvent> CreateAndonEvents(ProductionOrder order, Ulid ncrId, DateTimeOffset now)
    {
        var active = AndonEvent.Create("EQ-HYD-01", 4, AndonAlarmType.LeakRateHigh, AndonSeverity.Critical, "泄漏率 0.72 CC/hr 超出上限 0.50 CC/hr，设备已锁定", 0.72, "LeakRate", 0.5, null, order.Id);
        active.EventNumber = "AND-DEMO-001"; active.OccurredAt = now.AddMinutes(-3);
        var escalated = AndonEvent.Create("EQ-TQ-01", 3, AndonAlarmType.TorqueExceeded, AndonSeverity.Major, "M6-FL 扭矩 23.6 Nm 超出上限 23.0 Nm", 23.6, "Torque-M6-FL", 23, 20, order.Id);
        escalated.EventNumber = "AND-DEMO-002"; escalated.Status = AndonEventStatus.EscalatedL2; escalated.EscalationLevel = 1; escalated.EscalatedAt = now.AddMinutes(-8); escalated.OccurredAt = now.AddMinutes(-9); escalated.NonConformanceReportId = ncrId;
        var closed = AndonEvent.Create("EQ-FLS-01", 5, AndonAlarmType.FlashFailed, AndonSeverity.Minor, "ECU 固件校验失败，重新刷写后恢复", 1, "FlashVerify", null, null, order.Id);
        closed.EventNumber = "AND-DEMO-003"; closed.Acknowledge("ee"); closed.Resolve("ee", "更换通讯线并重新刷写"); closed.Close("验证通过，设备恢复生产"); closed.OccurredAt = now.AddDays(-2);
        return [active, escalated, closed];
    }

    private static List<MaintenancePlan> CreateMaintenancePlans(DateTimeOffset now)
    {
        var torque = MaintenancePlan.Create(Ulid.NewUlid(), "EQ-TQ-01", "螺栓拧紧机", MaintenanceType.CycleBased, 100000, "拧紧机定期标定", "检查反力臂、校验扭矩传感器、执行 5 点重复性测试");
        torque.LastTriggeredCycleCount = 98500;
        var hydraulic = MaintenancePlan.Create(Ulid.NewUlid(), "EQ-HYD-01", "液压测试台", MaintenanceType.TimeBased, 30, "液压台月度保养", "更换液压油滤芯，检查密封件和压力传感器，执行泄漏自检");
        hydraulic.LastTriggeredAt = now.AddDays(-26);
        return [torque, hydraulic];
    }

    private static List<MaintenanceWorkOrder> CreateMaintenanceOrders(List<MaintenancePlan> plans, DateTimeOffset now)
    {
        var open = MaintenanceWorkOrder.Create(Ulid.NewUlid(), plans[0].Id, "EQ-TQ-01", "螺栓拧紧机", MaintenanceType.CycleBased, MaintenanceTriggerType.CycleTrigger, 98500, "拧紧机周期标定", "完成扭矩传感器校验及反力臂点检");
        open.OrderNumber = "MT-DEMO-001";
        var completed = MaintenanceWorkOrder.Create(Ulid.NewUlid(), plans[1].Id, "EQ-HYD-01", "液压测试台", MaintenanceType.TimeBased, MaintenanceTriggerType.TimeTrigger, 30, "液压台月度保养", "更换滤芯并校验压力表");
        completed.OrderNumber = "MT-DEMO-002"; completed.Start("ee"); completed.Complete("ee", "滤芯已更换，压力保持测试通过"); completed.CreatedAt = now.AddDays(-10);
        return [open, completed];
    }

    private static List<SparePart> CreateSpareParts()
    {
        var arm = SparePart.Create(Ulid.NewUlid(), "SP-TQ-ARM", "拧紧机反力臂锁紧组件", "EQ-TQ-01 专用，含锁紧螺钉", "SET", 8, 3, "EQ-TQ-01"); arm.UpdateStock(2);
        var filter = SparePart.Create(Ulid.NewUlid(), "SP-HYD-FILTER", "液压油滤芯", "50 μm，HYD-25", "PCS", 30, 10, "EQ-HYD-01"); filter.UpdateStock(4);
        var sensor = SparePart.Create(Ulid.NewUlid(), "SP-HYD-SENSOR", "压力传感器", "0-250 bar，4-20mA", "PCS", 5, 2, "EQ-HYD-01"); sensor.UpdateStock(6);
        return [arm, filter, sensor];
    }

    private static List<Supplier> CreateSuppliers(DateTimeOffset now)
    {
        var bosch = Supplier.Create(Ulid.NewUlid(), "SUP-BOSCH", "博世汽车部件（苏州）有限公司", "ECU/HCU 子总成", "ECU-ESP9-001,HCU-ESP9-001,PUMP-PISTON,SENSOR-PRS-HP", true, "刘工", "0512-88880001", "supplier@example.invalid");
        var infineon = Supplier.Create(Ulid.NewUlid(), "SUP-INFINEON", "英飞凌科技（无锡）有限公司", "汽车级 MCU/驱动 IC", "MCU-TC3X7,DRV-TLE9", true, "陈工", "0510-88880002", "supplier@example.invalid");
        var eto = Supplier.Create(Ulid.NewUlid(), "SUP-ETO", "苏州 ETO 电磁阀有限公司", "电磁阀", "VALVE-SOL-NO,VALVE-SOL-NC", true, "周工", "0512-88880003", "supplier@example.invalid");
        var nok = Supplier.Create(Ulid.NewUlid(), "SUP-NOK", "NOK 密封系统（天津）有限公司", "橡胶密封件", "SEAL-ORING-18,SEAL-ORING-12,SEAL-ORING-8", false, "赵工", "022-88880004", "supplier@example.invalid");
        bosch.UpdateScore(96); infineon.UpdateScore(92); eto.UpdateScore(78); nok.UpdateScore(65);
        foreach (var supplier in new[] { bosch, infineon, eto, nok }) { supplier.IsoCertification = "IATF 16949 / ISO 14001"; supplier.IsoExpiryDate = now.AddMonths(10); }
        return [bosch, infineon, eto, nok];
    }

    private static IEnumerable<SupplierScoreCard> CreateScoreCards(List<Supplier> suppliers)
    {
        var values = new[] { (96d, 98d, 95d, 100d, 88d), (92d, 94d, 90d, 96d, 85d), (78d, 88d, 75d, 90d, 82d), (65d, 80d, 60d, 75d, 78d) };
        return suppliers.Select((s, i) => SupplierScoreCard.Create(Ulid.NewUlid(), s.Id, s.SupplierCode, "2026-Q3", values[i].Item1, "来料合格 99/100 批", values[i].Item2, "准时交付 49/50 批", values[i].Item3, "平均 3.2 天关闭", values[i].Item4, "PPAP 通过 100%", values[i].Item5, "同类供应商中位数", "sqe"));
    }

    private static IEnumerable<PpapDocument> CreatePpapDocuments(List<Supplier> suppliers, DateTimeOffset now)
    {
        var docs = new List<PpapDocument>();
        foreach (var (supplier, code, name) in new[] { (suppliers[0], "ECU-ESP9-001", "ECU 电子控制单元 V3"), (suppliers[1], "MCU-TC3X7", "TC3x7 主控 MCU"), (suppliers[2], "VALVE-SOL-NO", "常开电磁阀 2/2 路") })
        {
            var doc = PpapDocument.Create(Ulid.NewUlid(), supplier.Id, supplier.SupplierCode, code, name, "sqe", 3, now.AddMonths(8));
            doc.Submit(); doc.Approve("sqe"); docs.Add(doc);
        }
        return docs;
    }

    private static IEnumerable<CriticalSupplierSetting> CreateCriticalSupplierSettings()
    {
        return [
            CriticalSupplierSetting.Create(Ulid.NewUlid(), "MCU-TC3X7", "TC3x7 主控 MCU", 3),
            CriticalSupplierSetting.Create(Ulid.NewUlid(), "VALVE-SOL-NO", "常开电磁阀 2/2 路", 3),
            CriticalSupplierSetting.Create(Ulid.NewUlid(), "SENSOR-PRS-HP", "高压压力传感器 0-25MPa", 3),
        ];
    }

    private static List<TraceabilityLink> CreateTraceability(ProductionOrder order, DateTimeOffset now)
    {
        var serial = "ESP9-20260824-0001";
        var links = new List<TraceabilityLink>();
        var previous = string.Empty;
        foreach (var (level, component, material) in new[]
        {
            (TraceabilityLevel.Vehicle, "ESP9-20260824-0001", "VIN-DEMO-LYV1234567890123"),
            (TraceabilityLevel.Assembly, "ESP9-20260824-0001", "BAT-ECU-001"),
            (TraceabilityLevel.Component, "BAT-ECU-001", "BAT-PCB-001"),
            (TraceabilityLevel.Material, "BAT-PCB-001", "BAT-AL-6061-001"),
        })
        {
            var link = TraceabilityLink.Create(order.Id, level, serial, component, material, previous, now.AddDays(-1).AddMinutes(links.Count));
            links.Add(link); previous = link.Hash;
        }
        return links;
    }

    private static IEnumerable<MaterialBinding> CreateBindings(ProductionOrder order, Ulid ecuBatchId, Ulid hcuBatchId)
    {
        return [
            MaterialBinding.Create(order.Id, ecuBatchId, "ECU-ESP9-001", "BAT-ECU-001", "ESP9-20260824-0001", 1, true, "operator-031"),
            MaterialBinding.Create(order.Id, hcuBatchId, "HCU-ESP9-001", "BAT-HCU-001", "ESP9-20260824-0001", 1, true, "operator-031"),
        ];
    }

    private static IEnumerable<MaterialConsumption> CreateConsumptions(ProductionOrder order, DateTimeOffset now)
    {
        return [
            MaterialConsumption.Create(order.Id, order.OrderNumber, "ECU-ESP9-001", "ECU 电子控制单元 V3", 118, 118, 118, "PCS", true),
            MaterialConsumption.Create(order.Id, order.OrderNumber, "HCU-ESP9-001", "HCU 液压控制单元 V2", 118, 118, 118, "PCS", true),
            MaterialConsumption.Create(order.Id, order.OrderNumber, "SEAL-ORING-18", "O 型密封圈 18×2.5 NBR", 118, 120, 126, "PCS", false),
        ];
    }

    private static JitPullSignal CreateJitSignal(ProductionOrder order)
    {
        return JitPullSignal.Create(order.Id, order.OrderNumber, "SENSOR-PRS-LP", "低压压力传感器 0-5MPa", 48, "PCS", "STN-04");
    }

    private static (List<ControlledDocument> Documents, List<DocumentVersion> Versions) CreateDocuments(DateTimeOffset now)
    {
        var document = ControlledDocument.Create(Ulid.NewUlid(), "DOC-DEMO-SOP-001", DocumentType.Sop, "ESP-9.0 总成线标准作业指导书", "ST1-ST7");
        var version = DocumentVersion.CreateDraft(Ulid.NewUlid(), document.Id, "v1.0", "demo/DOC-DEMO-SOP-001-v1.0.pdf", "ESP-9.0-SOP-v1.0.pdf", 245760, "application/pdf", "演示受控文档");
        version.SubmitForApproval("manager"); version.Approve("manager"); document.SetCurrentVersion(version.Id);
        return ([document], [version]);
    }

}
