# IATF 16949 条款覆盖矩阵

> **项目：** 博世 ESP® 制动系统 MES（AutoMES）
> **版本：** v1.0 — 2026-07-07
> **目标：** 覆盖 IATF 16949:2016 核心条款对 MES 系统的要求，提供审核证据引用

---

## 综述

AutoMES 系统覆盖 IATF 16949:2016 以下 6 个关键条款。每个条款通过一个或多个 MES 模块实现合规，本文档提供条款要求、实现方式、代码引用和测试证据的完整追溯。

| 条款 | 标题 | 覆盖模块 | 实现状态 |
|------|------|----------|---------|
| 8.5.1.4 | 作业准备验证 | M01+M07 | ✅ 已实现 |
| 8.5.1.5 | 全面生产维护（TPM） | M05 | ✅ 已实现 |
| 8.5.6.1.1 | 过程参数监控 | M03+M05 | ✅ 已实现 |
| 8.6.1 | 产品放行 | M03 | ✅ 已实现 |
| 8.7.1.5 | 返工产品可追溯 | M04 | ✅ 已实现 |
| 9.1.1.1 | 统计过程控制（SPC） | M03 | ✅ 已实现 |

---

## 8.5.1.4 — 作业准备验证（Job Setup Verification）

### 条款要求

> 组织应验证作业准备，当发生作业准备（如换型、班次变更）时，应进行首件检验。验证方法应保留形成文件的信息。

### MES 覆盖

**对应模块：** M01（生产工单管理）+ M07（工艺管理）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| 换型/班次后强制首件检验 | `FirstArticleInspection` 模型，班次首件/换型首件/设备维修后三种类型 | `src/MesAdmin.Domain/Models/FirstArticleInspection.cs` |
| 控制计划逐项检验 | `InspectionItem` 特性列表含标准值、上下限、实测值、合格判定 | `src/MesAdmin.Domain/Models/FirstArticleInspection.cs:InspectionItem` |
| 防错三重校验（物料+BOM+设备参数） | `TripleCheckService` 三步骤验证：物料扫码→BOM 比对→设备参数比对 | `src/MesAdmin.Application/Features/Routing/TripleCheckService.cs` |
| 工艺路线版本管理 | `Routing` 模型 + ECO 状态机（Draft→Pending→Approved→Released→Superseded） | `src/MesAdmin.Domain/Models/Routing.cs` |
| BOM 版本校验 | 工单创建时校验 BOM 版本 + 产品编码 | `src/MesAdmin.Application/Features/ProductionOrders/` |
| 不合格锁定工单 | 首件检验不合格时工单无法继续 | `FirstArticleInspection.Complete()` → `InspectionStatus.Failed` |

### 关键代码路径

```csharp
// 首件检验核心逻辑
public class FirstArticleInspection
{
    public void Start() → InspectionStatus.InProgress
    public void Complete(inspectorId, allPassed) → Passed / Failed
}

// 防错三重校验
public class TripleCheckService
{
    public async Task<TripleCheckResult> VerifyAsync(orderId, stationId)
    {
        // 步骤1：物料扫码校验
        // 步骤2：BOM 比对
        // 步骤3：设备参数比对
    }
}
```

### 测试证据

- 首件检验流程测试：`T1.5`
- 三重校验 E2E 测试：158 测试全通过（T3.3）
- ECO 审批流程测试：含状态机转换、版本唯一约束（T3.4）

---

## 8.5.1.5 — 全面生产维护（TPM）

### 条款要求

> 组织应制定、实施并维护一个全面生产维护系统，以预防设备失效和过程变异。维护目标应形成文件并定期评审。

### MES 覆盖

**对应模块：** M05（设备管理 TPM + OEE）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| 预防性维护计划 | `MaintenancePlan` 模型（CycleBased 循环计数 / TimeBased 时间间隔两种触发模式） | `src/MesAdmin.Domain/Models/MaintenancePlan.cs` |
| 维护工单自动生成 | `PreventiveMaintenanceService` 后台服务定期扫描 OEE 数据，触发维护工单 | `src/MesAdmin.Infrastructure/RealTime/PreventiveMaintenanceService.cs` |
| 拧紧机每 10 万次标定 | `MaintenancePlan.IsCycleOverdue(currentCycleCount)` | `MaintenancePlan.cs:IsCycleOverdue` |
| 液压台每月密封件更换 | `MaintenancePlan.IsTimeOverdue()` | `MaintenancePlan.cs:IsTimeOverdue` |
| 备件管理 | `SparePart` CRUD + 库存盘点/补货/消耗 + 采购申请审批流 | `src/MesAdmin.Domain/Models/SparePart.cs` |
| OEE 监控（设备综合效率） | R3 管道实时计算 OEE = 可用率 × 性能率 × 良品率，SignalR 推送 | `src/MesAdmin.Infrastructure/RealTime/OeeReactivePipeline.cs` |
| 设备状态实时监控 | 8 台核心设备 × 100Hz PLC 数据 → Channel → R3 → SignalR | `src/MesAdmin.Infrastructure/Plc/PlcDataAcquisitionPipeline.cs` |
| 维护目标记录 | OEE 目标值 85%~92%（S≥85% / A 70-85% / B<70%） | `OeeRecord.Compute()` + `Equipment.OeeTarget` |

### 测试证据

- TPM 预防性维护集成测试：19 端到端（T2.17）
- 备件管理：32 单元 + 20 集成测试（T2.18）
- OEE 计算测试：钳制边界 + 等级判定（`OeeComputationTests.cs`）
- PLC 断连混沌测试：6 场景（T4.12）

---

## 8.5.6.1.1 — 过程参数监控

### 条款要求

> 组织应对影响产品符合性或过程变异的参数进行监控和测量。应保持形成文件的信息，包括过程参数记录和异常处理。

### MES 覆盖

**对应模块：** M03（质量管理）+ M05（设备管理）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| 过程参数实时采集 | PLC 100Hz 采集 8 设备过程参数（扭矩/压力/泄漏率/CAN 通信等） | `src/MesAdmin.Infrastructure/Plc/PlcDataAcquisitionPipeline.cs` |
| 31 工序参数模板 | ESP 默认 31 工序参数定义（含规格上下限、单位、测量工具） | `src/MesAdmin.Api/Features/Routing/EspDefaultRouting.cs` |
| 过程参数记录 | `RoutingOperation.ParameterTemplates` JSONB 存储，每次操作记录实测值 | `src/MesAdmin.Domain/Models/Routing.cs:RoutingOperation` |
| 参数超差报警 | Andon 三级上报（L1 声光→L2 班组长→L3 生产经理） | `src/MesAdmin.Infrastructure/RealTime/AndonReactivePipeline.cs` |
| 扭矩超差检测 | M6（22±1Nm/180°±5°）/ M8（45±2Nm/270°±10°）实时判定 | `src/MesAdmin.Domain/Models/AndonEvent.cs` |
| 泄漏率超标检测 | ≤0.5 CC/hr 阈值，在线液压测试 R3 管道 | `src/MesAdmin.Infrastructure/RealTime/HydraulicTestReactivePipeline.cs` |
| 过程参数审计追踪 | 每条操作记录操作员工号/设备号/开始结束时间/过程参数 | `WorkOrderOperation.Parameters` JSONB（T1.7） |

### 关键代码路径

```csharp
// 工序执行记录（含过程参数）
modelBuilder.Entity<WorkOrderOperation>(b =>
{
    b.OwnsMany(o => o.Parameters, p =>
    {
        p.ToJson(); // JSONB 存储过程参数
    });
});

// 在线液压测试管道
HydraulicTestReactivePipeline
    → 12 路电磁阀逐一测试
    → 建压/保压/泄压 3 周期
    → 泄漏率 ≤0.5CC/hr 判定
    → 不合格自动锁设备
```

---

## 8.6.1 — 产品放行（Product Release）

### 条款要求

> 组织应在适当阶段实施策划的安排，以验证产品要求已得到满足。除非得到有关授权人员的批准，否则在策划的安排已圆满完成之前，不应向顾客放行产品。

### MES 覆盖

**对应模块：** M03（质量管理 — 检验放行）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| IQC 来料检验放行 | 物料入库前 AQL 抽样检验，不合格锁定批次 + 自动创建 NCR | `src/MesAdmin.Domain/Models/QualityRecord.cs:CreateIqc` |
| IPQC 过程巡检放行 | 每 50 件抽 5（拧紧）/ 每 100 件抽 3（液压），不合格锁定工单 | `src/MesAdmin.Domain/Models/QualityRecord.cs:CreateIpqc` |
| 首件检验放行 | 每班次/换型后强制，全项合格方可批量生产 | `FirstArticleInspection.Complete()` |
| 100% 在线功能测试放行 | 12 路电磁阀 + 建压/保压/泄压全项通过才放行 | `HydraulicTestReactivePipeline` |
| 完工确认放行 | 质量工程师审核 → 成品入库 → 追溯标签打印 | `CompleteOrderCommand` + `GoodsReceipt` |
| 让步接收流程 | 不合格品 MRB 评审后让步接收（偏差许可） | `NonConformanceReport.SetDisposition(Concession)` |
| AQL 抽样方案 | IQC 时记录 AQL 等级、Ac/Re 判定数 | `QualityRecord.AqlScheme / AcceptNumber / RejectNumber` |

### 关键代码路径

```csharp
// 质量检验状态机
public class QualityRecord
{
    public InspectionVerdict Verdict { get; set; } // Pending → Passed/Failed/ConditionalPass
    public void Complete()
    {
        DefectCount = Characteristics.Count(c => c.IsFailed);
        Verdict = DefectCount >= RejectNumber ? InspectionVerdict.Failed
               : DefectCount > 0 ? InspectionVerdict.ConditionalPass
               : InspectionVerdict.Passed;
    }
}

// 完工确认
public class CompleteOrderHandler
{
    // 质量工程师审核放行
    // 成品入库 + GoodsReceipt
    // 追溯标签打印
}
```

---

## 8.7.1.5 — 返工产品可追溯

### 条款要求

> 组织应保留形成文件的信息，包括返工产品的处置、返工产品的追溯信息（如数量、处置日期、可追溯信息等）。

### MES 覆盖

**对应模块：** M04（全链路追溯）+ M03（NCR 不合格品处置）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| 4 级追溯模型 | L1 VIN→L2 ESP S/N→L3 零部件→L4 原材料，全链路双向 | `src/MesAdmin.Domain/Models/TraceabilityLink.cs` |
| 返工产品追溯 | NCR 处置方式含 Rework/Repair，处置记录关联追溯链 | `NonConformanceReport.SetDisposition(Rework/Repair)` |
| 哈希链防篡改 | SHA-256 链式哈希，任何字段篡改导致后续校验失败 | `TraceabilityLink.ComputeHash()` + `VerifyHash()` |
| 批次数/处置日期记录 | NCR 含 DefectQuantity / DispositionDeadline / ClosedAt | `NonConformanceReport` |
| 返工后重检 | 返工品重新检验 → 合格回线（重新进入 QualityRecord 流程） | `QualityRecord.Complete()` |
| 正向追溯查询 | VIN→ESP S/N→工单→HCU S/N→阀体批次→供应商，≤30s | `src/MesAdmin.Api/Features/Traceability/Forward/` |
| 反向追溯查询 | 原材料批次→所有总成 S/N→所有 VIN，≤60s | `src/MesAdmin.Api/Features/Traceability/Reverse/` |

### 关键代码路径

```csharp
// 哈希链验证
public class TraceabilityLink
{
    public string ComputeHash()
    {
        var payload = $"{PreviousHash}|{Id}|{OrderId}|...";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
    public bool VerifyHash() => Hash == ComputeHash();
}

// NCR 处置
public class NonConformanceReport
{
    public NcrDisposition Disposition { get; set; }
    // Rework = 2, Repair = 3, Scrap = 4, ReturnToSupplier = 5
}
```

### 测试证据

- 追溯链哈希完整性测试：含篡改检测、链式验证（T1.21）
- 双向追溯性能测试：10 基准（T4.8）

---

## 9.1.1.1 — 统计过程控制（SPC）

### 条款要求

> 组织应保持形成文件的信息，证明已确定统计工具的使用，并验证其正确应用。组织应使用适当的统计方法验证过程能力。

### MES 覆盖

**对应模块：** M03（SPC 质量管理）

**实现机制：**

| 要求 | MES 实现 | 文件引用 |
|------|----------|----------|
| 控制图（X̄-R） | SpcCalculator 实时计算 X̄-R 控制限 + Cpk/Ppk | `src/MesAdmin.Domain/Models/SpcCalculator.cs` |
| 过程能力指数 Cpk | Cpk = min(USL-μ, μ-LSL) / 3σ，含 ASTM STP 15D 常数表 | `SpcCalculator.ComputeCpk()` |
| Western Electric 判异规则 | 规则 1-5,7,8 已实现（一点出界/连续 5 点上升等） | `SpcCalculator.CheckWesternElectricRules()` |
| SPC 样本采集 | 子组 n=5，stackalloc 零分配计算均值/极差/标准差 | `SpcSample.Create()` |
| 自动告警 | RuleAlert 自动检测判异，关联 Andon 报警 | `SpcRuleAlert` |
| 控制限实时更新 | 每新子组更新 X̄-R 控制限 | `SpcCalculator.UpdateControlLimits()` |
| 过程能力报告 | Cpk 日报/周报/月报 PDF 自动推送 | `QualityReportService` |
| 检验计划 | `InspectionPlan` 含 AQL/抽样频率/控制限/SPC 启用标志 | `src/MesAdmin.Domain/Models/InspectionPlan.cs` |

### 关键代码路径

```csharp
// SPC 核心计算（零分配）
public static class SpcCalculator
{
    public static (double Cl, double Ucl, double Lcl) ComputeXBarLimits(
        ReadOnlySpan<double> means, int n, int minSubgroups = 20)
    {
        // stackalloc 零分配
        Span<double> sorted = stackalloc double[means.Length];
        // 均值极差 → 控制限
        // 使用 A2/d2/D3/D4 常数表
    }

    public static double ComputeCpk(double mean, double stdDev, double usl, double lsl)
    {
        var cpu = (usl - mean) / (3 * stdDev);
        var cpl = (mean - lsl) / (3 * stdDev);
        return Math.Min(cpu, cpl);
    }

    public static List<SpcRuleAlert> CheckWesternElectricRules(
        double mean, double cl, double ucl, double lcl, ...)
    {
        // Rule 1: 一点超出控制限
        // Rule 2: 连续 3 点中的 2 点落在 2σ 外（同侧）
        // Rule 3: 连续 5 点中的 4 点落在 1σ 外（同侧）
        // ...
    }
}
```

### 测试证据

- SPC Cpk/Ppk/X̄-R 控制限计算测试：含边界条件、零分配验证（T2.5）
- Western Electric 判异规则测试：规则 1-5,7,8 覆盖（T2.5）
- 零分配基准测试：`ZeroAllocationBenchmarks.cs` → SpcCalculator stackalloc

---

## 覆盖矩阵总结

| 条款 | 模块 | 关键文件 | 测试覆盖 | 状态 |
|------|------|----------|----------|------|
| 8.5.1.4 作业准备验证 | M01+M07 | `FirstArticleInspection.cs`, `TripleCheckService.cs`, `Routing.cs` | 158+ (T3.3) | ✅ |
| 8.5.1.5 TPM | M05 | `MaintenancePlan.cs`, `PreventiveMaintenanceService.cs`, `OeeReactivePipeline.cs` | 19+ (T2.17) | ✅ |
| 8.5.6.1.1 过程参数 | M03+M05 | `PlcDataAcquisitionPipeline.cs`, `RoutingOperation`, `AndonReactivePipeline.cs` | 19+ (T2.17) | ✅ |
| 8.6.1 产品放行 | M03 | `QualityRecord.cs`, `CompleteOrderHandler.cs`, `FirstArticleInspection.cs` | 217 total | ✅ |
| 8.7.1.5 返工追溯 | M04 | `TraceabilityLink.cs`, `NonConformanceReport.cs` | 10 (T4.8) | ✅ |
| 9.1.1.1 SPC | M03 | `SpcCalculator.cs`, `SpcSample.cs`, `InspectionPlan.cs`, `SpcRuleAlert.cs` | 14+ (T2.5) | ✅ |

### 扩展条款（额外覆盖）

AutoMES 还覆盖以下 IATF 16949 扩展条款：

| 条款 | 覆盖 | 说明 |
|------|------|------|
| 8.5.1.2 标准化作业 | ✅ | M07 工艺路线定义 31 工序 × 7 站标准化作业 |
| 8.5.1.3 作业准备指导书 | ✅ | `EspDefaultRouting.CreateDefault()` 提供完整作业指导 |
| 8.5.1.6 工装管理 | ✅ | RoutingOperation.FixtureCode/FixtureName 工装夹具管理 |
| 8.5.6.1 测量技术 | ✅ | InspectionPlan.MeasurementTool/InspectionItem.InspectionTool |
| 8.7.1.1 不合格品隔离 | ✅ | NCR 状态机含隔离区管理 |
| 9.1.1.2 统计工具确定 | ✅ | SPC 工具选择：X̄-R 图 + Cpk + Western Electric 规则 |
| 9.1.2.1 顾客满意度 | ✅ | 8D 报告闭环 + PPM 统计 |
| 10.2.3 8D 纠正措施 | ✅ | EightDReport D1-D8 完整流程 |

### 补充说明

**数据完整性保障：**
- 所有实体主键使用 `Ulid`（可排序 UUID），避免自增 ID 的索引碎片和 Guid 的随机性
- 追溯链使用 SHA-256 哈希链防篡改
- Saga Effect 使用 `AtLeastOnce` 保证幂等性 + `AtMostOnce` 避免副作用重复
- 所有变更通过 ZLogger 结构化日志记录，审计追踪完整

**持续改进机制：**
- 8D/CAR 闭环（D1-D8）：团队→问题描述→围堵→根因分析→纠正措施→验证→预防→总结关闭
- SPC 控制图实时监控过程变异，提前预警
- Cpk 日报/周报/月报自动推送管理层
- Andon 三级上报确保异常及时响应
