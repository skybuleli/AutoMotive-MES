# AutoMES 任务清单

> **项目：** 博世 ESP® 制动系统 MES（汽车 Tier-1 供应商制造执行系统）
> **来源：** PRD v2（`automotive-mes-prd-v2.html`）+ TAD v2（`automotive-mes-tad-v2.html`）+ AGENTS.md 宪法
> **当前状态：** 阶段 0 完成，阶段 1 进行中（M01 工单基本成型，M02/M04 待补齐）
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
| T1.3 | `[~]` | 工单创建与校验（SAP Webhook 接收、产品编码 ESP-9.0/9.1 + BOM 版本 + 工艺路线版本校验、`Ulid.NewUlid()` + 工单号 `WO-YYYYMMDD-NNNN` 生成、版本不符拒单回写 SAP） | P0 | 3d | T1.2 |
| T1.4 | `[ ]` | 物料齐套检查（BOM 展开、线边 + ERP 库存查询、ECU/HCU/电机批次是否足 500 件、缺料触发 JIT 拉动、齐套通过转 `Released`） | P0 | 3d | T1.3 |
| T1.5 | `[~]` | 首件检验流程（每班次/换型后强制、控制计划逐项检验、自动判定合格/不合格、不合格锁定工单触发 CAR） | P0 | 2d | T1.4 |
| T1.6 | `[x]` | **Cleipnir `ProductionOrderSaga` 骨架**：31 工序 × 7 站编排、`Cleipnir.ResilientFunctions.PostgreSQL` 状态持久化、Checkpoint、Effect 策略矩阵（站1 Effect 外/站2-5,7 AtLeastOnce/站6 AtMostOnce） | P0 | 3d | T1.2 |
| T1.7 | `[~]` | 工序执行监控（每工序记录操作员工号/设备号/开始结束时间/过程参数、异常触发 Andon、Saga 按工艺路线依次执行） | P0 | 3d | T1.6 |
| T1.8 | `[~]` | 完工确认（31 工序完成统计合格/不良数）+ 质量工程师审核放行 + 成品入库（追溯标签打印含二维码、编码规则、SAP 同步完工数量、转 `Closed`） | P0 | 3d | T1.7 |
| T1.9 | `[x]` | 工单管理 Web 页面（`MudTable<ProductionOrder>` 列表/详情、状态流转看板、ObservableCollections 增量绑定） | P0 | 3d | T1.8 |
| T1.10 | `[x]` | 工单 REST API（Avalonia 工位终端用、JSON 默认 + `Accept: application/x-memorypack` 二进制双协议） | P0 | 2d | T1.8 |

### M02 物料管理 JIT/JIS（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T1.11 | `[ ]` | BOM 领域模型（ESP-9.0、87 种物料、4 层结构）+ MasterMemory 内存缓存 + SAP/PLM 同步、工单创建时版本校验 | P0 | 2d | T1.2 |
| T1.12 | `[ ]` | 来料扫码入库（GS1-128 解析含物料编码+批次+数量+生产日期、`ReadOnlySpan<char>` 零分配切片禁止 `Substring`、合格供应商名录校验、写 `material_bindings` 表） | P0 | 3d | T1.11 |
| T1.13 | `[ ]` | 线边库存实时监控（每工位电子看板、安全/最低库存阈值、低于安全黄色预警、低于最低红色报警+自动叫料） | P0 | 2d | T1.12 |
| T1.14 | `[ ]` | JIT 看板拉动（空料箱扫码生成电子看板信号→推送仓库 PDA→备料送达扫码确认、全流程时间戳） | P0 | 3d | T1.13 |
| T1.15 | `[ ]` | 投料批次绑定（操作员扫码绑定 物料编码+批次号→工单号→产品 S/N、写追溯链 `traceability_links`） | P0 | 2d | T1.14 |
| T1.16 | `[ ]` | 物料防错 Poka-Yoke（关键物料 ECU芯片/电磁阀 BOM 比对、错误锁定设备+声光报警、必须质量工程师解锁） | P0 | 2d | T1.15 |
| T1.17 | `[ ]` | 物料消耗反冲（工单完工按 BOM 标准用量扣减线边库存、差异>2% 生成异常报告、同步 SAP 物料移动凭证） | P0 | 2d | T1.15 |
| T1.18 | `[ ]` | 物料管理 Web 页面（库存看板/扫码入库/JIT 看板/批次查询） | P0 | 3d | T1.17 |

### M04 全链路追溯（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T1.19 | `[ ]` | `traceability_links` 表 + 4 级追溯模型（L1 车辆 VIN 17 位 / L2 ESP 总成 S/N `ESP9-YYYYMMDD-NNNNNN` / L3 零部件 ECU+HCU+电机 S/N+电磁阀批次 / L4 原材料阀体铝合金/PCB 板材批次） | P0 | 2d | T0.6 |
| T1.20 | `[ ]` | 追溯绑定写入（`Effect.AtLeastOnce` + DB 唯一约束防重复、装配工位扫码自动绑定 ECU/HCU/电机 S/N→电磁阀批次→阀体批次） | P0 | 2d | T1.19 |
| T1.21 | `[ ]` | 哈希链审计（追溯链不可篡改、每次绑定哈希链接前一条记录、过程参数记录审计追踪） | P0 | 2d | T1.20 |
| T1.22 | `[ ]` | 正向追溯查询（VIN→ESP 总成 S/N→工单→HCU S/N→阀体批次→供应商，性能 ≤30s、`.Select()` 投影禁止 `SELECT *`） | P0 | 2d | T1.20 |
| T1.23 | `[ ]` | 反向追溯查询（原材料批次→所有总成 S/N（如 1247 件）→所有 VIN、导出 Excel 发送整车厂、性能 ≤60s） | P0 | 2d | T1.22 |
| T1.24 | `[ ]` | 追溯查询 Web 页面（正反向切换、追溯链可视化、Excel 导出） | P0 | 2d | T1.23 |

---

## 4. 阶段 2：质量体系（PRD 9-14 周）— M03 SPC + M05 设备 + M06 Andon（~55d）

### M03 质量管理 SPC + 全检（P0）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.1 | `[ ]` | 质量领域模型（`QualityRecord`/`InspectionPlan`/`SpcSample`/`Ncr` 不合格品） | P0 | 2d | T1.1 |
| T2.2 | `[ ]` | IQC 来料检验（关键物料 ECU芯片/电磁阀/压力传感器 AQL 抽样、外观/尺寸/电性能、不合格锁定批次触发 SQE 8D） | P0 | 2d | T2.1 |
| T2.3 | `[ ]` | 首件检验（ESP 23 个检验特性、全部合格方可批量生产） | P0 | 2d | T2.1 |
| T2.4 | `[ ]` | IPQC 过程巡检（螺栓拧紧每 50 件抽 5、液压压力每 100 件抽 3、超时未完成升级报警） | P0 | 2d | T2.1 |
| T2.5 | `[ ]` | **SPC 实时控制图**：6 关键特性 X̄-R 图（M6/M8 扭矩、液压建压时间、保压压力、泄漏率、CAN 延迟）、R3 管道每 5 样本算 Cpk、超控制限报警、`stackalloc Span<double>` 零分配均值-极差计算（`.NET 10` `Span.Sort()`） | P0 | 3d | T2.4 |
| T2.6 | `[ ]` | 100% 在线液压功能测试（12 路电磁阀逐一动作测试、建压/保压/泄压循环、泄漏率 ≤0.5 CC/hr、不合格设备自动锁止） | P0 | 3d | T2.5 |
| T2.7 | `[ ]` | 不合格品处置（自动移入隔离区、质量工程师评审返工/让步/报废、返工品重检合格回线、全流程可追溯） | P0 | 2d | T2.6 |
| T2.8 | `[ ]` | 8D/CAR 闭环（D1-D8 每步责任人和截止日、超时自动升级、永久纠正措施验证后关闭、重复性问题自动触发 8D） | P0 | 3d | T2.7 |
| T2.9 | `[ ]` | 质量报表（日报一次合格率+不良品分布、周报 Cpk 趋势+控制图+异常、月报 PPM+供应商排名+质量成本、PDF 自动邮件推送） | P1 | 3d | T2.8 |
| T2.10 | `[ ]` | SPC + 质量 Web 页面（ECharts X̄-R 控制图、检验录入、NCR 处置、8D 看板） | P0 | 3d | T2.8 |

### M05 设备管理 TPM + OEE（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.11 | `[ ]` | 设备领域模型（`Equipment`、8 台核心设备清单、OEE 记录、维护计划、备件） | P1 | 2d | T1.1 |
| T2.12 | `[ ]` | **`OpcUaPlcClient`**：每 500ms 读设备状态寄存器、`PipeReader`/`PipeWriter` 零拷贝网络 IO（禁止裸 `Stream.ReadAsync`）、`SearchValues<byte>` 帧头 `0x55 0xAA` SIMD 扫描、`ref struct PlcFrameReader`、`ArrayPool<byte>.Shared` 512B 缓冲池 | P0 | 3d | T2.11 |
| T2.13 | `[ ]` | **`PlcDataAcquisitionPipeline`**：`BoundedChannel<PlcSnapshot>` 容量 10000 + `FullMode = Wait` 背压（禁止 `BlockingCollection`）、8 设备 100Hz 读取循环（`Task.Delay(10)`）、`ReadAllAsync` 喂 R3 管道 | P0 | 2d | T2.12 |
| T2.14 | `[ ]` | MessagePipe `PlcDataChanged` 发布 + **R3 `OeeReactivePipeline`**（`Sample(5s)` 采样、算可用率/性能率/良品率、订阅推送） | P0 | 2d | T2.13 |
| T2.15 | `[ ]` | **SignalR `DashboardHub`**：`OeeUpdated` 3s 推送、强制 MemoryPack 二进制（禁止 JSON）、8 设备 OEE 看板实时更新、`ChannelHealth` 10s 通道健康度 | P0 | 2d | T2.14 |
| T2.16 | `[ ]` | 多协议驱动（OPC UA 拧紧机 Atlas Copco / Open Protocol、EtherNet/IP 液压台、Modbus TCP 刷写台、Profinet 压装机、OPC UA SMT 线） | P1 | 4d | T2.12 |
| T2.17 | `[ ]` | 预防性维护（运行时间/次数触发维护工单、拧紧机每 10 万次标定、液压台每月密封件更换） | P1 | 2d | T2.15 |
| T2.18 | `[ ]` | 备件管理（维护工单关联备件清单、库存检查、不足生成采购申请） | P2 | 2d | T2.17 |
| T2.19 | `[ ]` | OEE 看板 Web 页面（8 设备 OEE 卡片 `glass-kpi-card`、`status-dot` 发光圆点绿/橙/红、ECharts 趋势、OEE 目标 85%~92%） | P1 | 3d | T2.15 |

### M06 Andon 报警（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T2.20 | `[ ]` | Andon 领域模型 + 三级上报（L1 工位声光+看板 5min 未响应→L2 班组长 PDA+企业微信 10min→L3 生产经理电话+邮件） | P1 | 2d | T2.15 |
| T2.21 | `[ ]` | **R3 防抖报警管道**：`ThrottleFirst(5s)` 避免重复报警风暴、`TorqueExceeded`/`LeakRateHigh`/`FlashFailed` MessagePipe 消息流 | P1 | 2d | T2.20 |
| T2.22 | `[ ]` | ESP 专用报警（扭矩超差、泄漏超标、刷写失败、CAN 通信异常 + `andon-pulse` 2s 脉冲动画 + `MudAlert`） | P1 | 2d | T2.21 |
| T2.23 | `[ ]` | Andon 看板 Web 页面（实时报警列表、升级链可视化、企业微信推送集成） | P1 | 2d | T2.22 |

---

## 5. 阶段 3：集成（PRD 15-22 周）— M07 工艺 + M08 SQE + M09 排程 + ERP（~34d）

### M07 工艺管理（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.1 | `[ ]` | 工艺领域模型（`Routing` 31 工序 × 7 站、`Operation` 标准工时/工装夹具/参数模板、ECO 版本） | P1 | 2d | T1.6 |
| T3.2 | `[ ]` | ESP 参数模板（M6 扭矩 22±1Nm/180°±5°、M8 扭矩 45±2Nm/270°±10°、液压/CAN 参数模板） | P1 | 1d | T3.1 |
| T3.3 | `[ ]` | 防错三重校验（扫描物料码→BOM 比对→设备参数比对→三重校验通过方可启动） | P1 | 2d | T3.2 |
| T3.4 | `[ ]` | 工艺版本控制（ECO 审批流、工艺变更需审批、旧版本归档可追溯） | P1 | 2d | T3.1 |
| T3.5 | `[ ]` | 工艺管理 Web 页面（工艺路线编辑、参数模板、ECO 审批） | P1 | 2d | T3.4 |

### M08 SQE 供应商质量（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.6 | `[ ]` | 供应商领域模型 + 评分（来料合格率 30% + 交货准时率 25% + 8D 响应速度 20% + PPAP 通过率 15% + 价格 10%） | P2 | 2d | T2.8 |
| T3.7 | `[ ]` | PPAP 管理（18 项文档电子归档、到期自动提醒、逾期升级） | P2 | 2d | T3.6 |
| T3.8 | `[ ]` | 关键供应商管控（电磁阀/压力传感器/PCB 板材三类最高等级管控） | P2 | 1d | T3.6 |
| T3.9 | `[ ]` | SQE Web 页面（供应商评分卡、PPAP 文档库） | P2 | 2d | T3.7 |

### M09 排程（P2）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.10 | `[ ]` | 排程领域模型（8 台设备 × 3 班次 × 换型时间约束、有限产能） | P2 | 2d | T1.8 |
| T3.11 | `[ ]` | 有限产能排程引擎（最早可排时间计算、冲突检测） | P2 | 3d | T3.10 |
| T3.12 | `[ ]` | 紧急插单（OEM 急单自动计算最早可排时间、推送冲突预警） | P2 | 2d | T3.11 |
| T3.13 | `[ ]` | Bryntum Gantt 甘特图集成（JSInterop、排程可视化、拖拽调整） | P2 | 3d | T3.11 |

### ERP/SAP 对接（P1）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T3.14 | `[ ]` | SAP RFC/BAPI 工单双向同步（状态、完工数量） | P1 | 3d | T1.8 |
| T3.15 | `[ ]` | SAP WM IDoc 库存同步 | P1 | 2d | T1.13 |
| T3.16 | `[ ]` | SAP Webhook 工单推送接收 + 拒单回写异常 | P1 | 2d | T1.3 |
| T3.17 | `[ ]` | SAP 物料移动凭证同步（消耗反冲） | P1 | 2d | T1.17 |

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
| T4.6 | `[ ]` | 热路径零分配验证（dotMemory / BenchmarkDotNet、Span/ArrayPool/stackalloc 验证 100% 零堆分配） | P1 | 3d | T2.13 |
| T4.7 | `[ ]` | PLC 吞吐压测（8 设备 × 100Hz、10x 提升验证） | P1 | 2d | T4.6 |
| T4.8 | `[ ]` | 追溯查询性能压测（正向 ≤30s、反向 ≤60s、大数据量验证） | P1 | 2d | T1.23 |
| T4.9 | `[ ]` | SignalR 并发压测（多终端实时推送） | P1 | 2d | T2.15 |

### 混沌工程（P0，阶段 4 必须执行）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| T4.10 | `[ ]` | 随机杀进程 → 验证 Saga 恢复（Effect 不重复 AtMostOnce / 重试到成功 AtLeastOnce、Checkpoint 恢复） | P0 | 2d | T1.6 |
| T4.11 | `[ ]` | 随机断网 → 验证 SignalR 自动重连 | P0 | 2d | T2.15 |
| T4.12 | `[ ]` | 随机拔 PLC → 验证 Channel 背压 + 缓冲（`FullMode = Wait`） | P0 | 2d | T2.13 |

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
| TD.1 | `[ ]` | 生产 `compose.yaml`（postgres:17-alpine + mes-web + mes-api + pg-backup、持久卷、资源限制、健康检查） | P1 | 2d | T0.3 |
| TD.2 | `[ ]` | **Uncloud 集群初始化**：`uc machine init root@<厂区首台服务器>` → 自动装 Docker + uncloudd systemd + Caddy + `*.uncld.dev` 子域名 + WireGuard mesh | P1 | 1d | TD.1 |
| TD.3 | `[ ]` | **Uncloud 多机扩展**：`uc machine add --name <产线N> root@<ip>` 加入各服务器、跨机服务互通验证、`uc machine ls` / `uc wg show --machine <name>` 查看 peer/延迟 | P1 | 1d | TD.2 |
| TD.4 | `[ ]` | **`uc deploy` 生产部署**：预览 Terraform 式计划（容器/卷/机器分布）→ 确认部署、`uc ls` / `uc inspect <service>` 验证、`uc logs -f` 跨机日志 | P1 | 2d | TD.3 |
| TD.5 | `[ ]` | **对外暴露配置**：`x-ports` 仅暴露 MES Web 前端 + Caddy 自动 HTTPS（`*.uncld.dev` 或自定义域名 CNAME）；数据库/SignalR Hub **不发布**，仅 WireGuard mesh 内可达 | P1 | 1d | TD.4 |
| TD.6 | `[ ]` | **PostgreSQL 3 级备份**：全量 `pg_dump`→对象存储每日 + WAL `archive_command` 持续 + PITR `pg_basebackup` 每周（pg-backup 容器） | P1 | 2d | TD.4 |
| TD.7 | `[ ]` | **零停机滚动部署验证**：`uc deploy` 版本更新 + 健康检查 + 自动重启、`uc scale <service> <n>` 跨机扩缩容 | P2 | 1d | TD.5 |
| TD.8 | `[ ]` | **终端 VPN 接入补充**：车间设备/运维人员接入厂区网络（Tailscale/WireGuard 客户端，因 Uncloud 仅解决服务器间组网，非终端 VPN） | P2 | 1d | TD.3 |

---

## 8. 横切·测试（~6d + 各模块随开发同步）

| ID | 状态 | 任务 | 优先级 | 工时 | 依赖 |
|----|------|------|--------|------|------|
| TX.1 | `[ ]` | 单元测试基础设施（xUnit + Cleipnir `InMemoryFunctionStore` + MessagePipe `TestPublisher<T>`/`TestSubscriber<T>` + R3 `TestScheduler`） | P0 | 2d | T0.2 |
| TX.2 | `[ ]` | 集成测试基础设施（`Testcontainers.PostgreSql` 启动真实 PG，OrbStack 提供 Docker 引擎） | P0 | 2d | T0.5 |
| TX.3 | `[ ]` | Saga 崩溃恢复测试（中断 `CancellationToken` 后验证 Effect 重新执行） | P0 | 2d | T1.6 |

> **注**：各模块单元测试随开发同步进行（每模块 1-2d，已含在对应任务工时内），不单列。

---

## 9. 汇总统计

| 阶段 | 任务数 | 工时（人天） |
|------|--------|-------------|
| 阶段 0 骨架 | 9 | 16d |
| 阶段 1 MVP | 24 | 57d |
| 阶段 2 质量 | 23 | 55d |
| 阶段 3 集成 | 17 | 34d |
| 阶段 4 优化 | 15 | 33d |
| 横切·部署 | 8 | 11d |
| 横切·测试 | 3 | 6d |
| **总计** | **99** | **~212d** |

> 单人预估；团队并行可压缩至 PRD v2 的 28 周路线图。

---

## 10. 关键路径

```
T0.1 骨架 → T0.2 依赖 → T0.5 DbContext → T0.6 Migration
  → T1.1 工单模型 → T1.2 Repository
  → T1.6 Cleipnir Saga 骨架 → T1.7 工序监控 → T1.8 完工确认
  → T2.12 OpcUaPlcClient → T2.13 Channel 管道 → T2.14 R3 OEE → T2.15 SignalR Hub
  → TD.2 Uncloud 集群 → T4.10 混沌工程（Saga 恢复）
```

**关键路径长度**：约 30 任务 / ~45 人天（单人串行核心链路）。

---

## 11. 优先级快速索引

### P0 任务（阻塞核心流程 / 合规强制）

- **阶段 0**：T0.1 ~ T0.9（全部）
- **阶段 1**：T1.1 ~ T1.24（全部，MVP 核心）
- **阶段 2**：T2.1 ~ T2.10（M03 质量）、T2.12 ~ T2.15（PLC + Channel + R3 + SignalR）
- **阶段 4**：T4.10 ~ T4.15（混沌工程 + IATF/ISO 审核）
- **横切·测试**：TX.1 ~ TX.3（全部）

### P1 任务（重要）

- **阶段 2**：T2.11、T2.16 ~ T2.19、T2.20 ~ T2.23
- **阶段 3**：T3.1 ~ T3.5（M07 工艺）、T3.14 ~ T3.17（ERP/SAP）
- **阶段 4**：T4.6 ~ T4.9（性能压测）
- **横切·部署**：TD.1 ~ TD.6

### P2 任务（可延后）

- **阶段 2**：T2.18（备件管理）
- **阶段 3**：T3.6 ~ T3.13（M08 SQE + M09 排程）
- **阶段 4**：T4.1 ~ T4.5（M10 报表 + 离线模式）
- **横切·部署**：TD.7 ~ TD.8

---

## 12. 变更记录

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-07-01 | v1.0 | 初始版本，基于 PRD v2 + TAD v2 + AGENTS.md，覆盖 28 周 4 阶段 99 任务 |
