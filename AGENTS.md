# AutoMES — AI 编程助手指南

> **面向：** Claude Code / Cursor / Copilot / 任何 AI 编程助手
> **项目：** 博世 ESP® 制动系统 MES（汽车 Tier-1 供应商制造执行系统）
> **目标平台：** .NET 10 · ASP.NET Core · PostgreSQL 17 · Docker

---

## 1. 项目一句话

为博世 ESP® 制动系统总成产线（7 站 31 工序）构建的全链路 MES，覆盖工单→物料→SPC→追溯→Andon→OEE。所有流程由 Cleipnir Saga 保证崩溃恢复零丢失；热路径零堆分配。

---

## 2. 技术栈（精确版本，禁止降级）

| 类别 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET SDK | 10.0 |
| UI | MudBlazor | 9.6.0 |
| 序列化 | MemoryPack | 1.21.4 |
| 工作流 | Cleipnir.ResilientFunctions | 4.2.5 |
| 消息（进程内） | MessagePipe | 1.8.2 |
| 响应式 | R3 | 1.3.1 |
| 日志 | ZLogger | 2.5.10 |
| 主键 | Ulid | 1.4.1 |
| UI 集合 | ObservableCollections | 3.3.4 |
| 缓存 | MasterMemory | 3.0.4 |
| 数据库 | Npgsql EF Core + PostgreSQL 17 | 10.0.2 |
| 实时通信 | SignalR (MemoryPack 二进制) | ASP.NET Core 10 |
| IO 管道 | System.Threading.Channels | .NET 10 |
| 网络 IO | System.IO.Pipelines | .NET 10 |
| 图表 | ECharts (JSInterop) | 5.6+ |
| 甘特图 | Bryntum Gantt (JSInterop) | 6.x |
| 部署 | Docker Compose + Uncloud (WireGuard) | — |

---

## 3. 项目结构

```
mes-admin/
├── MesAdmin.sln
└── src/
    ├── MesAdmin.Domain/          # 领域模型 + MemoryPack + Ulid
    │   └── Models/               # ProductionOrder, Equipment, QualityRecord...
    ├── MesAdmin.Application/     # Cleipnir Saga + 业务接口
    │   └── Sagas/                # ProductionOrderSaga (31工序编排)
    ├── MesAdmin.Infrastructure/  # EF Core + SignalR + PLC + Channels + Pipelines
    │   ├── Data/                 # MesDbContext (Ulid转换器)
    │   ├── Hubs/                 # DashboardHub (SignalR)
    │   ├── Plc/                  # OpcUaPlcClient + PlcDataAcquisitionPipeline
    │   └── RealTime/             # OeeReactivePipeline (R3) + MessagePipe 消息
    ├── MesAdmin.Web/             # MudBlazor 管理后台
    │   ├── Components/
    │   │   ├── Layout/           # MainLayout + NavMenu
    │   │   ├── Pages/            # Dashboard, Production, OeeDashboard, Quality...
    │   │   └── Shared/           # EChartsChart (JSInterop)
    │   └── wwwroot/js/           # echarts-interop.js, signalr-client.js
    └── MesAdmin.Api/             # REST API (Avalonia 工位终端用)
        └── Controllers/
```

**依赖方向：** `Web → Infrastructure → Application → Domain` ← `API → Infrastructure`

---

## 4. 核心架构原则

### 4.1 分层铁律

| 层 | 职责 | 禁止事项 |
|-----|------|---------|
| **Domain** | 纯 POCO 模型 + 枚举。所有模型标注 `[MemoryPackable]` | 禁止引用任何框架包（除 MemoryPack + Ulid） |
| **Application** | Cleipnir Saga 定义 + 接口（`IPlcClient`, `IMesDbContext`） | 禁止包含数据库/网络实现 |
| **Infrastructure** | EF Core、SignalR Hub、PLC 客户端、MessagePipe、R3 管道 | 禁止包含业务逻辑 |
| **Web** | MudBlazor 页面 + JSInterop + SignalR 客户端 | 禁止直接访问数据库 |

### 4.2 Saga 铁律

```
Effect.AtLeastOnce  → 必须幂等保护（唯一约束 / 先读后写 / Version 乐观锁）
Effect.AtMostOnce   → 重放时复用上次结果，不重复执行副作用
Effect 之外          → 安全联锁、人工确认 —— 重放时重新实时评估
Effect 内            → 禁止读 Redis / 外部缓存（重放时缓存可能已失效）
```

### 4.3 零分配铁律（热路径强制）

- PLC 数据解析：`Span<T>` / `ReadOnlySpan<T>`，禁止 `byte[]` + `BitConverter` 分配版本
- 高频缓冲：`ArrayPool<T>.Shared` 租用/归还
- 协议帧扫描：`SearchValues<T>` + SIMD
- 网络 IO：`PipeReader` / `PipeWriter`，禁止裸 `Stream.ReadAsync`
- PLC 数据队列：`Channel<T>`（Bounded），禁止 `BlockingCollection<T>`
- 日志：`ZLogger`（`IBufferWriter<byte>` 直写），禁止字符串拼接

### 4.4 序列化铁律

- 进程间通信（SignalR）：**强制 MemoryPack 二进制**，禁止 JSON
- API 响应：默认 JSON，支持 `Accept: application/x-memorypack` 请求二进制
- Saga 状态持久化：MemoryPack → PostgreSQL JSONB
- 领域模型：统一 `[MemoryPackable] public partial class`

### 4.5 主键铁律

- 所有实体主键使用 `Ulid`（可排序 UUID），通过 `UlidToGuidConverter` 存入 PostgreSQL `uuid` 列
- 禁止自增 ID（分布式不安全）
- 禁止随机 Guid（索引碎片）

---

## 5. 编码公约

### 5.1 C# 版本与语法

- 使用 C# 13（随 .NET 10）
- 优先使用 `ref struct`、`stackalloc`、`scoped` 等栈分配关键字
- `record` 用于领域事件和 DTO
- `record struct` 用于性能敏感的小型值类型

### 5.2 命名约定

```csharp
// 实体：PascalCase
public partial class ProductionOrder { }

// 私有字段：_camelCase
private readonly IPlcClient _plcClient;

// 方法：PascalCase + 动词前缀
public async Task BindMaterialBatchAsync(Ulid orderId, string batchNo) { }

// Saga Effect ID：kebab-case + 语义标识符
await workflow.Effect.Capture("Torque-M6-FL", async () => { ... }, ResiliencyLevel.AtLeastOnce);
```

### 5.3 异步规范

- 所有 I/O 操作强制 `async/await`，禁止 `.Result` / `.Wait()`
- `CancellationToken` 必须从上层传递，禁止 `CancellationToken.None`
- `async void` 仅允许用于 SignalR Hub 事件处理器

### 5.4 日志规范

```csharp
// ✅ 使用 ZLogger 结构化日志
_logger.ZLogInformation($"工单 {order.OrderNumber} 工序 {op.Sequence} 完成");

// ❌ 禁止字符串插值 + 默认 ILogger
_logger.LogInformation($"工单 {order.OrderNumber} 完成");
```

---

## 6. Cleipnir Saga 模板

```csharp
public async Task Execute(ProductionOrder order, Workflow workflow)
{
    var state = await workflow.States.CreateOrGetDefault<OrderState>();

    // ── 安全联锁：Effect 之外 ──
    if (equipment.Status == EquipmentStatus.Alarm)
        throw new SafetyInterlockException();

    // ── 设备就绪确认：AtMostOnce ──
    await workflow.Effect.Capture("EnsureReady-10", async () => {
        if (!await _plc.IsReadyAsync(equipment.PlcAddress))
            throw new Exception("设备未就绪");
    }, ResiliencyLevel.AtMostOnce);

    // ── 加工操作：AtLeastOnce（幂等保护：先读后写）──
    await workflow.Effect.Capture("WriteParam-10-Torque", async () => {
        var current = await _plc.ReadAsync(address, tag);
        if (!Equals(current, targetValue))
            await _plc.WriteAsync(address, command);
    }, ResiliencyLevel.AtLeastOnce);

    // ── 完工确认：AtLeastOnce（幂等保护：DB唯一约束）──
    await workflow.Effect.Capture("CompleteOp-10", async () => {
        await _db.CompleteOperation(order.Id, sequence);
    }, ResiliencyLevel.AtLeastOnce);
}
```

---

## 7. MessagePipe 消息发布模板

```csharp
// 发布（通常在 Saga Effect 内）
private readonly IAsyncPublisher<OrderStatusChanged> _publisher;

await workflow.Effect.Capture("PublishStatusChanged", async () => {
    await _publisher.PublishAsync(new OrderStatusChanged(
        order.Id, order.OrderNumber, oldStatus, newStatus));
}, ResiliencyLevel.AtLeastOnce);

// 订阅（在 Infrastructure 层注册）
builder.Services.AddMessagePipe();
builder.Services.AddSingleton<IAsyncSubscriber<OrderStatusChanged>>(
    sp => /* → DashboardPushService / AuditLogger */);
```

---

## 8. R3 响应式管道模板

```csharp
// OEE 计算管道
_plcStream
    .Where(s => s.EquipmentCode == "EQ-CNC-01")
    .Sample(TimeSpan.FromSeconds(5))
    .Select(s => ComputeOee(s))
    .Subscribe(async oee => await _push.PushOeeUpdate(equipmentId, oee));

// Andon 防抖报警
_plcStream
    .ThrottleFirst(TimeSpan.FromSeconds(5))
    .Where(s => s.Value > upperLimit)
    .Subscribe(s => _andon.Trigger(s));
```

---

## 9. MudBlazor UI 规范

### 9.1 主题

- **暗色主题**（默认）：深邃工业风，薰衣草紫 `#CBA6F7` 为主色调
- **亮色主题**（车间终端）：洁净工业风，深紫 `#8F6AAF` 为主色调
- 全局圆角 `12px`
- 字体：`Inter Tight`

### 9.2 组件选择

| 场景 | MudBlazor 组件 | 替代/增强 |
|------|---------------|----------|
| 数据表格 | `MudTable<T>` | ObservableCollections 增量绑定 |
| 图表 | `EChartsChart` (JSInterop) | MudBlazor 无内置图表 |
| 甘特图 | Bryntum Gantt (JSInterop) | MudBlazor 无内置甘特图 |
| KPI 卡片 | 自定义 `glass-kpi-card` CSS 类 | 玻璃态 + 微动效 |
| 报警通知 | `MudAlert` + Andon 脉冲动画 | CSS `andon-pulse` |
| 设备状态 | `MudChip` + 状态指示器 | 发光圆点 + 颜色映射 |

### 9.3 自定义 CSS 类（必须定义）

- `.glass-kpi-card` — 玻璃态 KPI 卡片
- `.andon-pulse` — Andon 报警脉冲动画
- `.status-dot` — 设备状态指示器（绿/橙/红发光圆点）
- `.grad-border` — 动画渐变边框

---

## 10. 数据库规范

### 10.1 迁移

- 使用 EF Core Code-First Migration
- Migration 命名：`{YYYYMMDD}_{描述}`，如 `20260701_AddTraceabilityLinks`
- 禁止手动修改已应用的 Migration

### 10.2 索引策略

- 所有外键列必须建索引
- 所有 `WHERE`/`ORDER BY` 常用列必须建索引
- 追溯表（`traceability_links`）对 `vin_or_serial`、`component_batch`、`material_batch` 各建索引

### 10.3 查询规范

- 禁止 `SELECT *`，使用 `.Select()` 投影
- 禁止 EF Core 客户端评估（`AsEnumerable()` 之前必须完成所有过滤）
- 高频只读查询优先使用 `MasterMemory` 内存缓存

---

## 11. 测试规范

### 11.1 单元测试

- Cleipnir Saga：使用 `InMemoryFunctionStore` 测试 Saga 流程
- MessagePipe：使用 `TestPublisher<T>` / `TestSubscriber<T>`
- R3 管道：使用 `TestScheduler` 模拟时间

### 11.2 集成测试

- 使用 `Testcontainers.PostgreSql` 启动真实 PostgreSQL
- 测试 Saga 崩溃恢复：中断 `CancellationToken` 后验证 Effect 重新执行

### 11.3 混沌工程

- 阶段 4 必须执行：随机杀进程 → 验证 Saga 恢复
- 随机断网 → 验证 SignalR 自动重连
- 随机拔 PLC → 验证 Channel 背压 + 缓冲

---

## 12. 禁止事项清单

| ❌ 禁止 | ✅ 替代 |
|---------|--------|
| `Guid.NewGuid()` 作为实体主键 | `Ulid.NewUlid()` |
| `System.Text.Json` 用于内部通信 | `MemoryPack` |
| `Microsoft.Extensions.Logging.ILogger` 默认实现 | `ZLogger` |
| `BlockingCollection<T>` | `Channel<T>` |
| `Stream.ReadAsync` 裸用 | `PipeReader` / `PipeWriter` |
| `byte[]` 分配式解析 | `Span<T>` / `ReadOnlySpan<T>` |
| `string.Substring` / `string.Format` 在高频路径 | `ReadOnlySpan<char>` / `ZString` |
| Saga Effect 内读 Redis | 读本地 DB 或通过 `AtMostOnce` 缓存 |
| 忽略 `CancellationToken` | 必须从 Action/Controller 层传入 |
| EF Core `AsEnumerable()` 在 `Where` 之前 | 先完成查询再 `AsEnumerable()` |

---

## 13. 快速启动命令

```bash
# 开发环境
docker compose -f docker/compose.yaml up postgres -d
cd src/MesAdmin.Web && dotnet run

# 运行测试
dotnet test

# 生成 EF Core Migration
dotnet ef migrations add 20260701_Description \
    --project src/MesAdmin.Infrastructure \
    --startup-project src/MesAdmin.Web

# Docker 部署
docker compose -f docker/compose.yaml up -d
```

---

## 14. 相关文档

| 文档 | 路径 |
|------|------|
| PRD v2（博世ESP版） | `deliverables/TradeDesk/automotive-mes-prd-v2.html` |
| TAD v2（零分配架构） | `deliverables/TradeDesk/automotive-mes-tad-v2.html` |
| Cysharp 全家桶适配分析 | `workfiles/mes-admin/docs/cysharp-adoption-analysis.md` |

---

*本文件是 AI 编程助手在该项目中的行为宪法。所有代码生成、修改、审查必须遵循以上规范。违反核心架构原则（第 4 节）的代码不得合并。*
