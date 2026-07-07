# AutoMES — 博世 ESP® 制动系统 MES

> 面向汽车 Tier-1 供应商（博世 ESP® 电子稳定程序制动系统总成产线）的全链路制造执行系统。
> 覆盖 **工单 → 物料 → SPC → 追溯 → Andon → OEE**，7 站 31 工序全 Saga 编排，热路径零堆分配。
>
> **交付状态：99/99 任务完成 ✅ · 383 测试通过 ✅ · 43 性能基准 ✅**
>
> [![CI](https://github.com/skybuleli/AutoMotive-MES/actions/workflows/ci.yml/badge.svg)](https://github.com/skybuleli/AutoMotive-MES/actions/workflows/ci.yml)

---

## 📊 项目概览

| 指标 | 数值 |
|------|------|
| 源文件 | 315 `.cs` / 30 `.razor` / 6 `.json` / 11 `.md` / 5 `.yaml` |
| 代码项目 | 7 个（Domain / Application / Infrastructure / Api / Web / Generators / Benchmarks） |
| 领域模型 | 33（[MemoryPackable] 实体 + 值对象） |
| API 端点 | 59（FastEndpoints · 9 功能组） |
| Blazor 页面 | 21 |
| SignalR Hub | 4（DashboardHub / AndonHub / MemoryPackHubProtocol / HubMessageEnvelope） |
| **测试** | **383 ✅ · 0 failed · 0 skipped**（166 单元 + 217 集成/混沌） |
| 性能基准 | 43（4 套件：零分配/PLC/追溯/SignalR） |
| EF Core 迁移 | 19 次 |
| 总工时（预估） | ~212 人天 |
| 路线图 | 28 周（PRD v2 4 阶段） |

---

## 🏗️ 系统架构

```
┌─────────────────────────────────────────────────────────────┐
│                      MudBlazor Web UI                       │
│  (暗色 #CBA6F7 / 亮色 #8F6AAF · Inter Tight · 12px 圆角)  │
└──────────────────┬──────────────────────────────────────────┘
                   │ SignalR (MemoryPack 二进制)
                   │ REST API (JSON / x-memorypack)
┌──────────────────▼──────────────────────────────────────────┐
│                    MesAdmin.Api  REST API                    │
│  FastEndpoints · JWT RBAC (6 角色) · ZLogger 结构化日志    │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│               MesAdmin.Infrastructure                        │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐  │
│  │ EF Core  │ │ SignalR  │ │ PLC 驱动 │ │ MessagePipe   │  │
│  │ PostgreSQL│ │ Hubs     │ │ OPC UA/  │ │ + R3 响应式   │  │
│  │ 17       │ │          │ │ ETH/IP  │ │ 管道          │  │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌───────────────┐  │
│  │ Cleipnir │ │ Channels │ │ Pipelines│ │ ZLogger       │  │
│  │ Saga     │ │ 10000    │ │ PipeReader│ │ IBufferWriter │  │
│  │ 持久化   │ │ Backpressure│ │ 零拷贝  │ │ 直写         │  │
│  └──────────┘ └──────────┘ └──────────┘ └───────────────┘  │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│                MesAdmin.Application                          │
│  Cleipnir ProductionOrderSaga (31 工序 × 7 站编排)         │
│  Features: 工单/物料/质量/追溯/Andon/SPC/排程/SAP/Sync     │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│                  MesAdmin.Domain                             │
│  [MemoryPackable] 模型 · Ulid 主键 · 严格值对象            │
└─────────────────────────────────────────────────────────────┘
```

### 依赖方向

```
Web → Infrastructure → Application → Domain
                                    ↑
Api → Infrastructure -──────────────┘
```

---

## 📦 技术栈

| 类别 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET SDK (C# 13) | 10.0 |
| UI | MudBlazor | 9.6.0 |
| 序列化 | MemoryPack | 1.21.4 |
| 工作流引擎 | Cleipnir.ResilientFunctions | 4.2.5 |
| 进程内消息 | MessagePipe | 1.8.2 |
| 响应式编程 | R3 | 1.3.1 |
| 日志 | ZLogger | 2.5.10 |
| 主键 | Ulid | 1.4.1 |
| UI 集合 | ObservableCollections | 3.3.4 |
| 内存缓存 | MasterMemory | 3.0.4 |
| 数据库 | Npgsql EF Core + PostgreSQL 17 | 10.0.2 |
| 实时通信 | SignalR + MemoryPack 二进制 | ASP.NET Core 10 |
| IO 管道 | System.Threading.Channels / System.IO.Pipelines | .NET 10 |
| 报表 | QuestPDF | — |
| 部署 | Docker Compose + Uncloud (WireGuard) | — |
| 可观察性 | GreptimeDB + vmalert + Alertmanager + OTLP | — |

---

## 🧩 模块清单

### M01 生产工单管理
- `ProductionOrder` 领域模型 + 5 状态机（Created→Released→InProgress→Completed→Closed）
- Cleipnir `ProductionOrderSaga`：31 工序 × 7 站编排、Effect 策略矩阵（AtLeastOnce/AtMostOnce）
- 工单 CRUD + SAP Webhook 接收 + ESP-9.0/9.1 产品编码校验
- 物料齐套检查 + 首件检验 + 完工确认（质量审核放行 → 成品入库 → 追溯标签打印）
- MudBlazor Web 页面 + REST API（JSON + MemoryPack 双协议）

### M02 物料管理 JIT/JIS
- BOM 内存缓存（ConcurrentDictionary + 启动预热 + 缓存优先策略）
- 来料扫码入库（GS1-128 解析、`ReadOnlySpan<char>` 零分配）
- 线边库存实时监控 + 安全/最低库存阈值预警
- JIT 看板拉动、投料批次绑定、Poka-Yoke 防错
- 物料消耗反冲（BOM 标准用量扣减 + 差异>2% 异常报告）

### M03 质量管理 SPC + 全检
- SPC 实时控制图：SpcCalculator（Cpk/Ppk/X̄-R + Western Electric 8 判异规则）
- IQC/IPQC/首件检验完整流程
- 100% 在线液压功能测试（R3 管道 + 12 路电磁阀测试 + 泄漏率判定）
- NCR 不合格品处置 + 8D/CAR 闭环
- 质量报表（QuestPDF A4：日报/周报/月报 + 定时邮件推送）
- ECharts X̄-R 控制图 Web 页面

### M04 全链路追溯
- 4 级追溯模型（L1 VIN → L2 ESP 总成 S/N → L3 零部件 → L4 原材料）
- SHA-256 哈希链防篡改 + DB 唯一约束防重复
- 正向追溯（VIN→所有组件，≤30s）
- 反向追溯（原材料批次→所有 VIN，≤60s + Excel 导出）
- 追溯 Web 页面（正反向切换 + 追溯链可视化）

### M05 设备管理 TPM + OEE
- 8 设备 PLC 驱动（OPC UA / EtherNet/IP / Modbus TCP / Profinet）
- `PlcDataAcquisitionPipeline`：BoundedChannel 10000 + FullMode=Wait 背压
- R3 `OeeReactivePipeline`：OEE 实时计算（可用率/性能率/良品率）
- SignalR `DashboardHub`：OEE 实时推送 + MemoryPack 二进制
- 预防性维护（运行时间/次数触发维护工单）
- 备件管理（CRUD + 库存盘点 + 采购申请 + 审批流）
- OEE 看板 Web 页面（SignalR 实时 + glass-kpi-card）

### M06 Andon 报警
- Andon 领域模型 + 6 状态机 + 三级上报（L1/L2/L3）
- R3 防抖报警管道（ThrottleFirst 5s + dedup）
- ESP 专用报警（扭矩超差/泄漏超标/CAN 异常/工艺偏差）
- Andon 看板 Web 页面（实时报警列表 + 升级链可视化 + SignalR 推送）

### M07 工艺管理 + SQE + 排程 + SAP
- 工艺路线 31 工序 × 7 站 + ECO 版本控制（Draft→Approved→Released→Superseded）
- 防错三重校验（物料扫码 → BOM 比对 → 设备参数比对）
- SQE 供应商质量（评分模型 5 维度 + PPAP 18 项文档 + 关键供应商管控）
- 排程引擎（有限产能 + 8 设备 × 3 班次 + 紧急插单 + ECharts 甘特图）
- SAP 工单双向同步 + 库存同步 + 物料移动凭证同步 + Webhook 拒单回写

### 跨模块基础设施
- **MesAdmin.Generators**: 源生成器（`ServiceRegistrationGenerator` 自动 DI 注册）
- **可观察性栈**: GreptimeDB（OTLP metrics/traces） + vmalert（PromQL 规则评估） + Alertmanager → 飞书 Webhook
- **JWT 安全**: 6 角色 RBAC（生产经理/班组长/质量工程师/设备工程师/仓库员/SQE）
- **FastEndpoints 组**: 9 功能组（Andon / Quality / Routing / Reconciliation / Sync / SapWebhooks / Reports / Scheduling / Inventory）

---

## 🧪 测试覆盖

| 测试套件 | 数量 | 类型 | 基础设施 |
|---------|------|------|---------|
| `MesAdmin.Domain.Tests` | 166 | 单元测试 | xUnit + Cleipnir InMemory |
| `MesAdmin.Application.Tests` | 217 | 集成+混沌 | Testcontainers PostgreSQL 17 |
| **总计** | **383** | | **0 failed · 0 skipped** |

### 关键测试场景

| 场景 | 测试数 | 覆盖内容 |
|------|--------|---------|
| Saga 崩溃恢复 | 11 | 所有工站边界崩溃 + Effect 粒度验证 |
| SignalR 自动重连 | 9 | HubConnection 状态机 + 重连间隔 + MemoryPack 协议 |
| PLC 断连背压 | 6 | 断连/重连/循环/ChannelHealth |
| Saga Effect 幂等 | 17 | AtLeastOnce/AtMostOnce 重放验证 |

---

## ⚡ 性能基准

| 基准套件 | 方法数 | 测试内容 |
|---------|--------|---------|
| `ZeroAllocationBenchmarks` | 14 | PlcFrameReader / Writer / SpcCalculator / SpcSample / OeeRecord / MemoryPack / ArrayPool |
| `PlcThroughputBenchmarks` | 8 | Channel 吞吐 / PlcSnapshot 创建 / ChannelHealth |
| `TraceabilityQueryBenchmarks` | 10 | 正反向追溯 / 哈希链 / MemoryPack |
| `SignalRPushBenchmarks` | 11 | MemoryPack 序列化 / Andon / 并发推送 |
| **总计** | **43** | **[MemoryDiagnoser] 零分配验证** |

### 零分配验证覆盖

| 热路径 | 技术 | 状态 |
|--------|------|------|
| PLC 帧解析 | `ref struct PlcFrameReader` + `ReadOnlySpan<byte>` | ✅ |
| PLC 帧编码 | `Span<byte>` + `stackalloc` | ✅ |
| SPC 计算 | `stackalloc Span<double>` + `Span.Sort()` | ✅ |
| OEE 计算 | `stackalloc` metrics 数组 | ✅ |
| GS1-128 解析 | `ReadOnlySpan<char>` | ✅ |
| MemoryPack | 进程间二进制通信 | ✅ |
| ArrayPool | 缓冲池租用/归还 | ✅ |

---

## 📐 核心架构决策

### Cleipnir Saga 编排
```
Effect 外    → 安全联锁、人工确认（重放时重新实时评估）
AtLeastOnce  → 幂等保护（唯一约束 / 先读后写 / Version 乐观锁）
AtMostOnce   → 重放时复用上次结果，不重复执行副作用
Effect 内    → 禁止读外部缓存（重放时缓存可能已失效）
```

### 零分配铁律
- `SearchValues<T>` + SIMD 帧头扫描
- `PipeReader` / `PipeWriter` 零拷贝网络 IO
- `IBufferWriter<byte>` ZLogger 直写
- `Channel<T>` (Bounded) 替换 `BlockingCollection<T>`

### MemoryPack 二进制强制
- SignalR 通信：**强制 MemoryPack**，禁止 JSON
- Saga 状态持久化：MemoryPack → PostgreSQL JSONB
- API：默认 JSON，支持 `Accept: application/x-memorypack`

### Ulid 主键策略
- 所有实体主键使用 `Ulid`（可排序 UUID）
- `UlidToGuidConverter` → PostgreSQL `uuid` 列
- 禁止自增 ID / 随机 Guid

---

## ⚙️ 部署

```
┌─────────────────────────────────────────────┐
│                Uncloud Cluster               │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │ PostgreSQL│  │ mes-api  │  │ mes-web  │  │
│  │ 17       │  │ .NET 10  │  │ .NET 10  │  │
│  └──────────┘  └──────────┘  └──────────┘  │
│  WireGuard mesh + Caddy (自动 HTTPS)        │
│  多机扩展 · 零停机滚动部署 · 3 级备份       │
└─────────────────────────────────────────────┘
         │
         │ Tailscale / WireGuard VPN
         ▼
┌─────────────────────────────────────────────┐
│           车间终端 (Avalonia)                │
│  REST API + SignalR MemoryPack 二进制       │
│  离线模式 (BoundedChannel 5000 + 重试)      │
└─────────────────────────────────────────────┘
```

- **本地开发**：[OrbStack](https://orbstack.dev) + `docker compose -f docker/compose.dev.yaml up -d`
- **生产部署**：[Uncloud](https://uncloud.run)（WireGuard mesh + Caddy + 多机 Docker Compose）

---

## 🚀 快速开始

```bash
# 1. 启动 PostgreSQL
docker compose -f docker/compose.dev.yaml up -d

# 2. 运行 Web 管理后台
dotnet run --project src/MesAdmin.Web

# 3. 运行 REST API（可选，工位终端用）
dotnet run --project src/MesAdmin.Api

# 4. 运行全部测试
dotnet test

# 5. 运行性能基准
dotnet run -c Release --project tests/MesAdmin.Benchmarks
```

---

## ✅ 交付清单

| 检查项 | 状态 |
|--------|------|
| 全部 99 开发任务完成 | ✅ |
| 383 测试全部通过（0 failed · 0 skipped） | ✅ |
| 43 性能基准覆盖所有热路径 | ✅ |
| 19 次 EF Core 迁移对齐 TAD 设计 | ✅ |
| 混沌工程 3 项完成（Saga/SignalR/PLC） | ✅ |
| IATF 16949 + ISO 26262 合规文档齐全 | ✅ |
| 审计追踪 + 哈希链防篡改 | ✅ |
| Uncloud 生产部署指南完整 | ✅ |
| 零停机滚动部署 + PostgreSQL 3 级备份 | ✅ |

---

## 📁 文档索引

| 文档 | 路径 | 说明 |
|------|------|------|
| 任务清单 | [TASKS.md](./TASKS.md) | 99 任务完整状态追踪 |
| AI 助手指南 | [AGENTS.md](./AGENTS.md) | 项目编码宪法 |
| IATF 16949 矩阵 | [docs/compliance/iatf-16949-matrix.md](./docs/compliance/iatf-16949-matrix.md) | 6+7 条款覆盖证据 |
| ISO 26262 工具资质 | [docs/compliance/iso-26262-tool-qualification.md](./docs/compliance/iso-26262-tool-qualification.md) | 8 工具 TCL 分类与认证 |
| 审计追踪 | [docs/compliance/audit-trail.md](./docs/compliance/audit-trail.md) | 哈希链/参数完整性/合规矩阵 |
| 部署指南 | [docs/deployment/uncloud-setup.md](./docs/deployment/uncloud-setup.md) | Uncloud 集群全流程指南 |
| 数据库备份 | [docs/deployment/postgres-backup.md](./docs/deployment/postgres-backup.md) | 3 级备份策略与恢复指南 |
| 终端 VPN | [docs/deployment/terminal-access.md](./docs/deployment/terminal-access.md) | Tailscale/Headscale/WireGuard 方案 |
| 可观察性 | [docs/observability/decision.md](./docs/observability/decision.md) | GreptimeDB + vmalert + Alertmanager |
| 项目摘要 | [PROJECT_SUMMARY.md](./PROJECT_SUMMARY.md) | 完整项目成果摘要 |
| PRD v2 | `automotive-mes-prd-v2.html` | 产品需求文档 |
| TAD v2 | `automotive-mes-tad-v2.html` | 零分配技术架构文档 |

---

> **2026-07-07 · .NET 10 · PostgreSQL 17 · Cysharp 全家桶 · Cleipnir Saga · MemoryPack 二进制 · 零堆分配**
