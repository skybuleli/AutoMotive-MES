using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.Storage;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Sagas;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

public class ProductionOrderSagaTests
{
    [Fact]
    public async Task Execute_ShouldNotReplayCompletedFlowForSameInstance()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var plc = new FakePlcClient();
        var saga = new ProductionOrderSaga(repo, opRepo, plc);
        var order = CreateOrder();
        order.Release();
        await repo.AddAsync(order);

        var store = new InMemoryFunctionStore();
        await store.Initialize();

        var registry = new FunctionsRegistry(store);
        var action = registry.RegisterAction<Ulid>("ProductionOrderSaga", saga.Execute);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var firstSaveCount = repo.SaveChangesCallCount;
        var firstUpdateCount = repo.UpdateCallCount;

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(order.PlannedQuantity, order.QualifiedQuantity);
        Assert.Equal(0, order.DefectiveQuantity);
        Assert.NotNull(order.CompletedAt);
        Assert.Equal(2, firstSaveCount);
        Assert.Equal(2, firstUpdateCount);
        // Effect 之外的安全联锁在重放时会重新执行，所以 ReadyCheckCount 会增加
        // 但核心断言：SaveChanges 和 Update 不应因 Effect 重放而增加（AtLeastOnce 幂等）
        Assert.Equal(firstSaveCount, repo.SaveChangesCallCount);
        Assert.Equal(firstUpdateCount, repo.UpdateCallCount);
    }

    private static ProductionOrder CreateOrder()
        => ProductionOrder.Create(
            Ulid.NewUlid(),
            "WO-20260701-0003",
            "ESP-9.0",
            Ulid.NewUlid(),
            "BOM-A",
            100,
            1,
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));

    private sealed class FakeProductionOrderRepository : IProductionOrderRepository
    {
        public int SaveChangesCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_orders.SingleOrDefault(o => o.Id == id));

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<ProductionOrder?>(null);

        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ProductionOrder>());

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ProductionOrder>());

        public Task<int> CountAsync(OrderStatus? status, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> CountByOrderNumberPrefixAsync(string orderNumberPrefix, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        private readonly List<ProductionOrder> _orders = [];

        public Task AddAsync(ProductionOrder order, CancellationToken cancellationToken = default)
        {
            _orders.Add(order);
            return Task.CompletedTask;
        }

        public void Update(ProductionOrder order)
        {
            UpdateCallCount++;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakePlcClient : IPlcClient
    {
        public int ReadyCheckCount { get; private set; }

        public Task<object> ReadAsync(string address, string tag, CancellationToken cancellationToken = default)
            => Task.FromResult<object>(true);

        public Task WriteAsync(string address, string tag, object value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsReadyAsync(string plcAddress, CancellationToken cancellationToken = default)
        {
            ReadyCheckCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeWorkOrderOperationRepository : IWorkOrderOperationRepository
    {
        public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkOrderOperation?>(null);

        public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkOrderOperation?>(null);

        public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<WorkOrderOperation>());

        public Task AddAsync(WorkOrderOperation operation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Update(WorkOrderOperation operation) { }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);
    }
}
