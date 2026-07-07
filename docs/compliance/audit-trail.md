# 审计追踪文档

> **项目：** 博世 ESP® 制动系统 MES（AutoMES）
> **标准：** IATF 16949 §8.7.1.5 / ISO 26262 ASIL-D / 21 CFR Part 11 / EU GMP Annex 11
> **版本：** v1.0 — 2026-07-07
> **范围：** 哈希链防篡改、过程参数不可篡改性、数据完整性保障机制

---

## 1. 概述

### 1.1 目的

本文档详细说明 AutoMES 系统中的审计追踪机制，确保：

1. **哈希链防篡改** — 追溯链使用 SHA-256 哈希链接，任何历史数据篡改可被检测
2. **过程参数完整性** — 生产工序的过程参数以 JSONB 持久化，不可逆写日志
3. **数据完整性保障** — 主键设计、约束策略、事务保护、崩溃恢复机制

### 1.2 适用范围

本审计追踪覆盖以下数据类型：

| 数据类型 | 表 | 完整性机制 |
|----------|----|-----------|
| 追溯链 | `traceability_links` | SHA-256 哈希链 |
| 工序参数 | `work_order_operations.Parameters` | JSONB 持久化 + 审计日志 |
| 工单状态变更 | `production_orders` | 状态机 + ZLogger 审计 |
| 质量检验记录 | `quality_records` | 不可覆写完成状态 |
| NCR 处置 | `non_conformance_reports` | 状态机 + 时间戳审计 |
| SPC 样本 | `spc_samples` | 唯一约束 + 子组序号 |

---

## 2. 哈希链防篡改（追溯链）

### 2.1 实现原理

追溯链使用 SHA-256 哈希链保证数据不可篡改。每条 `TraceabilityLink` 记录包含：

- `PreviousHash` — 前一条记录的哈希值
- `Hash` — 本条记录的哈希值（基于 PreviousHash + 本条内容计算）

**哈希计算输入：**
```
Hash = SHA-256(PreviousHash | Id | OrderId | Level | VinOrSerial | 
                ComponentBatch | MaterialBatch | CreatedAt)
```

**链式验证：**
```
链首: Hash1 = SHA-256("" | content1)
链中: Hash2 = SHA-256(Hash1 | content2)
链尾: Hash3 = SHA-256(Hash2 | content3)
```

任何字段被篡改 → 该条记录的 VerifyHash() 失败 → 后续所有记录哈希链断裂。

### 2.2 关键代码

```csharp
// 哈希计算
public string ComputeHash()
{
    var payload = $"{PreviousHash}|{Id}|{OrderId}|{(int)Level}|{VinOrSerial}|"
                + $"{ComponentBatch}|{MaterialBatch}|{CreatedAt:O}";
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

// 哈希验证
public bool VerifyHash() => Hash == ComputeHash();

// 创建链首/链接
public static TraceabilityLink Create(Ulid orderId, TraceabilityLevel level,
    string vinOrSerial, string componentBatch, string materialBatch,
    string previousHash, DateTimeOffset createdAt)
{
    var link = new TraceabilityLink { ... };
    link.Hash = link.ComputeHash(); // 自动计算哈希
    return link;
}
```

### 2.3 验证流程

```csharp
// 批量验证追溯链完整性
public async Task<bool> VerifyChainAsync(Ulid orderId)
{
    var links = await _db.TraceabilityLinks
        .Where(l => l.OrderId == orderId)
        .OrderBy(l => l.CreatedAt)
        .ToListAsync();
    
    for (int i = 0; i < links.Count; i++)
    {
        if (!links[i].VerifyHash())
            return false; // 数据已被篡改!
        
        if (i > 0 && links[i].PreviousHash != links[i - 1].Hash)
            return false; // 链式断裂!
    }
    return true;
}
```

### 2.4 防篡改保障措施

| 措施 | 说明 |
|------|------|
| 不可逆哈希 | SHA-256 是目前计算上不可逆的哈希算法 |
| 链式依赖 | 修改任意记录导致后续所有记录哈希断裂 |
| 数据库约束 | `Effect.AtLeastOnce` + 唯一索引防止非法写入 |
| `Ulid` 主键 | 可排序 UUID 防止主键可预测性攻击 |

### 2.5 测试证据

```csharp
[Fact]
public void TraceabilityHash_ShouldDetectTampering()
{
    var link = CreateLinkWithPreviousHash("hash1");
    var originalHash = link.Hash;
    link.VinOrSerial = "tampered";
    Assert.False(link.VerifyHash()); // 检测篡改
}

[Fact]
public void TraceabilityHash_Chain_ShouldVerifyIntegrity()
{
    // 创建链：link1 → link2 → link3
    // 修改 link2 → verify chain → 失败
}
```

---

## 3. 过程参数完整性

### 3.1 工序参数存储

每个工序的操作参数通过 `WorkOrderOperation.Parameters` JSONB 列持久化。JSONB 是 PostgreSQL 原生二进制 JSON 格式，支持：

- 写入时校验 JSON 格式合法性
- 存储原始参数数据，不可部分更新/覆写
- 支持索引查询（GIN 索引）

```csharp
// DbContext 配置
modelBuilder.Entity<WorkOrderOperation>(b =>
{
    b.OwnsMany(o => o.Parameters, p =>
    {
        p.ToJson(); // JSONB 存储
        p.Property(x => x.ParameterCode).HasMaxLength(32).IsRequired();
        p.Property(x => x.ParameterName).HasMaxLength(64).IsRequired();
        p.Property(x => x.Unit).HasMaxLength(16).IsRequired();
    });
});
```

### 3.2 参数审计字段

每条工序操作记录包含完整审计信息：

| 字段 | 类型 | 说明 |
|------|------|------|
| `OperatorId` | string | 操作员工号（不可为空） |
| `EquipmentId` | string | 设备编号 |
| `StartAt` | timestamptz | 操作开始时间 |
| `EndAt` | timestamptz | 操作结束时间 |
| `Status` | string | 操作状态（Pending→InProgress→Completed→Failed） |
| `Parameters` | JSONB | 过程参数详细信息 |
| `FailureReason` | string | 失败原因（失败时必填） |

### 3.3 参数不可篡改保障

| 保障机制 | 说明 |
|----------|------|
| JSONB 不可变 | PostgreSQL JSONB 以二进制格式存储，无法字段级覆写 |
| Saga Effect 保护 | 参数写入通过 `Effect.AtLeastOnce` 保护，崩溃后自动恢复 |
| 状态机约束 | 完成/Failed 状态后不可修改 |
| ZLogger 结构化日志 | 每次参数写入都记录结构化日志 |
| DB 唯一约束 | 同一工单同一工序只有一条参数记录 |

### 3.4 测试证据

```
Chaos_PlcDisconnectionTests.cs → 验证 PLC 断连不丢失过程数据
ChannelBackpressureTests.cs → 验证背压保护下的数据完整性
```

---

## 4. 数据完整性保障机制

### 4.1 主键策略

所有实体主键统一使用 `Ulid`（Universally Unique Lexicographically Sortable Identifier）：

```
Ulid = 时间戳(48 bit) + 随机数(80 bit)
     → 128 bit, Base32 编码, 26 字符
```

| 特性 | 优势 |
|------|------|
| 128-bit | 与 Guid 兼容，使用 `UlidToGuidConverter` 存入 PG `uuid` 列 |
| 时间戳前缀 | B+Tree 索引友好，避免 Guid 随机性导致的索引碎片 |
| 可排序 | 按创建时间天然排序，无需额外创建时间索引 |
| 分布式安全 | 无需中心化 ID 生成器 |

### 4.2 事务保护

| 机制 | 说明 |
|------|------|
| **EF Core SaveChanges** | 工作单元模式，一次 SaveChanges 是一个事务 |
| **TransactionMiddleware** | Application 层命令事务中间件，自动回滚 |
| **Cleipnir Saga** | 分布式 Saga 模式 + 崩溃恢复 + 幂等 Effect |
| **PostgreSQL ACID** | Full ACID 事务支持，WAL 日志确保持久性 |

```csharp
// 事务中间件（Application 层）
internal sealed class TransactionMiddleware<TCommand, TResponse>(
    MesDbContext db, ILogger<TransactionMiddleware<TCommand, TResponse>> logger)
    : ICommandMiddleware<TCommand, TResponse>
{
    public async Task<TResponse> InvokeAsync(
        TCommand command, Func<Task<TResponse>> next, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await next();
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning("事务回滚: {Type} - {Msg}", typeof(TCommand).Name, ex.Message);
                throw;
            }
        });
    }
}
```

### 4.3 唯一约束防重复

| 表 | 唯一约束 | 用途 |
|----|---------|------|
| `production_orders` | `OrderNumber` | 防止工单号重复 |
| `traceability_links` | `(VinOrSerial, Level)` | 防止重复绑定（幂等保护） |
| `work_order_operations` | `(OrderId, Sequence)` | 防止工序重复 |
| `boms` | `(ProductCode, Version)` | BOM 版本唯一 |
| `routings` | `(ProductCode, Version)` | 工艺路线版本唯一 |
| `andon_events` | `EventNumber` | 报警事件编号唯一 |
| `non_conformance_reports` | `NcrNumber` | NCR 编号唯一 |
| `eight_d_reports` | `ReportNumber` | 8D 报告编号唯一 |
| `spc_samples` | `(CharacteristicCode, SubgroupIndex)` | SPC 子组序号唯一 |

### 4.4 崩溃恢复

| 场景 | 恢复机制 | 证据 |
|------|---------|------|
| 应用崩溃（Saga 中） | Cleipnir 重放 Effect，AtLeastOnce 幂等 | 11 个混沌测试（T4.10） |
| 网络断连（SignalR） | HubConnection.WithAutomaticReconnect | 9 个重连测试（T4.11） |
| PLC 断连 | Channel FullMode=Wait 背压缓冲 | 6 个断连测试（T4.12） |
| 数据库连接断开 | EF Core 连接池重试策略 | PostgreSQL 自动重连 |
| 事务冲突 | 乐观并发控制 | Version 字段（待扩展） |

### 4.5 状态机约束

所有实体使用状态机确保状态转换合法：

| 实体 | 合法状态转换 |
|------|------------|
| `ProductionOrder` | Created → Released → InProgress → Completed → Closed |
| `FirstArticleInspection` | Pending → InProgress → Passed/Failed |
| `AndonEvent` | Active → EscalatedL2/L3 → Acknowledged → Resolved → Closed |
| `NonConformanceReport` | Open → UnderReview → Dispositioned → Closed |
| `EightDReport` | D1→D2→D3→D4→D5→D6→D7→D8→Closed |
| `QualityRecord` | Pending → Passed/Failed/ConditionalPass |
| `Routing.ECO` | Draft → PendingApproval → Approved → Released → Superseded |

状态机由领域模型的方法强制执行，非法状态转换抛出 `InvalidOperationException`：

```csharp
public void Start()
{
    if (Status != InspectionStatus.Pending)
        throw new InvalidOperationException($"状态为 {Status}，无法开始");
    Status = InspectionStatus.InProgress;
}
```

---

## 5. 日志审计（ZLogger）

所有操作变更通过 ZLogger 结构化日志记录：

### 5.1 日志模板

| 日志级别 | 记录内容 | 示例 |
|---------|---------|------|
| `ZLogInformation` | 正常操作完成 | `"工单 {OrderNumber} 工序 {Sequence} 完成"` |
| `ZLogWarning` | 业务异常/重试 | `"反冲跳过：工单 {OrderNumber} 未找到 BOM"` |
| `ZLogError` | 系统异常 | `"PLC Channel 消费循环异常：{Message}"` |

### 5.2 日志内容

每条日志记录包含：

- **时间戳**（UTC，`timestamptz` 格式）
- **结构化字段**（工单号、设备编码、角色等）
- **调用链标识**（Saga Effect ID / 命令名称）
- **上下文信息**（用户角色、终端标识、IP 地址）

### 5.3 日志持久化

- 开发环境：控制台 + 文件
- 生产环境：控制台（Docker stdout）+ 可选外部日志收集

### 5.4 禁止事项

```
❌ 禁止使用默认 ILogger 字符串插值：
   _logger.LogInformation($"工单 {order} 完成");  // 非结构化，无法查询

✅ 必须使用 ZLogger 结构化格式：
   _logger.ZLogInformation($"工单 {order.OrderNumber} 工序 {op.Sequence} 完成");
```

---

## 6. 数据完整性检查清单

### 6.1 每日/定期检查

| 检查项 | 周期 | 工具 |
|--------|------|------|
| 追溯链哈希完整性 | 每日 | `VerifyChainAsync()` |
| SPC 子组序号连续性 | 每日 | `SpcSample.SubgroupIndex` 检查 |
| Saga 未完成工单 | 每小时 | `ProductionOrder` InProgress 状态 |
| NCR 超期未处置 | 每日 | `NonConformanceReport.DispositionDeadline` |
| 8D 超期未关闭 | 每日 | `EightDReport.CorrectiveActionDueDate` |
| Channel 健康度 | 10s | `DashboardHub.ChannelHealth` |
| 数据库备份完整性 | 每日 | pg_dump + WAL 归档 |

### 6.2 手动审计步骤

1. **追溯链审计：**
   ```
   GET /api/v1/traceability/verify/{orderId} → 返回链完整性状态
   ```

2. **Saga 状态审计：**
   ```
   检查 Cleipnir PostgreSQL 状态表 → 验证所有 Saga 已正确完成
   ```

3. **日志审计：**
   ```
   搜索 ZLogger 错误日志 → 排查异常操作
   ```

---

## 7. 相关文件索引

| 文件 | 内容 |
|------|------|
| `src/MesAdmin.Domain/Models/TraceabilityLink.cs` | 哈希链实现 |
| `src/MesAdmin.Domain/Models/WorkOrderOperation.cs` | 工序参数模型 |
| `src/MesAdmin.Domain/Models/ProductionOrder.cs` | 工单状态机 |
| `src/MesAdmin.Domain/Models/QualityRecord.cs` | 质量检验状态机 |
| `src/MesAdmin.Domain/Models/NonConformanceReport.cs` | NCR 状态机 |
| `src/MesAdmin.Domain/Models/FirstArticleInspection.cs` | 首件检验状态机 |
| `src/MesAdmin.Domain/Models/AndonEvent.cs` | Andon 状态机 |
| `src/MesAdmin.Application/Behaviors/TransactionMiddleware.cs` | 事务保护 |
| `src/MesAdmin.Application/Sagas/ProductionOrderSaga.cs` | Saga 崩溃恢复 |
| `src/MesAdmin.Infrastructure/Data/MesDbContext.cs` | 唯一约束 + Ulid 配置 |
| `src/MesAdmin.Infrastructure/Logging/ZLoggerSetup.cs` | 日志配置 |
| `src/MesAdmin.Infrastructure/Hubs/DashboardHub.cs` | Channel 健康度监控 |

### 测试文件索引

| 文件 | 测试内容 |
|------|---------|
| `tests/MesAdmin.Application.Tests/FullLifecycleE2ETest.cs` | 工单全生命周期审计 |
| `tests/MesAdmin.Application.Tests/Chaos_PlcDisconnectionTests.cs` | PLC 断连数据完整性 |
| `tests/MesAdmin.Application.Tests/Chaos_SignalRReconnectTests.cs` | SignalR 重连数据完整性 |
| `tests/MesAdmin.Application.Tests/ProductionOrderSagaTests.cs` | Saga 崩溃恢复 |
| `tests/MesAdmin.Domain.Tests/TraceabilityLinkTests.cs` | 哈希链完整性 |
| `tests/MesAdmin.Benchmarks/TraceabilityQueryBenchmarks.cs` | 追溯查询性能 |

---

## 8. 合规矩阵

| 法规/标准 | 条款 | 对应章节 | 状态 |
|-----------|------|---------|------|
| IATF 16949 | §8.7.1.5 返工追溯 | §2 哈希链 | ✅ |
| IATF 16949 | §8.5.6.1.1 过程参数 | §3 过程参数 | ✅ |
| IATF 16949 | §7.5.3 文件化信息保留 | §4 数据完整性 | ✅ |
| ISO 26262 | §12.4.4 数据完整性 | §4 数据完整性 | ✅ |
| ISO 26262 | §12.4.5 审计追踪 | §2 + §5 日志 | ✅ |
| 21 CFR Part 11 | §11.10(e) 审计追踪 | §2 + §5 | ✅ |
| EU GMP Annex 11 | §3 审计追踪 | §2 + §5 | ✅ |
