using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

public class ProductionOrderCommandHandlerTests
{
    [Fact]
    public async Task CreateOrder_ShouldPersistNewOrder()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var handler = new CreateOrderHandler(repo, opRepo);

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand(" esp-9.0 ", " BOM-A ", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal("ESP-9.0", order.ProductCode);
        Assert.Equal("BOM-A", order.BomVersion);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.StartsWith($"WO-{DateTimeOffset.Now:yyyyMMdd}-", order.OrderNumber);
        Assert.Single(repo.StoredOrders);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task CreateOrder_ShouldInitialize31Operations()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var handler = new CreateOrderHandler(repo, opRepo);

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-A", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal(31, opRepo.StoredOperations.Count);
        Assert.Equal(1, opRepo.StoredOperations.Count(o => o.Station == 1));
        Assert.Equal(4, opRepo.StoredOperations.Count(o => o.Station == 2));
        Assert.Equal(5, opRepo.StoredOperations.Count(o => o.Station == 3));
    }

    [Fact]
    public async Task ReleaseOrder_ShouldUpdateStatusAndSave()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0001");
        await repo.AddAsync(order);
        var handler = new ReleaseOrderHandler(repo);

        var released = await handler.ExecuteAsync(new ReleaseOrderCommand(order.Id), default);

        Assert.Equal(OrderStatus.Released, released.Status);
        // 跟踪查询模式：不再调用 Update()，SaveChanges 自动检测变更
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task StartOrder_ShouldRejectNonStartableOrder()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0003");
        order.Release();
        order.Start();
        await repo.AddAsync(order);  // 已 InProgress，CanStart=false
        var handler = new StartOrderHandler(repo);

        // CanStart=false 时在发布事件前就抛异常，不需要 IServiceResolver
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new StartOrderCommand(order.Id), default));
    }

    [Fact]
    public async Task ReportOperation_ShouldUpdateOperationStatus()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var handler = new ReportOperationHandler(opRepo);

        var order = CreateOrder("WO-20260701-0004");
        await repo.AddAsync(order);
        var op = WorkOrderOperation.Create(order.Id, 1, 1, "LOAD-01", "上料扫码");
        await opRepo.AddAsync(op);
        await opRepo.SaveChangesAsync();

        var result = await handler.ExecuteAsync(
            new ReportOperationCommand(order.Id, 1, "OP-001", "EQ-01", null), default);

        Assert.Equal(OperationStatus.Completed, result.Status);
        Assert.Equal("OP-001", result.OperatorId);
        Assert.Equal("EQ-01", result.EquipmentId);
        Assert.NotNull(result.StartAt);
        Assert.NotNull(result.EndAt);
    }

    private static ProductionOrder CreateOrder(string orderNumber)
        => ProductionOrder.Create(
            Ulid.NewUlid(),
            orderNumber,
            "ESP-9.0",
            Ulid.NewUlid(),
            "BOM-A",
            100,
            1,
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    private sealed class FakeProductionOrderRepository : IProductionOrderRepository
    {
        public List<ProductionOrder> StoredOrders { get; } = [];
        public int SaveChangesCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default)
            => Task.FromResult(StoredOrders.SingleOrDefault(order => order.Id == id));

        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken cancellationToken = default)
            => Task.FromResult(StoredOrders.SingleOrDefault(order => order.Id == id));

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(StoredOrders.SingleOrDefault(order => order.OrderNumber == orderNumber));

        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(StoredOrders.ToList());

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken cancellationToken = default)
        {
            var query = StoredOrders.AsEnumerable();
            if (status is not null)
                query = query.Where(order => order.Status == status);
            return Task.FromResult(query.Skip(skip).Take(take).ToList());
        }

        public Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default)
        {
            var count = status is null ? StoredOrders.Count : StoredOrders.Count(order => order.Status == status);
            return Task.FromResult(count);
        }

        public Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken cancellationToken = default)
            => Task.FromResult(StoredOrders.Count(order => order.OrderNumber.StartsWith(orderNumberPrefix, StringComparison.Ordinal)));

        public Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default)
        {
            StoredOrders.Add(order);
            return Task.CompletedTask;
        }

        public void Update(ProductionOrder order) { UpdateCallCount++; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeWorkOrderOperationRepository : IWorkOrderOperationRepository
    {
        public List<WorkOrderOperation> StoredOperations { get; } = [];

        public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(StoredOperations.SingleOrDefault(o => o.Id == id));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => Task.FromResult(StoredOperations.SingleOrDefault(o => o.OrderId == orderId && o.Sequence == sequence));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => Task.FromResult(StoredOperations.SingleOrDefault(o => o.OrderId == orderId && o.Sequence == sequence));

        public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
            => Task.FromResult(StoredOperations.Where(o => o.OrderId == orderId).OrderBy(o => o.Sequence).ToList());

        public Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken ct = default)
            => Task.FromResult(StoredOperations.Where(o => o.OrderId == orderId).OrderBy(o => o.Sequence).ToList());

        public Task AddAsync(WorkOrderOperation operation, CancellationToken ct = default)
        {
            StoredOperations.Add(operation);
            return Task.CompletedTask;
        }

        public void Update(WorkOrderOperation operation) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
