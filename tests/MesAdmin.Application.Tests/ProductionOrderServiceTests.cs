using MesAdmin.Application.Common;
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
        var routingRepo = new FakeRoutingRepository(); // 无 routing，回退到硬编码
        var handler = new CreateOrderHandler(repo, opRepo, routingRepo);

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand(" esp-9.0 ", " BOM-A ", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal("ESP-9.0", order.ProductCode);
        Assert.Equal("BOM-A", order.BomVersion);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.StartsWith($"WO-{DateTimeOffset.UtcNow:yyyyMMdd}-", order.OrderNumber);
        Assert.Single(repo.StoredOrders);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task CreateOrder_ShouldInitialize31Operations()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var routingRepo = new FakeRoutingRepository();
        var handler = new CreateOrderHandler(repo, opRepo, routingRepo);

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
        var handler = new ReleaseOrderHandler(repo, new FakeSapOrderSyncRepo());

        var released = await handler.ExecuteAsync(new ReleaseOrderCommand(order.Id), default);

        Assert.Equal(OrderStatus.Released, released.Status);
        // 跟踪查询模式：不再调用 Update()，SaveChanges 自动检测变更
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task CancelOrder_ShouldSetCancelledStatusAndReason()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0009");
        await repo.AddAsync(order);
        var handler = new CancelOrderHandler(repo, new FakeSapOrderSyncRepo());

        var cancelled = await handler.ExecuteAsync(new CancelOrderCommand(order.Id, "客户撤单"), default);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal("客户撤单", cancelled.CancelReason);
        Assert.Equal(1, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task CancelOrder_ShouldRejectWhenInProgress()
    {
        var repo = new FakeProductionOrderRepository();
        var order = CreateOrder("WO-20260701-0010");
        order.Release();
        order.Start();
        await repo.AddAsync(order);
        var handler = new CancelOrderHandler(repo, new FakeSapOrderSyncRepo());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new CancelOrderCommand(order.Id, "too late"), default));
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

    // ═══════════════════════════════════════════
    //  P0 集成测试：Routing 表初始化工序
    // ═══════════════════════════════════════════

    [Fact]
    public async Task CreateOrder_WhenRoutingFound_ShouldUseRoutingTableOps()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var routingRepo = new FakeRoutingRepository(true); // 返回 Routing 表数据
        var handler = new CreateOrderHandler(repo, opRepo, routingRepo);

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-A", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal(31, opRepo.StoredOperations.Count);
        // 验证工序名称来自 Routing 表而非硬编码
        var seq1 = opRepo.StoredOperations.First(o => o.Sequence == 1);
        Assert.Equal("上料扫码", seq1.OperationName);
        Assert.Equal("LOAD-01", seq1.OperationCode);

        var seq31 = opRepo.StoredOperations.First(o => o.Sequence == 31);
        Assert.Equal("VIN 绑定 + 标签打印", seq31.OperationName);
        Assert.Equal("VN-01", seq31.OperationCode);
    }

    [Fact]
    public async Task CreateOrder_WhenRoutingNotFound_ShouldUseHardcodedFallback()
    {
        var repo = new FakeProductionOrderRepository();
        var opRepo = new FakeWorkOrderOperationRepository();
        var routingRepo = new FakeRoutingRepository(false); // 返回 null，触发回退
        var handler = new CreateOrderHandler(repo, opRepo, routingRepo);

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-A", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal(31, opRepo.StoredOperations.Count);
        // 回退到硬编码时，工序名与生产路线定义一致
        var seq1 = opRepo.StoredOperations.First(o => o.Sequence == 1);
        Assert.Equal("上料扫码", seq1.OperationName);
    }

    // ═══════════════════════════════════════════
    //  并发唯一冲突重试（#2 健壮性）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task CreateOrder_WhenOrderNumberConflicts_ShouldRetryWithIncrementedSequence()
    {
        // 首次提交抛唯一冲突，第二次成功 → 序号应从 0001 递增到 0002
        var repo = new ConflictInjectingOrderRepository(conflictCount: 1);
        var handler = new CreateOrderHandler(repo, new FakeWorkOrderOperationRepository(), new FakeRoutingRepository());

        var order = await handler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-A", Ulid.NewUlid(), 50, (short)1), default);

        Assert.Equal(2, repo.SaveChangesCallCount);
        Assert.EndsWith("-0002", order.OrderNumber);
    }

    [Fact]
    public async Task CreateOrder_WhenConflictPersists_ShouldThrowAfterMaxAttempts()
    {
        // 持续冲突 → 5 次尝试后仍抛 DuplicateOrderNumberException
        var repo = new ConflictInjectingOrderRepository(conflictCount: 99);
        var handler = new CreateOrderHandler(repo, new FakeWorkOrderOperationRepository(), new FakeRoutingRepository());

        await Assert.ThrowsAsync<DuplicateOrderNumberException>(
            () => handler.ExecuteAsync(
                new CreateOrderCommand("ESP-9.0", "BOM-A", Ulid.NewUlid(), 50, (short)1), default));

        Assert.Equal(5, repo.SaveChangesCallCount);
    }

    /// <summary>SaveChanges 前 N 次抛 DuplicateOrderNumberException，之后成功——模拟并发唯一冲突。</summary>
    private sealed class ConflictInjectingOrderRepository : IProductionOrderRepository
    {
        private readonly int _conflictCount;
        public int SaveChangesCallCount { get; private set; }

        public ConflictInjectingOrderRepository(int conflictCount) => _conflictCount = conflictCount;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            if (SaveChangesCallCount <= _conflictCount)
                throw new DuplicateOrderNumberException("WO-CONFLICT");
            return Task.FromResult(1);
        }

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(0);
        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<ProductionOrder>());
        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default) => Task.FromResult(new List<ProductionOrder>());
        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default) => Task.FromResult(0);
        public void Update(ProductionOrder order) { }
    }

    /// <summary>
    /// Fake Routing 仓储用于测试。
    /// hasRouting=true 时返回兼容的 Routing 数据，false 时返回 null（模拟 DB 无数据）。
    /// </summary>
    private sealed class FakeSapOrderSyncRepo : ISapOrderSyncRecordRepository
    {
        public Task AddAsync(SapOrderSyncRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddRangeAsync(IEnumerable<SapOrderSyncRecord> records, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<SapOrderSyncRecord>> GetPendingSyncAsync(CancellationToken ct = default) => Task.FromResult(new List<SapOrderSyncRecord>());
        public Task<List<SapOrderSyncRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default) => Task.FromResult(new List<SapOrderSyncRecord>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeRoutingRepository : IRoutingRepository
    {
        private readonly Routing? _routing;

        public FakeRoutingRepository(bool hasRouting = false)
        {
            if (!hasRouting) return;

            var ops = ProductionRoutings.Default.Select(d => new RoutingOperation
            {
                Sequence = d.Seq,
                Station = d.Station,
                OperationCode = d.Code,
                OperationName = d.Name,
                ParameterTemplates = [],
            }).ToList();

            _routing = Routing.Create(
                Ulid.NewUlid(), "ESP-9.0", "Test Routing", "1.0", "system", ops);
        }

        public Task<Routing?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_routing);

        public Task<Routing?> GetActiveByProductAsync(string productCode, CancellationToken ct = default)
            => Task.FromResult(_routing);

        public Task<List<Routing>> GetByProductAsync(string productCode, CancellationToken ct = default)
            => Task.FromResult(_routing is not null ? new List<Routing> { _routing } : new List<Routing>());

        public Task<List<Routing>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(_routing is not null ? new List<Routing> { _routing } : new List<Routing>());

        public Task<List<Routing>> GetByEcoStatusAsync(EcoStatus status, CancellationToken ct = default)
            => Task.FromResult(_routing is not null ? new List<Routing> { _routing } : new List<Routing>());

        public Task AddAsync(Routing routing, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Routing routing, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
