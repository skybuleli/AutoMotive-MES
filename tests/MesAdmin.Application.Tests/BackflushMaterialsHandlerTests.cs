using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// T1.17 BackflushMaterialsHandler 单元测试。
/// 用 Fake 仓储验证 BOM 展开 → FIFO 批次扣减 → 差异报告 → SAP 同步记录 完整逻辑。
/// </summary>
public class BackflushMaterialsHandlerTests
{
    private static readonly ILogger<BackflushMaterialsHandler> Logger = NullLogger<BackflushMaterialsHandler>.Instance;

    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: 标准反冲 — 无差异
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStockMatchesBom_ShouldConsumeAndCreateSapRecords()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSampleBom());

        // 为每种关键物料创建 1 个 Qualified 批次，数量充足
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");
        repos.Batches.AddQualifiedBatch("HCU-ESP9-001", 100, "BAT-HCU-001");
        repos.Batches.AddQualifiedBatch("MOT-ESP9-001", 100, "BAT-MOT-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ConsumedCount);
        Assert.Equal(0, result.VarianceCount);  // 无差异（标准=消耗=100）
        Assert.Equal(3, result.SapSyncCount);
        Assert.Empty(result.Warnings);

        // 验证消耗记录
        Assert.Equal(3, repos.Consumptions.AddedConsumptions.Count);
        Assert.All(repos.Consumptions.AddedConsumptions, c =>
        {
            Assert.Equal(100, c.StandardQuantity);
            Assert.Equal(100, c.ConsumedQuantity);
            Assert.Equal(0, c.VarianceQuantity);
        });

        // 验证 SAP 同步
        Assert.Equal(3, repos.SapSyncRecords.AddedRecords.Count);
        Assert.All(repos.SapSyncRecords.AddedRecords, r =>
        {
            Assert.Equal("261", r.MovementType);
            Assert.Equal(100, r.Quantity);
            Assert.False(r.SapSynced);
        });

        // 无差异报告
        Assert.Empty(repos.Variances.AddedReports);

        // 验证批次库存已扣减
        Assert.Equal(0, repos.Batches.GetBatch("BAT-ECU-001")!.RemainingQuantity);
        Assert.Equal(0, repos.Batches.GetBatch("BAT-HCU-001")!.RemainingQuantity);
        Assert.Equal(0, repos.Batches.GetBatch("BAT-MOT-001")!.RemainingQuantity);

        Assert.Equal(1, repos.Consumptions.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: 幂等跳过 — 已有消耗记录
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAlreadyBackflushed_ShouldSkip()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSampleBom());
        repos.Consumptions.PresetConsumptions.Add(new MaterialConsumption());  // 已有记录
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);  // 返回现有记录数
        Assert.Equal(0, result.VarianceCount);
        Assert.Equal(0, result.SapSyncCount);
        Assert.Single(result.Warnings);
        Assert.Equal("已执行过反冲，跳过", result.Warnings[0]);

        // 未新增消耗记录
        Assert.Empty(repos.Consumptions.AddedConsumptions);
        // 未消耗批次库存
        Assert.Equal(100, repos.Batches.GetBatch("BAT-ECU-001")!.RemainingQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: BOM 不存在 — 跳过反冲
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenBomNotFound_ShouldSkipWithWarning()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, null);  // BOM 不存在

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.False(result.Success);
        Assert.Equal(0, result.ConsumedCount);
        Assert.Single(result.Warnings);
        Assert.Contains("未找到 BOM", result.Warnings[0]);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: 工单不存在 — 抛 KeyNotFoundException
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotFound_ShouldThrow()
    {
        var repos = new BackflushRepos(null, CreateSampleBom());
        var handler = repos.BuildHandler();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.ExecuteAsync(new BackflushMaterialsCommand(Ulid.NewUlid()), default));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: 工单未完工 — 抛 InvalidOperationException
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotCompleted_ShouldThrow()
    {
        var order = CreateOrder("WO-BF-0005", 100);
        // 订单是 Created 状态，不是 Completed
        var repos = new BackflushRepos(order, CreateSampleBom());
        var handler = repos.BuildHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new BackflushMaterialsCommand(order.Id), default));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 差异超过 2% — 生成异常报告
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenVarianceExceedsThreshold_ShouldCreateVarianceReport()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSampleBom());

        // 库存比标准少 5 件 → 差异 5%
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 95, "BAT-ECU-001");
        repos.Batches.AddQualifiedBatch("HCU-ESP9-001", 100, "BAT-HCU-001");
        repos.Batches.AddQualifiedBatch("MOT-ESP9-001", 100, "BAT-MOT-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ConsumedCount);
        Assert.Equal(1, result.VarianceCount);  // ECU 差异 5%
        Assert.Single(result.Warnings);
        Assert.Contains("ECU-ESP9-001", result.Warnings[0]);
        Assert.Contains("5.0%", result.Warnings[0]);

        // 验证差异报告
        Assert.Single(repos.Variances.AddedReports);
        var report = repos.Variances.AddedReports[0];
        Assert.Equal("ECU-ESP9-001", report.MaterialCode);
        Assert.Equal("偏低", report.Direction);
        Assert.False(report.IsResolved);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: FIFO 顺序 — 老批次优先消耗
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldConsumeOldestBatchFirst()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSingleCriticalBom());

        // 3 个批次，不同生产日期（FIFO 顺序：BAT-OLD → BAT-MID → BAT-NEW）
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 30, "BAT-NEW",
            productionDate: new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 50, "BAT-MID",
            productionDate: new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero));
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 40, "BAT-OLD",
            productionDate: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);

        // FIFO 顺序消耗：BAT-OLD(40) → BAT-MID(50) → BAT-NEW(10)
        var oldBatch = repos.Batches.GetBatch("BAT-OLD")!;
        var midBatch = repos.Batches.GetBatch("BAT-MID")!;
        var newBatch = repos.Batches.GetBatch("BAT-NEW")!;

        Assert.Equal(0, oldBatch.RemainingQuantity);   // 40/40 消耗完
        Assert.Equal(MaterialBatchStatus.Consumed, oldBatch.Status);  // 批次耗尽
        Assert.Equal(0, midBatch.RemainingQuantity);   // 50/50 消耗完
        Assert.Equal(20, newBatch.RemainingQuantity);  // 只消耗了 10/30
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: 有投料绑定 — 优先扣减绑定批次
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldDeductFromBoundBatchesFirst()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSingleCriticalBom());

        // 绑定批次 1（60 件）
        var boundBatch = repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 60, "BAT-BOUND",
            productionDate: new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
        // 非绑定批次 2（80 件，日期更早）
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 80, "BAT-QUALIFIED",
            productionDate: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        // 添加绑定记录（绑定到 boundBatch）
        repos.Bindings.AddBinding(repos.Order.Id, "ECU-ESP9-001", boundBatch.Id, "BAT-BOUND", 60);

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);

        // 优先消耗绑定批次（60），不足再从 Qualified 补（40）
        Assert.Equal(0, repos.Batches.GetBatch("BAT-BOUND")!.RemainingQuantity);  // 60/60 消耗完
        Assert.Equal(40, repos.Batches.GetBatch("BAT-QUALIFIED")!.RemainingQuantity);  // 40/80 消耗
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 9: 非关键物料被跳过
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldSkipNonCriticalItems()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateBomWithMixedCriticality());

        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");
        // 非关键物料不创建库存 → 应被跳过

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);  // 只有 ECU 被处理
        Assert.Equal(1, result.SapSyncCount);   // 只有 ECU 有 SAP 记录
        Assert.Single(repos.Consumptions.AddedConsumptions);
        Assert.Equal("ECU-ESP9-001", repos.Consumptions.AddedConsumptions[0].MaterialCode);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 10: 多物料反冲 — 全部正常消耗
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_MultipleMaterials_ShouldProcessAll()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSampleBom());

        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");
        repos.Batches.AddQualifiedBatch("HCU-ESP9-001", 200, "BAT-HCU-001");
        repos.Batches.AddQualifiedBatch("MOT-ESP9-001", 300, "BAT-MOT-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(3, result.ConsumedCount);
        Assert.Equal(3, result.SapSyncCount);

        // 检查各物料消耗
        Assert.Contains(repos.Consumptions.AddedConsumptions, c => c.MaterialCode == "ECU-ESP9-001" && c.ConsumedQuantity == 100);
        Assert.Contains(repos.Consumptions.AddedConsumptions, c => c.MaterialCode == "HCU-ESP9-001" && c.ConsumedQuantity == 100);
        Assert.Contains(repos.Consumptions.AddedConsumptions, c => c.MaterialCode == "MOT-ESP9-001" && c.ConsumedQuantity == 100);

        // 批次库存正确扣减
        Assert.Equal(0, repos.Batches.GetBatch("BAT-ECU-001")!.RemainingQuantity);
        Assert.Equal(100, repos.Batches.GetBatch("BAT-HCU-001")!.RemainingQuantity);
        Assert.Equal(200, repos.Batches.GetBatch("BAT-MOT-001")!.RemainingQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 11: 批次库存不足 — 尽可能消耗
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStockInsufficient_ShouldConsumeAvailableOnly()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSingleCriticalBom());

        // 只有 60 件库存，需求 100 件
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 60, "BAT-ECU-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);
        Assert.Equal(1, result.VarianceCount);  // 差异 40%
        Assert.Single(result.Warnings);

        // 消耗了 60 件（全部可用库存）
        Assert.Equal(0, repos.Batches.GetBatch("BAT-ECU-001")!.RemainingQuantity);
        var consumption = repos.Consumptions.AddedConsumptions[0];
        Assert.Equal(60, consumption.ConsumedQuantity);
        Assert.Equal(100, consumption.StandardQuantity);
        Assert.Equal(-40, consumption.VarianceQuantity);  // 偏低 40
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 12: 批次状态过滤 — 非 Qualified 批次不可消耗
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldOnlyConsumeQualifiedBatches()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSingleCriticalBom());

        // 1 个 Qualified + 2 个其他状态
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 80, "BAT-OK");
        repos.Batches.AddBatch("ECU-ESP9-001", 100, "BAT-REJECTED",
            MaterialBatchStatus.Rejected, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        repos.Batches.AddBatch("ECU-ESP9-001", 100, "BAT-RECEIVED",
            MaterialBatchStatus.Received, new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero));

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);
        Assert.Equal(1, result.VarianceCount);  // 消耗 80，标准 100

        // 只消耗了 Qualified 批次
        Assert.Equal(0, repos.Batches.GetBatch("BAT-OK")!.RemainingQuantity);
        Assert.Equal(100, repos.Batches.GetBatch("BAT-REJECTED")!.RemainingQuantity);  // 未消耗
        Assert.Equal(100, repos.Batches.GetBatch("BAT-RECEIVED")!.RemainingQuantity);  // 未消耗
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 13: 不合格批次隔离场景 — 跳过 Rejected 批次
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenRejectedBatchExists_ShouldSkipIt()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(50, CreateSingleCriticalBom());

        // 1 个 Rejected 批次在 FIFO 最前面，1 个 Qualified 在后面
        repos.Batches.AddBatch("ECU-ESP9-001", 100, "BAT-REJECTED",
            MaterialBatchStatus.Rejected, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 50, "BAT-OK",
            productionDate: new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);
        Assert.Equal(0, result.VarianceCount);

        // 跳过了 Rejected 批次，从 Qualified 消耗
        Assert.Equal(100, repos.Batches.GetBatch("BAT-REJECTED")!.RemainingQuantity);  // 未消耗
        Assert.Equal(0, repos.Batches.GetBatch("BAT-OK")!.RemainingQuantity);  // 已消耗 50
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 14: 反冲后批次标记为 Consumed
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenBatchFullyConsumed_ShouldMarkConsumed()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateSingleCriticalBom());

        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);

        var batch = repos.Batches.GetBatch("BAT-ECU-001")!;
        Assert.Equal(0, batch.RemainingQuantity);
        Assert.Equal(MaterialBatchStatus.Consumed, batch.Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 15: BOM 有非关键物料有库存 — 不处理非关键
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenNonCriticalHasStock_ShouldNotConsume()
    {
        var (handler, repos) = CreateHandlerWithCompletedOrder(100, CreateBomWithMixedCriticality());

        repos.Batches.AddQualifiedBatch("ECU-ESP9-001", 100, "BAT-ECU-001");
        repos.Batches.AddQualifiedBatch("CAP-MLCC-100N", 5000, "BAT-CAP-001");  // 非关键, 有库存

        var result = await handler.ExecuteAsync(new BackflushMaterialsCommand(repos.Order.Id), default);

        Assert.True(result.Success);
        Assert.Equal(1, result.ConsumedCount);  // 只消耗了 ECU（关键）
        Assert.Single(repos.Consumptions.AddedConsumptions);
        Assert.Equal("ECU-ESP9-001", repos.Consumptions.AddedConsumptions[0].MaterialCode);

        // 非关键物料库存未扣减
        Assert.Equal(5000, repos.Batches.GetBatch("BAT-CAP-001")!.RemainingQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper — 创建已完成状态的工单
    // ═══════════════════════════════════════════════════════════

    private static (BackflushMaterialsHandler Handler, BackflushRepos Repos) CreateHandlerWithCompletedOrder(
        int qualifiedQty, Bom? bom)
    {
        var order = CreateOrder($"WO-BF-{Ulid.NewUlid().ToString()[..8]}", qualifiedQty);
        order.Release();   // Created → Released
        order.Start();     // Released → InProgress
        order.Complete(qualifiedQty, 0, DateTimeOffset.UtcNow);  // InProgress → Completed
        var repos = new BackflushRepos(order, bom);
        return (repos.BuildHandler(), repos);
    }

    private static ProductionOrder CreateOrder(string orderNumber, int plannedQty)
        => ProductionOrder.Create(
            Ulid.NewUlid(), orderNumber, "ESP-9.0",
            Ulid.NewUlid(), "V1.0", plannedQty, 1,
            new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero));

    private static Bom CreateSampleBom()
    {
        var bom = Bom.Create(Ulid.NewUlid(), "ESP-9.0", "V1.0", DateTimeOffset.UtcNow);
        bom.AddItem(BomItem.Create("ECU-ESP9-001", "ECU 电子控制单元 V3", 1, "PCS", level: 2, isCritical: true));
        bom.AddItem(BomItem.Create("HCU-ESP9-001", "HCU 液压控制单元 V2", 1, "PCS", level: 2, isCritical: true));
        bom.AddItem(BomItem.Create("MOT-ESP9-001", "直流无刷电机 48V/120W", 1, "PCS", level: 2, isCritical: true));
        return bom;
    }

    private static Bom CreateSingleCriticalBom()
    {
        var bom = Bom.Create(Ulid.NewUlid(), "ESP-9.0", "V1.0", DateTimeOffset.UtcNow);
        bom.AddItem(BomItem.Create("ECU-ESP9-001", "ECU 电子控制单元 V3", 1, "PCS", level: 2, isCritical: true));
        return bom;
    }

    private static Bom CreateBomWithMixedCriticality()
    {
        var bom = Bom.Create(Ulid.NewUlid(), "ESP-9.0", "V1.0", DateTimeOffset.UtcNow);
        bom.AddItem(BomItem.Create("ECU-ESP9-001", "ECU 电子控制单元 V3", 1, "PCS", level: 2, isCritical: true));
        bom.AddItem(BomItem.Create("CAP-MLCC-100N", "MLCC 100nF/50V", 32, "PCS", level: 3, isCritical: false));
        bom.AddItem(BomItem.Create("RES-SMD-1K", "贴片电阻 1KΩ 1%", 24, "PCS", level: 3, isCritical: false));
        return bom;
    }

    // ═══════════════════════════════════════════════════════════
    //  BackflushRepos — 聚合所有 Fake 仓储 + 构建 Handler
    // ═══════════════════════════════════════════════════════════

    private sealed class BackflushRepos
    {
        public ProductionOrder Order { get; }
        public FakeOrderRepository Orders { get; }
        public FakeBackflushBatchRepository Batches { get; }
        public FakeBomRepository Boms { get; }
        public FakeBindingRepository Bindings { get; }
        public FakeConsumptionRepository Consumptions { get; }
        public FakeVarianceRepository Variances { get; }
        public FakeSapSyncRepository SapSyncRecords { get; }

        public BackflushRepos(ProductionOrder? order, Bom? bom)
        {
            Order = order!;
            Orders = new FakeOrderRepository(order);
            Boms = new FakeBomRepository(bom);
            Batches = new FakeBackflushBatchRepository();
            Bindings = new FakeBindingRepository();
            Consumptions = new FakeConsumptionRepository();
            Variances = new FakeVarianceRepository();
            SapSyncRecords = new FakeSapSyncRepository();
        }

        public BackflushMaterialsHandler BuildHandler()
            => new(Orders, Boms, Batches, Bindings, Consumptions, Variances, SapSyncRecords, Logger);
    }

    // ═══════════════════════════════════════════════════════════
    //  Fake 仓储实现
    // ═══════════════════════════════════════════════════════════

    private sealed class FakeOrderRepository : IProductionOrderRepository
    {
        private readonly ProductionOrder? _order;
        public int SaveChangesCallCount { get; private set; }

        public FakeOrderRepository(ProductionOrder? order) => _order = order;

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
            => Task.FromResult(_order?.OrderNumber == orderNumber ? _order : null);

        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default)
        {
            var list = _order is not null ? new List<ProductionOrder> { _order } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default)
        {
            var list = _order is not null ? new List<ProductionOrder> { _order } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
        {
            var count = _order is not null ? 1 : 0;
            return Task.FromResult(count);
        }

        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default)
        {
            return Task.FromResult(0);
        }

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(ProductionOrder order) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// Fake 物料批次仓储 — 支持跟踪状态的批次，可真实执行 Consume()。
    /// 返回实际的 MaterialBatch 实例，Consume() 方法会真实修改 RemainingQuantity。
    /// </summary>
    private sealed class FakeBackflushBatchRepository : IMaterialBatchRepository
    {
        private readonly List<MaterialBatch> _batches = [];

        /// <summary>添加一个 Qualified 批次。</summary>
        public MaterialBatch AddQualifiedBatch(string materialCode, double qty, string batchNumber,
            DateTimeOffset? productionDate = null)
        {
            var batch = MaterialBatch.Create(materialCode, $"物料-{materialCode}", batchNumber,
                "SUP-TEST", "测试供应商", qty, "PCS", true, productionDate);
            batch.Qualify();
            _batches.Add(batch);
            return batch;
        }

        /// <summary>添加一个指定状态的批次。</summary>
        public MaterialBatch AddBatch(string materialCode, double qty, string batchNumber,
            MaterialBatchStatus status, DateTimeOffset? productionDate = null)
        {
            var batch = MaterialBatch.Create(materialCode, $"物料-{materialCode}", batchNumber,
                "SUP-TEST", "测试供应商", qty, "PCS", true, productionDate);
            // 绕过硬编码状态转换约束（只用于测试特定状态过滤）
            batch.Status = status;
            if (status == MaterialBatchStatus.Qualified)
                batch.Qualify();
            _batches.Add(batch);
            return batch;
        }

        public MaterialBatch? GetBatch(string batchNumber)
            => _batches.FirstOrDefault(b => b.BatchNumber == batchNumber);

        public Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_batches.FirstOrDefault(b => b.Id == id));

        /// <summary>返回跟踪状态的批次（支持 Consume() 调用）。</summary>
        public Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_batches.FirstOrDefault(b => b.Id == id));

        public Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default)
            => Task.FromResult(_batches.FirstOrDefault(b => b.BatchNumber == batchNumber));

        public Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken ct = default)
        {
            var result = _batches.Where(b => materialCode is null || b.MaterialCode == materialCode).Skip(skip).Take(take).ToList();
            return Task.FromResult(result);
        }

        /// <summary>返回跟踪状态的批次 — 按 materialCode 过滤，支持 Consume()。</summary>
        public Task<List<MaterialBatch>> GetTrackedPageAsync(string materialCode, int skip, int take, CancellationToken ct = default)
        {
            var result = _batches.Where(b => b.MaterialCode == materialCode).Skip(skip).Take(take).ToList();
            return Task.FromResult(result);
        }

        public Task<int> CountAsync(string? materialCode, CancellationToken ct = default)
            => Task.FromResult(_batches.Count);

        public Task AddAsync(MaterialBatch batch, CancellationToken ct = default)
        {
            _batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => Task.FromResult(1);

        public Task<double> GetAvailableQuantityAsync(string materialCode, CancellationToken ct = default)
        {
            var sum = _batches.Where(b => b.MaterialCode == materialCode && b.Status == MaterialBatchStatus.Qualified)
                .Sum(b => b.RemainingQuantity);
            return Task.FromResult(sum);
        }

        public Task<Dictionary<string, double>> GetAvailableQuantitiesAsync(IEnumerable<string> materialCodes, CancellationToken ct = default)
        {
            var dict = new Dictionary<string, double>();
            foreach (var code in materialCodes.Distinct())
                dict[code] = _batches.Where(b => b.MaterialCode == code && b.Status == MaterialBatchStatus.Qualified)
                    .Sum(b => b.RemainingQuantity);
            return Task.FromResult(dict);
        }
    }

    private sealed class FakeBomRepository : IBomRepository
    {
        private readonly Bom? _bom;
        public FakeBomRepository(Bom? bom) => _bom = bom;

        public Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken ct = default)
            => Task.FromResult(_bom);
    }

    private sealed class FakeBindingRepository : IMaterialBindingRepository
    {
        private readonly List<MaterialBinding> _bindings = [];

        public void AddBinding(Ulid orderId, string materialCode, Ulid batchId, string batchNumber, double qty)
        {
            _bindings.Add(new MaterialBinding
            {
                Id = Ulid.NewUlid(),
                OrderId = orderId,
                MaterialCode = materialCode,
                MaterialBatchId = batchId,
                BatchNumber = batchNumber,
                Quantity = qty,
                ProductSerial = $"SERIAL-{batchNumber}",
                OperatorId = "OP-TEST",
                BoundAt = DateTimeOffset.UtcNow,
            });
        }

        public Task<MaterialBinding?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_bindings.FirstOrDefault(b => b.Id == id));

        public Task<List<MaterialBinding>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        {
            var result = _bindings.Where(b => b.OrderId == orderId).ToList();
            return Task.FromResult(result);
        }

        public Task<List<MaterialBinding>> GetByProductSerialAsync(string productSerial, CancellationToken ct = default)
        {
            var result = _bindings.Where(b => b.ProductSerial == productSerial).ToList();
            return Task.FromResult(result);
        }

        public Task AddAsync(MaterialBinding binding, CancellationToken ct = default)
        {
            _bindings.Add(binding);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeConsumptionRepository : IMaterialConsumptionRepository
    {
        public List<MaterialConsumption> PresetConsumptions { get; } = [];  // 用于幂等测试
        public List<MaterialConsumption> AddedConsumptions { get; } = [];
        public int SaveChangesCallCount { get; private set; }

        public Task AddAsync(MaterialConsumption consumption, CancellationToken ct = default)
        {
            AddedConsumptions.Add(consumption);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<MaterialConsumption> consumptions, CancellationToken ct = default)
        {
            AddedConsumptions.AddRange(consumptions);
            return Task.CompletedTask;
        }

        public Task<List<MaterialConsumption>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
            => Task.FromResult(PresetConsumptions.ToList());

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeVarianceRepository : IConsumptionVarianceRepository
    {
        public List<ConsumptionVarianceReport> AddedReports { get; } = [];

        public Task AddAsync(ConsumptionVarianceReport report, CancellationToken ct = default)
        {
            AddedReports.Add(report);
            return Task.CompletedTask;
        }

        public Task<List<ConsumptionVarianceReport>> GetUnresolvedAsync(CancellationToken ct = default)
        {
            var result = AddedReports.Where(r => !r.IsResolved).ToList();
            return Task.FromResult(result);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeSapSyncRepository : ISapInventorySyncRecordRepository
    {
        public List<SapInventorySyncRecord> AddedRecords { get; } = [];

        public Task AddAsync(SapInventorySyncRecord record, CancellationToken ct = default)
        {
            AddedRecords.Add(record);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<SapInventorySyncRecord> records, CancellationToken ct = default)
        {
            AddedRecords.AddRange(records);
            return Task.CompletedTask;
        }

        public Task<List<SapInventorySyncRecord>> GetPendingSyncAsync(CancellationToken ct = default)
        {
            var result = AddedRecords.Where(r => !r.SapSynced).ToList();
            return Task.FromResult(result);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
