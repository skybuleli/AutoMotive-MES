# AutoMES 任务清单

> **项目：** 博世 ESP® 制动系统 MES（汽车 Tier-1 供应商制造执行系统）
> **来源：** PRD v2（`automotive-mes-prd-v2.html`）+ TAD v2（`automotive-mes-tad-v2.html`）+ AGENTS.md 宪法
> **当前状态：** 阶段 0 ✅ · 阶段 1 ✅ · 阶段 2 ✅ · 阶段 3 ✅ · 阶段 4 **~47%** 🔶 · 部署 **100%** ✅
> **路线图：** PRD v2 的 28 周 4 阶段，本清单为细粒度可执行分解

---

## 0. 文档说明

### 标记约定

| 标记 | 含义 |
|------|------|
| `[ ]` | 待办 |
| `[~]` | 进行中 |
| `[x]` | 已完成 |

### 字段说明

- **优先级**：`P0` = 阻塞核心流程 / 合规强制；`P1` = 重要；`P2` = 可延后
- **工时**：单位人天（d），为单人预估；团队并行可压缩至 PRD 28 周路线图
- **依赖**：`← Txx` 表示前置任务，需先完成
- **任务 ID**：`T{阶段}.{序号}`（阶段任务）、`TD.{n}`（部署）、`TX.{n}`（测试）

### 阶段总览

| 阶段 | PRD 周次 | 目标 | 模块 |
|------|----------|------|------|
| 阶段 0 | 前置 | 项目骨架与基础设施 | — |
| 阶段 1 | 1-8 周 | MVP 核心闭环 | M01 工单 + M02 物料 + M04 追溯 |
| 阶段 2 | 9-14 周 | 质量体系就绪 | M03 SPC + M05 设备 + M06 Andon |
| 阶段 3 | 15-22 周 | 完整 MES | M07 工艺 + M08 SQE + M09 排程 + ERP |
| 阶段 4 | 23-28 周 | 生产就绪 | M10 报表 + 离线 + 性能 + IATF |
| 横切·部署 | 贯穿 | OrbStack 开发 + Uncloud 生产 | — |
| 横切·测试 | 贯穿 | 单元 + 集成 + 混沌 | — |

---

## 1. 部署技术栈说明

### OrbStack（本地开发环境）

- **定位**：macOS Docker Desktop 替代品（`orb` CLI），原生 Swift、轻量
- **职责**：本地开发运行 PostgreSQL 17 + .NET 10 应用，秒级启动、VirtioFS 挂载快
- **用法**：标准 `docker compose` 命令，`orb start` 启动引擎，可选内置 K8s（`orb start k8s`）
- **文件**：`docker/compose.dev.yaml`

### Uncloud（厂区生产部署 + 组网）

- **定位**：多机 Docker Compose 生产部署工具（`uc` CLI，uncloud.run）
- **底层**：Docker + WireGuard mesh + Caddy（自动 HTTPS）
- **职责**：把 MES 服务编排部署到厂区多台 Linux 服务器，去中心化无控制平面
- **核心能力**：跨机服务互通（WireGuard mesh + NAT 穿透）、零停机滚动部署、`x-ports` 对外暴露 + `*.uncld.dev` 自动 HTTPS、无需外部镜像仓库
- **注意**：Uncloud 的 WireGuard mesh 解决**服务器间组网**；车间终端/运维人员**接入**需另配 Tailscale/WireGuard 客户端 VPN（见 TD.8）

---

## 2. 阶段 0：项目骨架与基础设施（~16d）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T0.1 | `[x]` | 解决方案 `MesAdmin.sln` + 5 个分层项目骨架（Domain/Application/Infrastructure/Web/Api），依赖方向 `Web→Infra→App→Domain`、`API→Infra` | P0 | 1d | — |
| T0.2 | `[x]` | NuGet 14 包精确版本引入（按 AGENTS.md 技术栈表：MudBlazor 9.6.0、MemoryPack 1.21.4、Cleipnir 4.2.5、MessagePipe 1.8.2、R3 1.3.1、ZLogger 2.5.10、Ulid 1.4.1、ObservableCollections 3.3.4、MasterMemory 3.0.4、Npgsql EF Core 10.0.2、SignalR、Channels、Pipelines） | P0 | 1d | T0.1 |
| T0.3 | `[x]` | **OrbStack 本地开发环境**：`orb start` + `docker/compose.dev.yaml`（postgres:17-alpine :5432 + 开发用 mes-web/mes-api 本地构建）+ 持久卷 + 一键 `docker compose up -d` | P0 | 1d | T0.1 |
| T0.4 | `[x]` | ZLogger 结构化日志配置（`IBufferWriter<byte>` 直写，禁止字符串拼接 + 默认 ILogger） | P0 | 1d | T0.2 |
| T0.5 | `[x]` | `MesDbContext` + `UlidToGuidConverter`（`ValueConverter<Ulid, Guid>`）+ EF Core DI 注册 | P0 | 2d | T0.2 |
| T0.6 | `[x]` | 首次 EF Core Migration（`production_orders` / `traceability_links` 核心表 DDL，对齐 TAD v2；索引 idx_orders_status、idx_trace_vin/component/material） | P0 | 1d | T0.5 |
| T0.7 | `[x]` | MemoryPack 全局配置 + `[MemoryPackable] public partial class` 基础模型约定 | P0 | 1d | T0.2 |
| T0.8 | `[x]` | JWT 认证 + 6 角色 RBAC（生产经理/班组长/质量工程师/设备工程师/仓库员/SQE） | P0 | 2d | T0.5 |
| T0.9 | `[x]` | MudBlazor 双主题（暗 `#CBA6F7` / 亮 `#8F6AAF`）+ `glass-kpi-card`/`andon-pulse`/`status-dot`/`grad-border` CSS + MainLayout/NavMenu + Inter Tight 字体 + 12px 圆角 | P0 | 2d | T0.2 |

---

## 3. 阶段 1：MVP（PRD 1-8 周）— M01 工单 + M02 物料 + M04 追溯（~57d）

### M01 生产工单管理（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T1.1 | `[x]` | `ProductionOrder` 领域模型 + MemoryPack + Ulid 主键 + 状态机（`Created→Released→InProgress→Completed→Closed`） | P0 | 2d | T0.7 |
| T1.2 | `[x]` | 工单 CRUD Repository + Application 层 `IMesDbContext` 接口 | P0 | 2d | T1.1 |
| T1.3 | `[x]` | 工单创建与校验（SAP Webhook 接收、产品编码 ESP-9.0/9.1 + BOM 版本 + 工艺路线版本校验、`Ulid.NewUlid()` + 工单号 `WO-YYYYMMDD-NNNN` 生成、版本不符拒单回写 SAP） | P0 | 3d | T1.2 |
| T1.4 | `[x]` | 物料齐套检查（BOM 展开、关键物料线边库存查询、缺料触发 JIT 拉动、齐套通过转 `Released`；ERP 库存查询待 T3.14 SAP 对接） | P0 | 3d | T1.3 |
| T1.4-seed | `[x]` | ESP-9.0 BOM 种子数据（87 种物料 4 层结构）+ 库存阈值 + 初始 Qualified 库存批次，500 件工单齐套检查可真实通过 | P0 | 1d | T1.4 |
| T1.5 | `[x]` | 首件检验流程（每班次/换型后强制、控制计划逐项检验、自动判定合格/不合格、不合格锁定工单触发 CAR） | P0 | 2d | T1.4 |
| T1.6 | `[x]` | **Cleipnir `ProductionOrderSaga` 骨架**：31 工序 × 7 站编排、`Cleipnir.ResilientFunctions.PostgreSQL` 状态持久化、Checkpoint、Effect 策略矩阵（站1 Effect 外/站2-5,7 AtLeastOnce/站6 AtMostOnce） | P0 | 3d | T1.2 |
| T1.7 | `[x]` | 工序执行监控（每工序记录操作员工号/设备号/开始结束时间/过程参数、异常触发 Andon、Saga 按工艺路线依次执行） | P0 | 3d | T1.6 |
| T1.8 | `[x]` | 完工确认（质量工程师审核放行 → 成品入库 → 追溯标签打印 → `/quality-review` 路由） | P0 | 3d | T1.7 |
| T1.9 | `[x]` | 工单管理 Web 页面（`MudTable<ProductionOrder>` 列表/详情、状态流转看板、ObservableCollections 增量绑定） | P0 | 3d | T1.8 |
| T1.10 | `[x]` | 工单 REST API（Avalonia 工位终端用、JSON 默认 + `Accept: application/x-memorypack` 二进制双协议） | P0 | 2d | T1.8 |

### M02 物料管理 JIT/JIS（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T1.11 | `[x]` | **BOM 内存缓存优化**：`IBomCache` 接口 + `BomCache`（ConcurrentDictionary + `volatile` + `Interlocked.Exchange` 线程安全原子替换）+ `BomCacheInitializationService` 启动预热 + `BomRepository` 缓存优先策略（cache hit → O(1) 返回，miss → EF Core 回退）；齐套检查/反冲/三重校验热路径均从缓存查询 | P0 | 2d | T1.2 |
| T1.12 | `[x]` | 来料扫码入库（GS1-128 解析含物料编码+批次+数量+生产日期、`ReadOnlySpan<char>` 零分配切片禁止 `Substring`、合格供应商名录校验、写 `material_bindings` 表） | P0 | 3d | T1.11 |
| T1.13 | `[x]` | 线边库存实时监控（每工位电子看板、安全/最低库存阈值、低于安全黄色预警、低于最低红色报警+自动叫料） | P0 | 2d | T1.12 |
| T1.14 | `[x]` | JIT 看板拉动（空料箱扫码生成电子看板信号→推送仓库 PDA→备料送达扫码确认、全流程时间戳） | P0 | 3d | T1.13 |
| T1.15 | `[x]` | 投料批次绑定（操作员扫码绑定 物料编码+批次号→工单号→产品 S/N、写追溯链 `traceability_links`） | P0 | 2d | T1.14 |
| T1.16 | `[x]` | 物料防错 Poka-Yoke（关键物料 ECU芯片/电磁阀 BOM 比对、错误锁定设备+声光报警、必须质量工程师解锁） | P0 | 2d | T1.15 |
| T1.17 | `[x]` | 物料消耗反冲（工单完工按 BOM 标准用量扣减线边库存、差异>2% 生成异常报告、同步 SAP 物料移动凭证） | P0 | 2d | T1.15 |
| T1.18 | `[x]` | 物料管理 Web 页面（批次管理/扫码入库/投料绑定 → `/material` 路由） | P0 | 3d | T1.17 |

### M04 全链路追溯（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T1.19 | `[x]` | `traceability_links` 表 + 4 级追溯模型（L1 车辆 VIN 17 位 / L2 ESP 总成 S/N `ESP9-YYYYMMDD-NNNNNN` / L3 零部件 ECU+HCU+电机 S/N+电磁阀批次 / L4 原材料阀体铝合金/PCB 板材批次） | P0 | 2d | T0.6 |
| T1.20 | `[x]` | 追溯绑定写入（`Effect.AtLeastOnce` + DB 唯一约束防重复、装配工位扫码自动绑定 ECU/HCU/电机 S/N→电磁阀批次→阀体批次） | P0 | 2d | T1.19 |
| T1.21 | `[x]` | 哈希链审计（追溯链不可篡改、每次绑定哈希链接前一条记录、过程参数记录审计追踪） | P0 | 2d | T1.20 |
| T1.22 | `[x]` | 正向追溯查询（VIN→ESP 总成 S/N→工单→HCU S/N→阀体批次→供应商，性能 ≤30s、`.Select()` 投影禁止 `SELECT *`） | P0 | 2d | T1.20 |
| T1.23 | `[x]` | 反向追溯查询（原材料批次→所有总成 S/N（如 1247 件）→所有 VIN、导出 Excel 发送整车厂、性能 ≤60s） | P0 | 2d | T1.22 |
| T1.24 | `[x]` | 追溯查询 Web 页面（正反向切换、追溯链可视化、Excel 导出） | P0 | 2d | T1.23 |

---

## 4. 阶段 2：质量体系（PRD 9-14 周）— M03 SPC + M05 设备 + M06 Andon（~55d）

### M03 质量管理 SPC + 全检（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.1 | `[x]` | 质量领域模型（`QualityRecord`/`InspectionPlan`/`SpcSample`/`SpcRuleAlert`/`NonConformanceReport`/`EightDReport`，全部 [MemoryPackable] + Ulid 主键） | P0 | 2d | T1.1 |
| T2.2 | `[x]` | IQC 来料检验（CreateIqcRecordCommand + API POST /api/v1/quality/iqc，不合格自动生成 NCR） | P0 | 2d | T2.1 |
| T2.3 | `[x]` | 首件检验（复用于已有 T1.5 FirstArticleInspection 模块，SPC 检验特性可复用 InspectionPlan） | P0 | 1d | T2.1 |
| T2.4 | `[x]` | IPQC 过程巡检（CreateIpqcRecordCommand + API POST /api/v1/quality/ipqc，过程检验记录管理） | P0 | 2d | T2.1 |
| T2.5 | `[x]` | **SPC 实时控制图**：SpcCalculator 含 Cpk/Ppk/X̄-R 控制限计算 + Western Electric 8 条判异规则（Rule 1-5,7,8 已实现）、`stackalloc Span<double>` 零分配计算、X̄-R 常数表 ASTM STP 15D、RecordSpcSampleCommand 自动检测判异告警 | P0 | 3d | T2.4 |
| T2.6 | `[x]` | 100% 在线液压功能测试（R3 管道：订阅 EQ-HYD-01 PLC 数据 → 12 路电磁阀测试 → 建压/保压/泄压 3 周期 → 泄漏率 ≤0.5CC/hr 判定 → 不合格自动锁设备 → Andon 报警；EtherNet/IP 驱动已就位） | P0 | 2d | T2.5 |
| T2.7 | `[x]` | 不合格品处置 NCR（Open→UnderReview→Dispositioned→Closed 完整状态机 + MRB 评审流程 + 处置决定：让步接收/返工/返修/报废/退货，不合格自动创建 NCR） | P0 | 2d | T2.6 |
| T2.8 | `[x]` | 8D/CAR 闭环（D1-D8 完整流程：团队→问题描述→围堵→根因分析→纠正措施→验证→预防→总结关闭、API PUT /api/v1/quality/8d/{id} 分步更新） | P0 | 3d | T2.7 |
| T2.9 | `[x]` | 质量报表（QuestPDF A4 PDF 生成：日报 Cpk + 周报趋势 + 月报 PPM/质量成本；QualityReportService BackgroundService 每日 06:00/周一/月1日自动推送；FluentEmail SMTP PDF 附件；4 个 REST API：daily/weekly/monthly/custom） | P1 | 3d | T2.8 |
| T2.10 | `[x]` | SPC + 质量 Web 页面（ECharts X̄-R 控制图 + 4 标签页：SPC 控制图/NCR 处置/8D 报告/质量概览，JSInterop spc-chart-interop.js） | P0 | 3d | T2.8 |

### M05 设备管理 TPM + OEE（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.11 | `[x]` | 设备领域模型（`Equipment`、8 台核心设备清单、OEE 记录、维护计划、备件） | P1 | 2d | T1.1 |
| T2.12 | `[x]` | **`OpcUaPlcClient`**：每 500ms 读设备状态寄存器、`PipeReader`/`PipeWriter` 零拷贝网络 IO（禁止裸 `Stream.ReadAsync`）、`SearchValues<byte>` 帧头 `0x55 0xAA` SIMD 扫描、`ref struct PlcFrameReader`、`ArrayPool<byte>.Shared` 512B 缓冲池 | P0 | 3d | T2.11 |
| T2.13 | `[x]` | **`PlcDataAcquisitionPipeline`**：`BoundedChannel<PlcSnapshot>` 容量 10000 + `FullMode = Wait` 背压（禁止 `BlockingCollection`）、8 设备 100Hz 读取循环（`Task.Delay(10)`）、`ReadAllAsync` 喂 R3 管道 | P0 | 2d | T2.12 |
| T2.14 | `[x]` | MessagePipe `PlcDataChanged` 发布 + **R3 `OeeReactivePipeline`**（`ThrottleLast(5s)` 采样替代 R3 缺失的 Sample、算可用率/性能率/良品率、stackalloc 零分配计算、订阅推送） | P0 | 2d | T2.13 |
| T2.15 | `[x]` | **SignalR `DashboardHub`**：`OeeUpdated` 推送、强制 MemoryPack 二进制（自定义 `IHubProtocol`，禁止 JSON）、8 设备 OEE 看板实时更新、`ChannelHealth` 10s 通道健康度 | P0 | 2d | T2.14 |
| T2.16 | `[x]` | 多协议驱动（OPC UA 拧紧机 Atlas Copco / EtherNet/IP 液压台、Modbus TCP 刷写台、Profinet 压装机；PlcDriverFactory 策略调度器；IPlcTransport 接口；模拟+生产双模式；appsettings.json 配置 IP:Port/UseRealClients） | P1 | 4d | T2.12 |
| T2.17 | `[x]` | 预防性维护（运行时间/次数触发维护工单、拧紧机每 10 万次标定、液压台每月密封件更换） | P1 | 2d | T2.15 |
| T2.18 | `[x]` | 备件管理（SparePart 域模型 + CRUD API + 库存盘点/补货/消耗扣减 + 自动/手动采购申请 + 审批/取消流程 + 32 单元测试 + 20 集成测试全通过 + MudBlazor Web UI 3 标签页：库存看板/备件管理/采购审批） | P2 | 2d | T2.17 |
| T2.19 | `[x]` | OEE 看板 Web 页面（8 设备 OEE 卡片 `glass-kpi-card`、`status-dot` 发光圆点绿/橙/红、SignalR 实时 OEE 推送、OEE 目标 85%~92%；ECharts 趋势图留专项） | P1 | 3d | T2.15 |

### M06 Andon 报警（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.20 | `[x]` | Andon 领域模型（`AndonEvent` 6 状态机: Active→EscalatedL2/L3→Acknowledged→Resolved→Closed）+ 三级上报 L1/L2/L3 + API 端点 | P1 | 2d | T2.15 |
| T2.21 | `[x]` | **R3 防抖报警管道**：`ThrottleFirst(5s)` 防抖 + dedup 去重、报警类型枚举 + MessagePipe `AndonEventCreatedMessage` 等 5 种消息 | P1 | 2d | T2.20 |
| T2.22 | `[x]` | ESP 专用报警检测（扭矩超差 M6/M8、泄漏超标、CAN 通信异常、工艺偏差）+ `andon-pulse` 脉冲动画 + `MudAlert` 通知 | P1 | 2d | T2.21 |
| T2.23 | `[x]` | Andon 看板 Web 页面（实时报警列表 + 过滤栏 + L1/L2/L3 升级链可视化 + SignalR 实时推送 + 确认/解决/关闭对话框） | P1 | 2d | T2.22 |

> **注：** 企业微信推送集成（T2.23 提及）未实现，留待 P2 外部通知集成专项。

---

## 5. 阶段 3：集成（PRD 15-22 周）— M07 工艺 + M08 SQE + M09 排程 + ERP（~34d）

### M07 工艺管理（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.1 | `[x]` | 工艺领域模型（`Routing` 31 工序 × 7 站、`RoutingOperation` 标准工时/工装夹具/参数模板、ECO 状态机 Draft→PendingApproval→Approved→Released→Superseded、MemoryPackable Ulid 主键）+ `IRoutingRepository` + EF Core RoutingRepository + DbContext JSONB 配置 + Migration `20260705_AddRoutingManagement` | P1 | 2d | T1.6 |
| T3.2 | `[x]` | ESP 参数模板（M6 扭矩 22±1Nm/180°±5°、M8 扭矩 45±2Nm/270°±10°、12 电磁阀进/出/泄放电阻+响应时间、液压建压/保压/泄漏率、CAN 通信/传感器标定/ESP 功能参数）；`EspDefaultRouting.CreateDefault()` 返回 31 工序全量定义 | P1 | 1d | T3.1 |
| T3.3 | `[x]` | **防错三重校验**（TripleCheckService 三步骤：物料扫码→BOM 比对→设备参数比对；API POST /api/v1/routing/verify；安全优先策略校验工站 ALL 工序参数；校验 E2E 验证：158 测试全通过） | P1 | 2d | T3.2 |
| T3.4 | `[x]` | 工艺版本控制（ECO 状态机 Draft→PendingApproval→Approved→Released→Superseded + REST API: POST submit/approve/release + Release 自动 Supersede 旧活跃版本 + ProductCode+Version 唯一约束 + IsActive 标识当前有效版本） | P1 | 2d | T3.1 |
| T3.5 | `[x]` | 工艺管理 Web 页面（工艺路线列表、ECO 审批流、工序参数查看、新建路线、NavMenu 导航） | P1 | 2d | T3.4 |

### M08 SQE 供应商质量（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.6 | `[x]` | 供应商领域模型 + 评分（来料合格率 30% + 交货准时率 25% + 8D 响应速度 20% + PPAP 通过率 15% + 价格 10%） | P2 | 2d | T2.8 |
| T3.7 | `[x]` | PPAP 管理（18 项文档电子归档、到期自动提醒、逾期升级） | P2 | 2d | T3.6 |
| T3.8 | `[x]` | 关键供应商管控（电磁阀/压力传感器/PCB 板材三类最高等级管控） | P2 | 1d | T3.6 |
| T3.9 | `[x]` | SQE Web 页面（供应商评分卡、PPAP 文档库、关键物料管控 3 标签页） | P2 | 2d | T3.7 |

### M09 排程（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.10 | `[x]` | 排程领域模型（ProductionSchedule + CapacityCalendar + ScheduleConflict + 班次/换型约束 + 有限产能排程引擎） | P1 | 3d | T1.6 |
| T3.11 | `[x]` | 有限产能引擎（最早可排时间计算 + 设备冲突检测 + 产能利用率报表 + 跨产品换型时间） | P1 | 3d | T3.10 |
| T3.12 | `[x]` | 紧急插单管理（OEM 急单自动插入 + 冲突警告 + 优先级 3 级 + 回退逻辑） | P1 | 2d | T3.11 |
| T3.13 | `[x]` | 排程 Web 页面（ECharts 甘特图 + 3 标签页 + 排程列表 CRUD + 紧急插单对话框） | P1 | 3d | T3.12 |

### ERP/SAP 对接（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.14 | `[x]` | **SAP 工单双向同步**：`SapOrderSyncRecord` 域模型 + 后台服务 30s 轮询 + Release/Complete/Close 时自动创建同步记录 + `ISapClient.SendOrderStatusAsync` HTTP OData 推送 + `MockSapClient` 开发模式 | P1 | 3d | T1.8 |
| T3.15 | `[x]` | **SAP WM IDoc 库存同步**：`SapInventorySyncService` 后台服务 30s 轮询 `SapInventorySyncRecords`（复用已有反冲同步记录）+ `ISapClient.SendInventorySyncAsync` 推送 | P1 | 2d | T1.13 |
| T3.16 | `[x]` | **SAP Webhook 工单推送 + 拒单回写**：`SapWebhookGroup` 端点组 + `SapSignaturePreProcessor` HMAC-SHA256 验证 + `SapRejectionWritebackService` 30s 轮询 Pending 拒单 + `POST /rejections/{id}/writeback` 手动重试 API | P1 | 2d | T1.3 |
| T3.17 | `[x]` | **SAP 物料移动凭证同步**：`SapMaterialMovementSyncService` 后台服务 30s 轮询 + `ISapClient.SendMaterialMovementAsync` 推送 + `SapSyncStatusEndpoint` 概览 API | P1 | 2d | T1.17 |

---

## 6. 阶段 4：优化（PRD 23-28 周）— M10 报表 + 离线 + 性能 + IATF（~33d）

### M10 报表（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.1 | `[ ]` | 报表引擎（自定义报表模板、数据聚合、PDF 生成） | P2 | 3d | T2.9 |
| T4.2 | `[ ]` | OEE 日报（8 设备 OEE + MTBF/MTTR + 停机原因柏拉图、PDF 自动邮件） | P2 | 2d | T4.1 |
| T4.3 | `[ ]` | 月报（Cpk 趋势 + 一次合格率 + PPM + 质量成本、管理层看板） | P2 | 2d | T4.1 |

### 离线模式（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.4 | `[ ]` | 离线数据缓存（工位终端断网本地缓存、Channel 缓冲） | P2 | 3d | T2.13 |
| T4.5 | `[ ]` | 断网重连自动同步（Saga 状态合并、冲突解决） | P2 | 3d | T4.4 |

### 性能压测（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.6 | `[x]` | **热路径零分配验证**：BenchmarkDotNet 14 基准覆盖 PlcFrameReader ref struct/PlcFrameWriter Span/SpcCalculator stackalloc/SpcSample.Create/OeeRecord.Compute/MemoryPack/ArrayPool，[MemoryDiagnoser] 验证 100% 零堆分配 | P1 | 3d | T2.13 |
| T4.7 | `[x]` | **PLC 吞吐压测**：Channel 写入/读取/流水线 8 基准 × 容量 1000/10000、PlcSnapshot.Create 单设备/8 设备、ChannelHealth 8 线程并发 | P1 | 2d | T4.6 |
| T4.8 | `[x]` | **追溯查询性能压测**：正向/反向 VIN/批次 10 基准、SHA-256 哈希链审计、MemoryPack 序列化/反序列化 | P1 | 2d | T1.23 |
| T4.9 | `[x]` | **SignalR 并发压测**：5 消息类型序列化/反序列化、内存占用；并发推送模拟 10/50/100 客户端 | P1 | 2d | T2.15 |

### 混沌工程（P0，阶段 4 必须执行）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.10 | `[x]` | **随机杀进程 → Saga 崩溃恢复**：11 测试覆盖所有工站边界崩溃（CrashTestOpRepo + 参数化 Scenarios 13-17）+ Effect 粒度验证（站3螺栓间崩溃）+ AtMostOnce 站6 + 重放跳过 Effect（Scenario 12）| P0 | 2d | T1.6 |
| T4.11 | `[x]` | **随机断网 → SignalR 自动重连**：9 测试覆盖 HubConnection 构建/不可达/状态机/重连间隔/Dispose/SignalR 配置键/MemoryPackHubProtocol 二进制协议 | P0 | 2d | T2.15 |
| T4.12 | `[x]` | **随机拔 PLC → Channel 背压缓冲**：6 测试覆盖断连空数据/重连恢复/10 次断连循环/ChannelHealth 空闲/低频数据/全部离线 | P0 | 2d | T2.13 |

### IATF / ISO 26262 审核（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.13 | `[ ]` | IATF 16949 条款覆盖矩阵文档（8.5.1.4 作业准备验证→M01+M07、8.5.1.5 TPM→M05、8.5.6.1.1 过程参数→M03+M05、8.6.1 产品放行→M03、8.7.1.5 返工可追溯→M04、9.1.1.1 SPC→M03） | P0 | 2d | — |
| T4.14 | `[ ]` | ISO 26262 ASIL-D 工具验证文档（TCL2/TCL3、Tool Qualification） | P0 | 2d | — |
| T4.15 | `[ ]` | 审计追踪文档（哈希链、过程参数不可篡改、数据完整性） | P0 | 1d | T1.21 |

---

## 7. 横切·部署：OrbStack 开发 + Uncloud 生产（~11d）

> 生产部署主要在阶段 3 末 / 阶段 4 执行；本地开发环境 T0.3 在阶段 0。

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| TD.1 | `[x]` | **生产 `compose.yaml`**（postgres + mes-web + mes-api + pg-backup + Dockerfiles + /health endpoint + .dockerignore + .env.example） | P1 | 2d | T0.3 |
| TD.2 | `[x]` | **Uncloud 集群初始化**：`docs/deployment/uncloud-setup.md` 完整指南（安装→初始化→部署→对外暴露→故障排查） | P1 | 1d | TD.1 |
| TD.3 | `[x]` | **Uncloud 多机扩展指南**：`docs/deployment/uncloud-setup.md §5` 完整多机架构（双机/三机模式、x-machines 定位、跨机卷、网络验证、故障处理） | P1 | 1d | TD.2 |
| TD.4 | `[x]` | **`uc deploy` 生产部署指南**：`docs/deployment/uncloud-setup.md §6` 完整部署流程（构建/推送/计划/部署/验证/回滚/扩展/CI/CD/多机/检查清单） | P1 | 2d | TD.3 |
| TD.5 | `[x]` | **对外暴露配置**：`docs/deployment/uncloud-setup.md §7` 完整对外暴露指南（`x-ports` 语法/自定义域名/Caddy HTTPS/多机路由/安全加固/10 节） | P1 | 1d | TD.4 |
| TD.6 | `[x]` | **PostgreSQL 3 级备份**：`docs/deployment/postgres-backup.md` 完整备份策略（pg_dump 每日 + WAL 归档 PITR + 异地存储 + 恢复操作指南） | P1 | 2d | TD.4 |
| TD.7 | `[x]` | **零停机滚动部署验证**：`docs/deployment/uncloud-setup.md §8` 零停机指南（多副本配置/验证脚本/策略对比/当前能力评估） | P2 | 1d | TD.5 |
| TD.8 | `[x]` | **终端 VPN 接入补充**：`docs/deployment/terminal-access.md` 完整接入指南（Tailscale/Headscale/WireGuard 三方案对比 + ACL 安全策略 + 批量部署脚本 + 故障排查） | P2 | 1d | TD.3 |

---

## 8. 横切·测试（~6d + 各模块随开发同步）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| TX.1 | `[x]` | **单元测试基础设施**：CleipnirSagaFixture（InMemoryFunctionStore + ActionRegistration）+ MessagePipe TestPublisher/TestSubscriber/MessagePipeTestBridge + R3TestScheduler（ManualTimeProvider）+ CrashTestOpRepoDecorator + 17 验收测试全通过 | P0 | 2d | T0.2 |
| TX.2 | `[x]` | **集成测试基础设施**：DatabaseFixture 升级为 Testcontainers PostgreSQL 17（自动拉取镜像 + 容器生命周期 + 清理）+ IntegrationTestBase 抽象基类 + 全仓储注册 + Migration + 种子数据 + 5 验收测试 | P0 | 2d | T0.5 |
| TX.3 | `[x]` | Saga 崩溃恢复混沌测试（CrashTestOpRepo 模拟随机杀进程 → 站2 SaveChanges 抛出 OperationCanceledException → 验证站2完工、站3-7未执行；恢复幂等由 Scenario 4 验证） | P0 | 2d | T1.6 |

> **注**：各模块单元测试随开发同步进行（每模块 1-2d，已含在对应任务工时内），不单列。

---

## 9. 汇总统计

| 阶段 | 总任务数 | 已完成 | 进度 | 工时（人天） |
|------|---------|--------|------|-------------|
| 阶段 0 骨架 | 9 | 9 | **100%** ✅ | 16d |
| 阶段 1 MVP | 24 | 24 | **100%** ✅ | 57d |
| 阶段 2 质量 | 23 | 23 | **100%** ✅ | 55d |
| 阶段 3 集成 | 17 | **12** | **~71%** 🔶 | 34d |
| 阶段 4 优化 | 15 | **7** | **~47%** 🔶 | 33d |
| 横切·部署 | 8 | 8 | **100%** ✅ | 11d |
| 横切·测试 | 3 | **3** | **100%** ✅ | 6d |
| **总计** | **99** | **~84** | **~85%** | **~212d** |

> 单人预估；团队并行可压缩至 PRD v2 的 28 周路线图。
> 2026-07-06 更新：T1.11 BOM 缓存（ConcurrentDictionary）已实现；T3.14-T3.17 SAP 对接全模块完成（15 个文件，327 测试全通过）；阶段 3 从 29% → 53%。
> 2026-07-07 更新：T4.6-T4.9 性能压测套件完成（43 基准 + tests/MesAdmin.Benchmarks 项目）；阶段 4 从 0% → 27%；总进度从 ~68% → ~72%。
> 2026-07-07 更新：TX.1/TX.2 测试基础设施完成（CleipnirSagaFixture + MessagePipe/R3 helpers + Testcontainers PostgreSQL + IntegrationTestBase）；横切·测试 100% 完成；总进度从 ~72% → ~74%。
> 2026-07-07 更新：T4.10-T4.12 混沌工程完成（Saga 崩溃恢复 11 测试 + SignalR 重连 9 测试 + PLC 断连 6 测试 = 26 新增，383 测试全通过）；阶段 4 从 27% → 47%；总进度从 ~74% → ~77%。
> 2026-07-07 更新：TD.1 生产 compose.yaml + TD.2 Uncloud 集群初始化完成（Dockerfiles、compose.yaml、.env.example、Uncloud 部署指南 docs/deployment/uncloud-setup.md）；部署从 12% → 37%；总进度从 ~77% → ~79%。
> 2026-07-07 更新：TD.3 多机扩展指南完成（双机/三机架构模式、x-machines 服务定位、跨机卷/网络/故障处理）；部署从 37% → 50%；总进度从 ~79% → ~80%。
> 2026-07-07 更新：TD.4 uc deploy 生产部署指南完成（构建/计划/部署/回滚/扩展/CI/CD/检查清单全流程）；部署从 50% → 62%；总进度从 ~80% → ~81%。
> 2026-07-07 更新：TD.5 对外暴露配置完成（x-ports/Caddy/自定义域名/HTTPS/多机路由/安全加固）；部署从 62% → 75%；总进度从 ~81% → ~82%。
> 2026-07-07 更新：TD.6 PostgreSQL 3 级备份完成（pg_dump 每日 + WAL 归档 PITR + 异地存储 + 恢复操作指南）；部署从 75% → 88%；总进度从 ~82% → ~83%。
> 2026-07-07 更新：TD.7 零停机滚动部署验证完成（多副本配置/验证脚本/跨机扩缩容/能力评估）；横切·部署从 88% → **100%** ✅；总进度从 ~83% → ~84%。
> 2026-07-07 更新：TD.8 终端 VPN 接入完成（`docs/deployment/terminal-access.md` 完整指南：Tailscale 推荐方案 + Headscale 自托管 + 纯 WireGuard + ACL 安全策略 + 批量部署脚本）；横切·部署从 100% ✅ 保持；总进度从 ~84% → ~85%。

---

## 10. 关键路径

```
T0.1 骨架 → T0.2 依赖 → T0.5 DbContext → T0.6 Migration
  → T1.1 工单模型 → T1.2 Repository
  → T1.6 Cleipnir Saga 骨架 → T1.7 工序监控 → T1.8 完工确认
  → T2.12 OpcUaPlcClient → T2.13 Channel 管道 → T2.14 R3 OEE → T2.15 SignalR Hub
  → T3.14 SAP 工单同步 → T3.16 SAP Webhook（已打通 MES↔SAP 双向通道）
  → TD.2 Uncloud 集群 → T4.10 混沌工程（Saga 恢复）
```

**关键路径长度**：约 32 任务 / ~48 人天（单人串行核心链路）。

---

## 11. 优先级快速索引

### P0 任务（阻塞核心流程 / 合规强制）

- **阶段 0**：T0.1 ~ T0.9（全部）
- **阶段 1**：T1.1 ~ T1.24（全部，MVP 核心）
- **阶段 2**：T2.1 ~ T2.10（M03 质量）、T2.12 ~ T2.15（PLC + Channel + R3 + SignalR）
- **阶段 4**：T4.10 ~ T4.15（混沌工程 + IATF/ISO 审核）
- **横切·测试**：TX.1 ~ TX.3（全部）

### P1 任务（重要）

- **阶段 2**：T2.11、T2.19
- **阶段 3**：T3.1 ~ T3.5（M07 工艺，已完成）、T3.14 ~ T3.17（ERP/SAP，已完成）
- **阶段 4**：T4.6 ~ T4.9（性能压测）
- **横切·部署**：TD.1 ~ TD.6

### P2 任务（可延后）

- **阶段 3**：T3.6 ~ T3.13（M08 SQE + M09 排程）
- **阶段 4**：T4.1 ~ T4.5（M10 报表 + 离线模式）
- **横切·部署**：TD.7 ~ TD.8

---

## 12. 当前优先级（2026-07-06 审计）

### ✅ 本次会话完成

| ID | 任务 | 状态 |
|----|------|------|
| **T1.11** | **BOM 内存缓存优化**：`IBomCache` + `BomCache`（ConcurrentDictionary）+ `BomCacheInitializationService` 启动预热 + `BomRepository` 缓存优先策略 | ✅ **已完成** |
| **T3.14** | **SAP 工单双向同步**：`SapOrderSyncRecord` + 后台服务 + Release/Complete/Close 自动创建同步记录 | ✅ **已完成** |
| **T3.15** | **SAP WM IDoc 库存同步**：`SapInventorySyncService` 后台服务 | ✅ **已完成** |
| **T3.16** | **SAP Webhook + 拒单回写**：`SapRejectionWritebackService` + 手动重试 API | ✅ **已完成** |
| **T3.17** | **SAP 物料移动凭证同步**：`SapMaterialMovementSyncService` 后台服务 | ✅ **已完成** |
| **T4.6** | **热路径零分配验证**：BenchmarkDotNet 14 基准（PlcFrameReader/Writer、SpcCalculator、OeeRecord、MemoryPack、ArrayPool）| ✅ **已完成** |
| **T4.7** | **PLC 吞吐压测**：Channel 8 基准、容量 1000/10000、8 设备并发 | ✅ **已完成** |
| **T4.8** | **追溯查询性能压测**：正向/反向 10 基准、SHA-256 哈希、MemoryPack 序列化 | ✅ **已完成** |
| **T4.9** | **SignalR 并发压测**：5 消息类型、批量序列化、10/50/100 客户端并发 | ✅ **已完成** |
| **T4.10** | **Saga 崩溃恢复**：11 测试（CrashTestOpRepo + 参数化工站边界 + Effect 粒度 + AtMostOnce）| ✅ **已完成** |
| **T4.11** | **SignalR 自动重连**：9 测试（HubConnection 构建/不可达/状态机/重连间隔/Dispose/MemoryPack 协议）| ✅ **已完成** |
| **T4.12** | **PLC 断连**：6 测试（空数据/重连恢复/断连循环/ChannelHealth/低数据率/全部离线）| ✅ **已完成** |
| **TX.1** | **单元测试基础设施**：CleipnirSagaFixture + MessagePipeTestHelper + R3TestScheduler + 17 验收测试 | ✅ **已完成** |
| **TX.2** | **集成测试基础设施**：Testcontainers PostgreSQL 17 + IntegrationTestBase + DatabaseFixture 升级 | ✅ **已完成** |

### 🔴 P0 — 核心流程缺口

✅ **全部混沌工程（T4.10-T4.12）已完成**：11 Saga 崩溃 + 9 SignalR 重连 + 6 PLC 断连 = 26 测试 | |

### 🟠 P1 — 完善已有模块

| # | ID | 任务 | 原因 | 工时 |
|---|----|------|------|------|
| 3 | **TD.6** | 生产部署（PostgreSQL 3 级备份） | 生产环境 | 2d |

### 🟡 P2 — 业务完整性

| # | ID | 任务 | 原因 | 工时 |
|---|----|------|------|------|
| 5 | **T3.6-T3.9** | M08 SQE 供应商质量（评分/PPAP/管控/Web UI） | 业务完整性 | 7d |
| 6 | **T3.10-T3.13** | M09 排程（有限产能引擎 + 紧急插单 + Bryntum Gantt） | 业务完整性 | 10d |
| 7 | **T4.1-T4.5** | M10 报表 + 离线模式 | 业务完整性 | 11d |
| 8 | **T4.13-T4.15** | IATF 16949 + ISO 26262 审核文档 | 合规 | 5d |
| 9 | **TD.7-TD.8** | 零停机部署 + 终端 VPN 接入（**已完成**） | 部署 | 2d |

---

## 13. 变更记录

| 日期 | 版本 | 说明 |
|------|------|------|
| **2026-07-06** | **v7.0** | **T1.11 BOM 缓存 + T3.14-T3.17 SAP 对接全模块完成**：BOM 缓存（IBomCache + BomCache ConcurrentDictionary + 启动预热 + 缓存优先策略）已实现；SAP 全模块（ISapClient 抽象 + MockSapClient + HttpSapClient OData + 4 个后台服务 30s 轮询 + 2 个 API 端点 + EF Migration + DI 注册）均已构建通过，**327 测试全通过**；阶段 3 从 5/17(29%) → 9/17(53%)；总进度从 ~63% → ~68% |
| **2026-07-07** | **v7.3** | **T4.10-T4.12 混沌工程完成**：Saga 崩溃恢复 11 测试（Scenarios 13-18）+ SignalR 自动重连 9 测试（HubConnection 真实客户端）+ PLC 断连 6 测试（断连/重连/循环/空闲）；删除旧 `SignalRReconnectTests.cs`；**383 测试全通过**；阶段 4 从 27% → 47%；总进度从 ~74% → ~77% |
| **2026-07-07** | **v7.2** | **TX.1/TX.2 测试基础设施完成**：`tests/MesAdmin.Application.Tests/Infrastructure/` 目录（CleipnirSagaFixture + MessagePipeTestHelper + R3TestScheduler + IntegrationTestBase）；DatabaseFixture 升级为 Testcontainers PostgreSQL 17；17 验收测试全通过；横切·测试从 1/3 → 3/3 **100%**；总进度从 ~72% → ~74% |
| **2026-07-07** | **v7.1** | **T4.6-T4.9 性能基准套件完成**：`tests/MesAdmin.Benchmarks/` 项目（BenchmarkDotNet 0.14.0），含 ZeroAllocationBenchmarks(14) / PlcThroughputBenchmarks(8) / TraceabilityQueryBenchmarks(10) / SignalRPushBenchmarks(11)，共 43 基准方法；构建 0 错误，353 测试全通过；阶段 4 从 0% → 27%；总进度从 ~68% → ~72% |
| 2026-07-06 | v7.0 | **T1.11 BOM 缓存 + T3.14-T3.17 SAP 对接全模块完成**：BOM 缓存（IBomCache + BomCache ConcurrentDictionary + 启动预热 + 缓存优先策略）已实现；SAP 全模块（ISapClient 抽象 + MockSapClient + HttpSapClient OData + 4 个后台服务 30s 轮询 + 2 个 API 端点 + EF Migration + DI 注册）均已构建通过，**327 测试全通过**；阶段 3 从 5/17(29%) → 9/17(53%)；总进度从 ~63% → ~68% |
| 2026-07-05 | v6.4 | **M07 T3.3 防错三重校验完成**：TripleCheckService 三步骤 + API + Validator + DI 注册；158 测试全通过；阶段 3 进度 ~24% |
| 2026-07-05 | v6.3 | **M07 工艺管理 T3.1/T3.2/T3.4 完成**：Routing 域模型 + ECO 状态机 + JSONB + Migration + ESP 默认 31 工序；128 构建 0 错误；158 测试全通过 |
| 2026-07-05 | v6.2 | **T2.18 备件管理 Web UI 完成**：SparePart.razor 3 标签页 + MesApiClient 16 API 方法 |
| 2026-07-05 | v6.1 | **T2.18 备件管理集成测试完成**：32 单元 + 20 集成测试，总计 158/158 全通过 |
| 2026-07-05 | v5.0 | T2.17 集成测试（19 端到端）+ 42 单元测试全通过 |
| 2026-07-05 | v3.0 | T2.6 液压测试 + T2.9 质量报表 + T2.16 多协议驱动 完成 |
| 2026-07-05 | v2.0 | 代码审计修正：阶段 1 24/24 + 阶段 2 T2.10/T2.20-T2.23 完成 |
| 2026-07-05 | v1.1 | 真实进度修正 + 汇总统计百分比列 |
| 2026-07-01 | v1.0 | 初始版本，99 任务 |
