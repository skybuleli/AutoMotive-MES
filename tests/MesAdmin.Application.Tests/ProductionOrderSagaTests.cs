using Cleipnir.ResilientFunctions;
using Cleipnir.ResilientFunctions.CoreRuntime.Invocation;
using Cleipnir.ResilientFunctions.Domain;
using Cleipnir.ResilientFunctions.Storage;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Sagas;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

/// <summary>
/// ProductionOrderSaga 单元测试：用 InMemoryFunctionStore 验证 31 工序编排完整逻辑。
/// 覆盖 Effect 幂等性、崩溃恢复、AtLeastOnce/AtMostOnce 策略、状态快照推进、重放安全。
///
/// ⚠ Saga 不处理站 1（seq 1，人工上料扫码），因此所有"工序全部完工"断言仅覆盖 seq 2-31。
/// SagaState 通过 Cleipnir Workflow.States API 持久化，不暴露于 InMemoryFunctionStore.GetFunction，
/// 因此状态验证改为通过工序记录、工单状态、仓储 SaveChanges 调用次数等可观测行为进行。
/// </summary>
public class ProductionOrderSagaTests
{
    private const int SagaProcessedOpCount = 30; // seq 2-31（站 1 由人工处理）

    // ═══════════════════════════════════════════════════════════
    //  Scenario 1: Saga 执行全部 30 道工序
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldCompleteAllSagaOperations()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);

        // 站 1（seq 1）由人工上料，Saga 不处理
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[1].Status);

        // 站 2-7（seq 2-31）由 Saga 完成
        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.Equal(SagaProcessedOpCount, sagaOps.Count);
        Assert.All(sagaOps, op => Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 2: Saga 幂等 — 重放不重复执行 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenReplayed_ShouldNotDuplicateOperations()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var firstSaveCount = repo.SaveChangesCallCount;

        // 仅计数 Saga 处理的工序（seq 2-31）
        var opsAfterFirst = opRepo.StoredOps.Values
            .Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, opsAfterFirst.Count);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 重放时 Effect 幂等，SaveChanges 不应增加
        Assert.Equal(firstSaveCount, repo.SaveChangesCallCount);

        var opsAfterReplay = opRepo.StoredOps.Values
            .Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, opsAfterReplay.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 3: 从 Released 状态启动 — Saga 跳过 Release
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAlreadyReleased_ShouldNotCallReleaseAgain()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        order.Release();
        var (action, _) = await RegisterSaga(saga);

        var saveBeforeSaga = repo.SaveChangesCallCount;

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.True(repo.SaveChangesCallCount > saveBeforeSaga);  // Start 至少 1 次

        var ops = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, ops.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 4: 崩溃恢复 — 站2完成后崩溃，重放站2幂等
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStation2AlreadyDone_ShouldSkipStation2()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        foreach (var seq in Enumerable.Range(2, 4))
        {
            var op = opRepo.StoredOps[seq];
            op.Start("AUTO", "EQ-ASM-01", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.All(Enumerable.Range(2, 4), seq =>
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        });

        Assert.All(Enumerable.Range(6, 26), seq =>
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 5: 崩溃恢复 — 站4 液压已部分完工
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStation4PartiallyDone_ShouldResume()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        foreach (var seq in Enumerable.Range(11, 3))
        {
            var op = opRepo.StoredOps[seq];
            op.Start("AUTO", "EQ-HYD-01", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 站 1 由人工处理，Saga 不设定
        Assert.Equal(OperationStatus.Pending, opRepo.StoredOps[1].Status);

        Assert.All(Enumerable.Range(2, 9), seq =>       // seq 2-10
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        Assert.All(Enumerable.Range(11, 13), seq =>     // seq 11-23
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));

        Assert.All(Enumerable.Range(24, 8), seq =>      // seq 24-31
            Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[seq].Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 6: 站6 AtMostOnce — 重放不重复执行
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Station6AtMostOnce_ShouldNotReexecute()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var firstSaveCount = opRepo.SaveChangesCallCount;

        var station6EndTimesBefore = Enumerable.Range(28, 3)
            .Select(seq => opRepo.StoredOps[seq].EndAt)
            .ToList();

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        var station6EndTimesAfter = Enumerable.Range(28, 3)
            .Select(seq => opRepo.StoredOps[seq].EndAt)
            .ToList();

        Assert.Equal(station6EndTimesBefore, station6EndTimesAfter);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 7: 站3 螺栓拧紧 — 每个螺栓独立 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Station3BoltEffects_ShouldCompleteIndependently()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        var boltOp = opRepo.StoredOps[6];
        boltOp.Start("AUTO", "EQ-TQ-01", DateTimeOffset.UtcNow.AddMinutes(-10));
        boltOp.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OperationStatus.Completed, boltOp.Status);
        Assert.NotNull(boltOp.EndAt);

        foreach (var seq in new[] { 7, 8, 9 })
        {
            var op = opRepo.StoredOps[seq];
            Assert.Equal(OperationStatus.Completed, op.Status);
        }

        Assert.Equal(OperationStatus.Completed, opRepo.StoredOps[10].Status);
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 8: 全部工序已完工 — Saga 跳过所有 Effect
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllOpsAlreadyCompleted_ShouldSkipAllEffects()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        // Pre-complete all 31 operations
        foreach (var op in opRepo.StoredOps.Values)
        {
            op.Start("AUTO", "EQ-TEST", DateTimeOffset.UtcNow.AddMinutes(-10));
            op.Complete(DateTimeOffset.UtcNow.AddMinutes(-9));
        }

        repo.ResetSaveCount();
        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // Saga outside-Effect code: Release (1) + Start (1) = 2 saves
        // All effects skip because IsStationCompleted returns true — no opRepo.SaveChanges
        Assert.Equal(2, repo.SaveChangesCallCount);
        Assert.All(opRepo.StoredOps.Values, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 9: 工单 Created 状态 → Saga 自动 Release + Start
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenCreated_ShouldReleaseAndStart()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        Assert.Equal(OrderStatus.Created, order.Status);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(OrderStatus.InProgress, order.Status);
        Assert.True(repo.SaveChangesCallCount >= 2);  // Release + Start

        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.All(sagaOps, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 10: 各工站工序数量正确
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldCompleteCorrectStationOperationCounts()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // Saga 处理的工序（seq 2-31）
        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2 && o.Status == OperationStatus.Completed).ToList();
        Assert.Equal(SagaProcessedOpCount, sagaOps.Count);

        Assert.Equal(4, sagaOps.Count(o => o.Station == 2));
        Assert.Equal(5, sagaOps.Count(o => o.Station == 3));
        Assert.Equal(13, sagaOps.Count(o => o.Station == 4));
        Assert.Equal(4, sagaOps.Count(o => o.Station == 5));
        Assert.Equal(3, sagaOps.Count(o => o.Station == 6));
        Assert.Equal(1, sagaOps.Count(o => o.Station == 7));
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 11: 工序完工时间戳按工站顺序递增
    //  ⚠ SagaState 通过 Workflow.States API 持久化，不暴露于 InMemoryFunctionStore，
    //    因此用工站首道工序 EndAt 时间戳代替状态验证工站执行顺序。
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_OperationTimestamps_ShouldBeInStationOrder()
    {
        var (order, repo, opRepo, saga, _) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        // 站 2-7 各工站首道工序的 EndAt 应严格递增
        // 站 1 由人工处理，Saga 不设 EndAt，排除在外
        var stationGroupEndAt = opRepo.StoredOps.Values
            .Where(o => o.Station >= 2 && o.Status == OperationStatus.Completed)
            .GroupBy(o => o.Station)
            .OrderBy(g => g.Key)
            .Select(g => g.First().EndAt!)
            .ToList();

        for (int i = 1; i < stationGroupEndAt.Count; i++)
            Assert.True(stationGroupEndAt[i - 1] <= stationGroupEndAt[i],
                $"工站 {i + 1} 的开始时间应不早于工站 {i} 的结束时间");
    }

    // ═══════════════════════════════════════════════════════════
    //  Scenario 12: 崩溃恢复 — 相同 store 重放全部跳过
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenAllCompleted_ReplayShouldSkipAllEffects()
    {
        var (order, repo, opRepo, saga, store) = CreateSagaWith31Ops();
        var (action, _) = await RegisterSaga(saga, store);

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);
        var saveCountAfterFirst = opRepo.SaveChangesCallCount;

        await action.Invoke.Invoke(order.Id.ToString(), order.Id);

        Assert.Equal(saveCountAfterFirst, opRepo.SaveChangesCallCount);

        var sagaOps = opRepo.StoredOps.Values.Where(o => o.Sequence >= 2).ToList();
        Assert.All(sagaOps, op =>
            Assert.Equal(OperationStatus.Completed, op.Status));
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static (ProductionOrder Order, SagaOrderRepo Repo, SagaOpRepo OpRepo, ProductionOrderSaga Saga, InMemoryFunctionStore Store)
        CreateSagaWith31Ops()
    {
        var order = ProductionOrder.Create(
            Ulid.NewUlid(), $"WO-SAGA-{Ulid.NewUlid().ToString()[..6]}", "ESP-9.0",
            Ulid.NewUlid(), "V1.0", 100, 1,
            new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.Zero));

        var repo = new SagaOrderRepo(order);
        var opRepo = new SagaOpRepo();
        var store = new InMemoryFunctionStore();

        foreach (var (seq, station, code, name) in ProductionRoutings.Default)
            opRepo.StoredOps[seq] = WorkOrderOperation.Create(order.Id, seq, station, code, name);

        var saga = new ProductionOrderSaga(repo, opRepo);
        return (order, repo, opRepo, saga, store);
    }

    private static async Task<(ActionRegistration<Ulid> Action, InMemoryFunctionStore Store)> RegisterSaga(
        ProductionOrderSaga saga, InMemoryFunctionStore? existingStore = null)
    {
        var store = existingStore ?? new InMemoryFunctionStore();
        await store.Initialize();
        var registry = new FunctionsRegistry(store);
        var action = registry.RegisterAction<Ulid>("ProductionOrderSaga", saga.Execute);
        return (action, store);
    }

    // ═══════════════════════════════════════════════════════════
    //  Fake 仓储
    // ═══════════════════════════════════════════════════════════

    public sealed class SagaOrderRepo : IProductionOrderRepository
    {
        private ProductionOrder? _order;
        public int SaveChangesCallCount { get; private set; }

        public SagaOrderRepo(ProductionOrder? order) => _order = order;
        public void ResetSaveCount() => SaveChangesCallCount = 0;

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(_order);

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
            => Task.FromResult<ProductionOrder?>(null);

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
            => Task.FromResult(_order is not null ? 1 : 0);

        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default)
        {
            _order = order;
            return Task.CompletedTask;
        }

        public void Update(ProductionOrder order) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    public sealed class SagaOpRepo : IWorkOrderOperationRepository
    {
        public Dictionary<int, WorkOrderOperation> StoredOps { get; } = new();
        public int SaveChangesCallCount { get; private set; }

        public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(StoredOps.Values.FirstOrDefault(o => o.Id == id));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken ct = default)
            => Task.FromResult(StoredOps.GetValueOrDefault(sequence));

        public Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken ct = default)
        {
            var op = StoredOps.GetValueOrDefault(sequence);
            return Task.FromResult(op);
        }

        public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        {
            var list = StoredOps.Values.OrderBy(o => o.Sequence).ToList();
            return Task.FromResult(list);
        }

        public Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken ct = default)
        {
            var list = StoredOps.Values.OrderBy(o => o.Sequence).ToList();
            return Task.FromResult(list);
        }

        public Task AddAsync(WorkOrderOperation operation, CancellationToken ct = default)
        {
            StoredOps[operation.Sequence] = operation;
            return Task.CompletedTask;
        }

        public void Update(WorkOrderOperation operation) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    public sealed class FakePlcClient : IPlcClient
    {
        public Task<object> ReadAsync(string address, string tag, CancellationToken ct = default)
            => Task.FromResult<object>(true);

        public Task WriteAsync(string address, string tag, object value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> IsReadyAsync(string plcAddress, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
