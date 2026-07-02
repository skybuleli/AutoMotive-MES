using FastEndpoints;
using MesAdmin.Application.Events;
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
    public async Task StartOrder_ShouldPublishEventWithoutMutatingState()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0002");
        order.Release();
        await repo.AddAsync(order);
        var eventBus = new CaptureEventBus();
        var handler = new StartOrderHandler(repo, eventBus);

        var result = await handler.ExecuteAsync(new StartOrderCommand(order.Id), default);

        // handler 只发布事件，不写状态 —— 状态推进由 Saga 负责
        Assert.Equal(OrderStatus.Released, result.Status);
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Equal(0, repo.SaveChangesCallCount);
        // 验证事件被发布（Saga 订阅者由此触发状态推进）
        Assert.Single(eventBus.PublishedEvents);
        var evt = Assert.IsType<OrderStartedEvent>(eventBus.PublishedEvents[0]);
        Assert.Equal(order.Id, evt.OrderId);
        Assert.Equal(order.OrderNumber, evt.OrderNumber);
    }

    [Fact]
    public async Task StartOrder_ShouldRejectNonStartableOrder()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0003");
        order.Release();
        order.Start();
        await repo.AddAsync(order);  // 已 InProgress，CanStart=false
        var eventBus = new CaptureEventBus();
        var handler = new StartOrderHandler(repo, eventBus);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new StartOrderCommand(order.Id), default));
        Assert.Empty(eventBus.PublishedEvents);  // 验证失败不发布事件
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

    private sealed class CaptureEventBus : IEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventModel, Mode waitMode, CancellationToken cancellation = default)
        {
            PublishedEvents.Add(eventModel!);
            return Task.CompletedTask;
        }
    }

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
