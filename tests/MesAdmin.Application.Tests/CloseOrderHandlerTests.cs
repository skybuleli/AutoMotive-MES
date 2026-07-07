using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

/// <summary>
/// CloseOrderHandler 单元测试。
/// 验证关闭约束（仅 Completed 状态可关闭）和幂等关闭逻辑。
/// </summary>
public class CloseOrderHandlerTests
{
    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: 正常关闭 — Completed → Closed
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCompleted_ShouldClose()
    {
        var (order, repo) = CreateCompletedOrder();
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        var result = await handler.ExecuteAsync(new CloseOrderCommand(order.Id), default);

        Assert.Equal(OrderStatus.Closed, result.Status);
        Assert.Same(order, result);  // 返回同一个引用（跟踪实体）
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: 工单不存在 — 抛 KeyNotFoundException
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotFound_ShouldThrow()
    {
        var repo = new FakeCloseOrderRepository(null);
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.ExecuteAsync(new CloseOrderCommand(Ulid.NewUlid()), default));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: Created 状态不可关闭
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCreated_ShouldThrow()
    {
        var order = CreateOrder();
        // Created — 不可关闭
        var repo = new FakeCloseOrderRepository(order);
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        Assert.False(order.CanClose);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new CloseOrderCommand(order.Id), default));
        Assert.Contains("Completed", ex.Message);
        Assert.Equal(0, repo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: InProgress 状态不可关闭
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenInProgress_ShouldThrow()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();
        var repo = new FakeCloseOrderRepository(order);
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        Assert.False(order.CanClose);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new CloseOrderCommand(order.Id), default));
        Assert.Contains("Completed", ex.Message);
        Assert.Equal(0, repo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: 已关闭状态不可再次关闭
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAlreadyClosed_ShouldThrow()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();
        order.Complete(100, 0, DateTimeOffset.UtcNow);
        order.Close();  // → Closed
        var repo = new FakeCloseOrderRepository(order);
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        Assert.False(order.CanClose);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new CloseOrderCommand(order.Id), default));
        Assert.Contains("Completed", ex.Message);
        Assert.Equal(0, repo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 空合格数量完工后关闭
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCompletedWithZeroQualified_ShouldClose()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();
        order.Complete(0, 100, DateTimeOffset.UtcNow);  // 全数不良，但状态仍为 Completed
        var repo = new FakeCloseOrderRepository(order);
        var handler = new CloseOrderHandler(repo, new FakeSapOrderSyncRepo());

        var result = await handler.ExecuteAsync(new CloseOrderCommand(order.Id), default);

        Assert.Equal(OrderStatus.Closed, result.Status);
        Assert.Equal(0, result.QualifiedQuantity);
        Assert.Equal(100, result.DefectiveQuantity);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static (ProductionOrder Order, FakeCloseOrderRepository Repo) CreateCompletedOrder()
    {
        var order = CreateOrder();
        order.Release();
        order.Start();
        order.Complete(100, 0, DateTimeOffset.UtcNow);
        return (order, new FakeCloseOrderRepository(order));
    }

    private static ProductionOrder CreateOrder()
        => ProductionOrder.Create(
            Ulid.NewUlid(), $"WO-CL-{Ulid.NewUlid().ToString()[..8]}", "ESP-9.0",
            Ulid.NewUlid(), "V1.0", 100, 1,
            new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero));

    // ═══════════════════════════════════════════════════════════
    //  Fake 仓储
    // ═══════════════════════════════════════════════════════════

    private sealed class FakeSapOrderSyncRepo : ISapOrderSyncRecordRepository
    {
        public Task AddAsync(SapOrderSyncRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddRangeAsync(IEnumerable<SapOrderSyncRecord> records, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SapOrderSyncRecord>> GetPendingSyncAsync(CancellationToken ct = default) => Task.FromResult(new List<SapOrderSyncRecord>());
        public Task<List<SapOrderSyncRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default) => Task.FromResult(new List<SapOrderSyncRecord>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeCloseOrderRepository : IProductionOrderRepository
    {
        private readonly ProductionOrder? _order;
        public int SaveChangesCallCount { get; private set; }

        public FakeCloseOrderRepository(ProductionOrder? order) => _order = order;

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
}
