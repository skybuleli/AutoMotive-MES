using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// CompleteOrderHandler 单元测试（T1.8 完工确认 + T1.17 反冲触发）。
/// 验证状态机推进、成品入库单生成、数量约束、幂等跳过。
/// </summary>
public class CompleteOrderHandlerTests
{
    private static readonly ILogger<CompleteOrderHandler> Logger = NullLogger<CompleteOrderHandler>.Instance;

    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: 正常完工 — InProgress → Completed + 入库单 + SaveChanges
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenInProgress_ShouldCompleteAndCreateReceipt()
    {
        var (order, repos) = CreateInProgressOrder();
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var result = await handler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 95, 5, "REV-001"), default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(95, result.QualifiedQuantity);
        Assert.Equal(5, result.DefectiveQuantity);
        Assert.NotNull(result.CompletedAt);
        Assert.Same(order, result);

        // SaveChanges 被调用 (order + receipt)
        Assert.True(repos.Orders.SaveChangesCallCount >= 1);

        // 入库单已创建
        Assert.NotNull(repos.Receipts.AddedReceipt);
        Assert.Equal(order.Id, repos.Receipts.AddedReceipt.OrderId);
        Assert.Equal(95, repos.Receipts.AddedReceipt.ReceivedQuantity);
        Assert.Equal("REV-001", repos.Receipts.AddedReceipt.ReviewerId);
        Assert.Single(repos.Receipts.AddedReceipts);
        Assert.True(repos.Receipts.SaveChangesCallCount >= 1);

        // 追溯标签码格式
        Assert.StartsWith("ESP9-", repos.Receipts.AddedReceipt.TraceabilityLabelCode);
        Assert.Contains(order.OrderNumber[^4..], repos.Receipts.AddedReceipt.TraceabilityLabelCode);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: 幂等跳过 — 已有入库单
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenReceiptExists_ShouldSkip()
    {
        var (order, repos) = CreateInProgressOrder();
        // 预置已存在的入库单
        var existing = GoodsReceipt.Create(
            order.Id, order.OrderNumber, order.ProductCode,
            50, "REV-OLD", DateTimeOffset.UtcNow.AddDays(-1));
        repos.Receipts.AddExisting(existing);

        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var result = await handler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 95, 5, "REV-001"), default);

        // 状态不变（仍为 InProgress）
        Assert.Equal(OrderStatus.InProgress, result.Status);
        // 没有新的入库单
        Assert.Null(repos.Receipts.AddedReceipt);
        // 未调用 SaveChanges
        Assert.Equal(0, repos.Orders.SaveChangesCallCount);
        Assert.Equal(0, repos.Receipts.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: 工单不存在 — 抛 KeyNotFoundException
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotFound_ShouldThrow()
    {
        var repos = new CompleteRepos(null);
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.ExecuteAsync(
                new CompleteOrderCommand(Ulid.NewUlid(), 100, 0, "REV-001"), default));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: Created 状态不可完工
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCreated_ShouldThrow()
    {
        var order = CreateOrder();
        var repos = new CompleteRepos(order);
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(
                new CompleteOrderCommand(order.Id, 100, 0, "REV-001"), default));
        Assert.Contains("InProgress", ex.Message);
        Assert.Equal(0, repos.Orders.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: Released 状态不可完工
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenReleased_ShouldThrow()
    {
        var order = CreateOrder();
        order.Release();
        var repos = new CompleteRepos(order);
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(
                new CompleteOrderCommand(order.Id, 100, 0, "REV-001"), default));
        Assert.Contains("InProgress", ex.Message);
        Assert.Equal(0, repos.Orders.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 合格+不良数量超过计划 — 抛异常（Complete 领域约束）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenTotalQuantityExceedsPlanned_ShouldThrow()
    {
        var (order, repos) = CreateInProgressOrder();  // Planned = 100
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(
                new CompleteOrderCommand(order.Id, 100, 10, "REV-001"), default));
        Assert.Contains("完工数量", ex.Message);
        Assert.Equal(0, repos.Orders.SaveChangesCallCount);
        Assert.Null(repos.Receipts.AddedReceipt);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: 全数完工 — 合格=计划, 不良=0
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllGood_ShouldCompleteWithFullQuantity()
    {
        var (order, repos) = CreateInProgressOrder();  // Planned = 100
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var result = await handler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 100, 0, "REV-001"), default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(100, result.QualifiedQuantity);
        Assert.Equal(0, result.DefectiveQuantity);
        Assert.NotNull(result.CompletedAt);

        Assert.Equal(100, repos.Receipts.AddedReceipt!.ReceivedQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: 全数不良 — 合格=0, 不良=计划
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllDefective_ShouldCompleteWithZeroQualified()
    {
        var (order, repos) = CreateInProgressOrder();  // Planned = 100
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var result = await handler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 0, 100, "REV-001"), default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(0, result.QualifiedQuantity);
        Assert.Equal(100, result.DefectiveQuantity);

        // 合格数为 0，入库数量也为 0
        Assert.Equal(0, repos.Receipts.AddedReceipt!.ReceivedQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 9: 零数量完工 — 合格=0, 不良=0（不常见但合法）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenZeroQuantities_ShouldComplete()
    {
        var (order, repos) = CreateInProgressOrder();  // Planned = 100
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        var result = await handler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 0, 0, "REV-001"), default);

        Assert.Equal(OrderStatus.Completed, result.Status);
        Assert.Equal(0, result.QualifiedQuantity);
        Assert.Equal(0, result.DefectiveQuantity);
        Assert.Equal(0, repos.Receipts.AddedReceipt!.ReceivedQuantity);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 10: 不合格数量为负 — 抛异常（领域约束）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenNegativeDefectiveQuantity_ShouldThrow()
    {
        var (order, repos) = CreateInProgressOrder();
        var handler = new CompleteOrderHandler(repos.Orders, repos.Receipts, Logger);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.ExecuteAsync(
                new CompleteOrderCommand(order.Id, 100, -1, "REV-001"), default));
        Assert.Equal(0, repos.Orders.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static (ProductionOrder Order, CompleteRepos Repos) CreateInProgressOrder()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();
        return (order, new CompleteRepos(order));
    }

    private static ProductionOrder CreateOrder()
        => ProductionOrder.Create(
            Ulid.NewUlid(), $"WO-CP-{Ulid.NewUlid().ToString()[..8]}", "ESP-9.0",
            Ulid.NewUlid(), "V1.0", 100, 1,
            new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero));

    // ═══════════════════════════════════════════════════════════
    //  Fake 仓储
    // ═══════════════════════════════════════════════════════════

    private sealed class CompleteRepos
    {
        public FakeOrderRepository Orders { get; }
        public FakeGoodsReceiptRepository Receipts { get; }

        public CompleteRepos(ProductionOrder? order)
        {
            Orders = new FakeOrderRepository(order);
            Receipts = new FakeGoodsReceiptRepository();
        }
    }

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
            var list = _order is not null ? new List<ProductionOrder> { _order } : [];
            return Task.FromResult(list);
        }

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default)
        {
            var list = _order is not null ? new List<ProductionOrder> { _order } : [];
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
            => Task.FromResult(_order is not null ? 1 : 0);

        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(ProductionOrder order) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeGoodsReceiptRepository : IGoodsReceiptRepository
    {
        private GoodsReceipt? _existingByOrder;  // 用于幂等测试
        public GoodsReceipt? AddedReceipt { get; private set; }
        public List<GoodsReceipt> AddedReceipts { get; } = [];
        public int SaveChangesCallCount { get; private set; }

        public void AddExisting(GoodsReceipt receipt)
            => _existingByOrder = receipt;

        public Task<GoodsReceipt?> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
            => Task.FromResult(_existingByOrder);

        public Task<GoodsReceipt?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_existingByOrder);

        public Task<List<GoodsReceipt>> GetPageAsync(int skip, int take, CancellationToken ct = default)
        {
            var list = _existingByOrder is not null ? new List<GoodsReceipt> { _existingByOrder } : [];
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(CancellationToken ct = default)
            => Task.FromResult(_existingByOrder is not null ? 1 : 0);

        public Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default)
        {
            AddedReceipt = receipt;
            AddedReceipts.Add(receipt);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}
