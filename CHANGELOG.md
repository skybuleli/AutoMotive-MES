# Changelog

## v1.0.0 (2026-07-07)

> **AutoMES — 博世 ESP® 制动系统 MES** — 首个完整发布版本
>
> 99/99 任务完成 · 383 测试通过 · 43 性能基准 · 0 构建警告

### 阶段 0：项目骨架
- .NET 10 解决方案，5 层项目结构（Domain / Application / Infrastructure / Web / Api）
- 14 个 NuGet 包精确版本锁定（MudBlazor/MemoryPack/Cleipnir/MessagePipe/R3/ZLogger/Ulid 等）
- EF Core + PostgreSQL 17 + Ulid 主键策略 + 首次迁移
- JWT 认证 + 6 角色 RBAC
- MudBlazor 双主题（暗色 #CBA6F7 / 亮色 #8F6AAF）
- ZLogger 结构化日志
- OrbStack 本地开发环境（Docker Compose）

### 阶段 1：MVP 核心闭环
- **M01 工单管理**：ProductionOrder 5 状态机 + Cleipnir Saga 31 工序 × 7 站编排 + 工单 CRUD/校验
- **M02 物料管理**：BOM 内存缓存 + GS1-128 扫码入库 + 线边库存监控 + JIT 看板 + Poka-Yoke 防错 + 物料反冲
- **M04 全链路追溯**：4 级追溯模型 + SHA-256 哈希链 + 正反向查询（≤30s / ≤60s）

### 阶段 2：质量体系
- **M03 SPC**：SpcCalculator（Cpk/Ppk/X̄-R + Western Electric 8 判异规则）+ IQC/IPQC/首件检验 + 100% 液压测试 + NCR/8D + 质量报表（QuestPDF）
- **M05 设备管理**：8 设备 PLC 驱动（OPC UA/EtherNet/IP/Modbus TCP/Profinet）+ Channel 背压管道 + R3 OEE 计算 + SignalR 实时推送 + 预防性维护 + 备件管理
- **M06 Andon 报警**：三级上报 L1/L2/L3 + R3 防抖管道 + ESP 专用报警 + Andon 看板

### 阶段 3：集成
- **M07 工艺管理**：31 工序 × 7 站工艺路线 + ECO 版本控制 + 防错三重校验
- **M08 SQE**：供应商评分模型 + PPAP 18 项文档 + 关键供应商管控
- **M09 排程**：有限产能引擎 + 8 设备 × 3 班次 + 紧急插单 + ECharts 甘特图
- **SAP 对接**：工单双向同步 + 库存同步 + 物料移动凭证同步 + Webhook 拒单回写

### 阶段 4：优化
- **M10 报表**：通用模板引擎 + OEE 日报 + 月度综合报表 + 定时邮件推送
- **离线模式**：OfflineSyncRecord + Channel 5000 缓存 + 4 个 REST API + 后台重试/清理
- **断网重连**：SagaReconciliationService + OfflineReplayService + ReconnectionBackgroundService + 3 个 REST API
- **性能压测**：43 基准（零分配 14 / PLC 吞吐 8 / 追溯 10 / SignalR 11），[MemoryDiagnoser] 验证 100% 零堆分配
- **混沌工程**：Saga 崩溃恢复 11 测试 + SignalR 自动重连 9 测试 + PLC 断连 6 测试
- **合规文档**：IATF 16949 条款覆盖矩阵 + ISO 26262 ASIL-D 工具验证 + 审计追踪

### 部署
- 生产 Docker Compose + Dockerfiles
- Uncloud 集群（WireGuard mesh + Caddy HTTPS + 多机扩展）
- PostgreSQL 3 级备份（pg_dump + WAL PITR + 异地存储）
- 零停机滚动部署 + 终端 VPN 接入（Tailscale/Headscale/WireGuard）

### 测试
| 套件 | 数量 |
|------|------|
| MesAdmin.Domain.Tests | 166 单元测试 |
| MesAdmin.Application.Tests | 217 集成+混沌测试 |
| **总计** | **383 · 0 failed · 0 skipped** |

### 性能基准
| 套件 | 方法数 |
|------|--------|
| ZeroAllocationBenchmarks | 14 |
| PlcThroughputBenchmarks | 8 |
| TraceabilityQueryBenchmarks | 10 |
| SignalRPushBenchmarks | 11 |
| **总计** | **43** |

### 技术栈
- .NET 10 · C# 13 · PostgreSQL 17
- MudBlazor 9.6.0 · MemoryPack 1.21.4 · Cleipnir 4.2.5
- MessagePipe 1.8.2 · R3 1.3.1 · ZLogger 2.5.10
- Ulid 1.4.1 · MasterMemory 3.0.4 · QuestPDF
- SignalR · Channels · Pipelines · Testcontainers
- Docker Compose · Uncloud · GreptimeDB · vmalert
// CI test commit - trigger workflow
