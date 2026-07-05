using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// T1.4 KitCheckHandler 单元测试。
/// 用 Fake 仓储验证 BOM 展开 → 库存校验 → 缺料 JIT 信号生成 的完整逻辑。
/// </summary>
public class KitCheckHandlerTests
{
    private static readonly ILogger<KitCheckHandler> Logger = NullLogger<KitCheckHandler>.Instance;

    // ══════════════════════════════════════════════════════
    //  Scenario 1: 齐套通过 — 所有关键物料库存充足
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStockSufficient_ShouldPassAndReleaseOrder()
    {
        var order = CreateCreatedOrder("WO-KC-0001", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        batchRepo.SetAvailable("ECU-ESP9-001", 200);
        batchRepo.SetAvailable("HCU-ESP9-001", 200);
        batchRepo.SetAvailable("MOT-ESP9-001", 200);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(result.IsPassed);
        Assert.Empty(result.Items);
        Assert.Empty(result.JitPullSignalIds);
        Assert.Equal(OrderStatus.Released, order.Status);
        Assert.Equal(1, orderRepo.SaveChangesCallCount);
        Assert.Empty(jitRepo.AddedSignals);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 2: 齐套失败 — 某关键物料短缺
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStockInsufficient_ShouldFailAndCreateJitSignal()
    {
        var order = CreateCreatedOrder("WO-KC-0002", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        // ECU 短缺：需求 100，可用 50 → 短缺 50
        batchRepo.SetAvailable("ECU-ESP9-001", 50);
        batchRepo.SetAvailable("HCU-ESP9-001", 200);
        batchRepo.SetAvailable("MOT-ESP9-001", 200);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.False(result.IsPassed);
        // KitCheckHandler 将所有关键物料加入 Items，不论是否短缺
        Assert.Equal(3, result.Items.Count);
        // 验证至少有一个短缺项
        Assert.Contains(result.Items, i => i.MaterialCode == "ECU-ESP9-001" && i.ShortageQuantity == 50);
        Assert.Contains(result.Items, i => i.MaterialCode == "HCU-ESP9-001" && i.ShortageQuantity == 0);
        Assert.Contains(result.Items, i => i.MaterialCode == "MOT-ESP9-001" && i.ShortageQuantity == 0);
        Assert.NotEmpty(result.JitPullSignalIds);

        // 验证订单状态不变
        Assert.Equal(OrderStatus.Created, order.Status);

        // 验证 JIT 信号（只有 ECU 缺料 → 1 个信号）
        Assert.Single(jitRepo.AddedSignals);
        var signal = jitRepo.AddedSignals[0];
        Assert.Equal("ECU-ESP9-001", signal.MaterialCode);
        Assert.Equal(50, signal.ShortageQuantity);
        Assert.Equal("产线-ESP-9.0", signal.TargetStation);
        Assert.Equal(JitPullStatus.Created, signal.Status);
        Assert.Equal(1, jitRepo.SaveChangesCallCount);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 3: BOM 不存在 — 跳过物料检查直接放行
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenBomNotFound_ShouldSkipCheckAndRelease()
    {
        var order = CreateCreatedOrder("WO-KC-0003", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(null);  // BOM 不存在
        var jitRepo = new FakeJitPullSignalRepository();

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(result.IsPassed);
        Assert.Equal(OrderStatus.Released, order.Status);
        Assert.Empty(jitRepo.AddedSignals);
        Assert.Equal(1, orderRepo.SaveChangesCallCount);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 4: 工单不存在 — 抛 KeyNotFoundException
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotFound_ShouldThrow()
    {
        var orderRepo = new FakeOrderRepository(null);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.ExecuteAsync(new KitCheckCommand(Ulid.NewUlid()), default));
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 5: 工单状态错误 — 抛 InvalidOperationException
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOrderNotCreated_ShouldThrow()
    {
        var order = CreateCreatedOrder("WO-KC-0005", 100);
        order.Release();  // 已放行，不再 Created
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.ExecuteAsync(new KitCheckCommand(order.Id), default));
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 6: 多种物料短缺 — 多个 JIT 信号
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenMultipleItemsShort_ShouldCreateMultipleJitSignals()
    {
        var order = CreateCreatedOrder("WO-KC-0006", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        // 三种关键物料全部短缺
        batchRepo.SetAvailable("ECU-ESP9-001", 0);
        batchRepo.SetAvailable("HCU-ESP9-001", 0);
        batchRepo.SetAvailable("MOT-ESP9-001", 0);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.False(result.IsPassed);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.JitPullSignalIds.Count);
        Assert.All(result.Items, item => Assert.True(item.ShortageQuantity > 0));

        Assert.Equal(3, jitRepo.AddedSignals.Count);
        Assert.Equal(1, jitRepo.SaveChangesCallCount);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 7: 混合关键/非关键物料 — 非关键被跳过
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldSkipNonCriticalItems()
    {
        var order = CreateCreatedOrder("WO-KC-0007", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateBomWithMixedCriticality());
        var jitRepo = new FakeJitPullSignalRepository();

        // 仅关键物料设库存；非关键物料不设（应被跳过）
        batchRepo.SetAvailable("ECU-ESP9-001", 200);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(result.IsPassed);
        Assert.Equal(OrderStatus.Released, order.Status);
        Assert.Empty(jitRepo.AddedSignals);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 8: 已有前序 JIT 信号 → 旧信号被取消，新信号创建
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenExistingPendingSignal_ShouldCancelOldAndCreateNew()
    {
        var order = CreateCreatedOrder("WO-KC-0008", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSampleBom());
        var jitRepo = new FakeJitPullSignalRepository();

        // 预先存在一个待处理的 JIT 信号
        var oldSignal = JitPullSignal.Create(
            order.Id, order.OrderNumber, "ECU-ESP9-001",
            "ECU 电子控制单元", 30, "PCS");
        jitRepo.AddTrackedPending(oldSignal);

        // ECU 仍然短缺（可用 50，需求 100 → 短缺 50）
        batchRepo.SetAvailable("ECU-ESP9-001", 50);
        batchRepo.SetAvailable("HCU-ESP9-001", 200);
        batchRepo.SetAvailable("MOT-ESP9-001", 200);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.False(result.IsPassed);

        // 旧信号被取消
        Assert.Equal(JitPullStatus.Cancelled, oldSignal.Status);
        Assert.Equal("被齐套检查重新触发替代", oldSignal.Remarks);

        // 新信号创建
        Assert.Single(jitRepo.AddedSignals);
        var newSignal = jitRepo.AddedSignals[0];
        Assert.Equal("ECU-ESP9-001", newSignal.MaterialCode);
        Assert.Equal(JitPullStatus.Created, newSignal.Status);
        Assert.Equal(50, newSignal.ShortageQuantity);
        Assert.Equal(1, jitRepo.SaveChangesCallCount);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 9: 完全无库存 — 报告全额短缺
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenNoStockAtAll_ShouldReportFullShortage()
    {
        var order = CreateCreatedOrder("WO-KC-0009", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSingleCriticalBom());
        var jitRepo = new FakeJitPullSignalRepository();

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.False(result.IsPassed);
        Assert.Single(result.Items);
        Assert.Equal(100, result.Items[0].RequiredQuantity);
        Assert.Equal(0, result.Items[0].AvailableQuantity);
        Assert.Equal(100, result.Items[0].ShortageQuantity);

        Assert.Single(jitRepo.AddedSignals);
        Assert.Equal(100, jitRepo.AddedSignals[0].ShortageQuantity);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 10: 库存恰好等于需求 — 边界通过
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenStockExactlyMatches_ShouldPass()
    {
        var order = CreateCreatedOrder("WO-KC-0010", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateSingleCriticalBom());
        var jitRepo = new FakeJitPullSignalRepository();

        batchRepo.SetAvailable("ECU-ESP9-001", 100);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(result.IsPassed);
        Assert.Empty(jitRepo.AddedSignals);
        Assert.Equal(OrderStatus.Released, order.Status);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 11: BOM 仅有非关键物料 — 跳过检查直接通过
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WhenOnlyNonCriticalBomItems_ShouldPass()
    {
        var order = CreateCreatedOrder("WO-KC-0011", 100);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var jitRepo = new FakeJitPullSignalRepository();

        var bom = new Bom();
        bom.AddItem(BomItem.Create("CAP-MLCC-100N", "MLCC 100nF", 32, "PCS", level: 3, isCritical: false));
        bom.AddItem(BomItem.Create("RES-SMD-1K", "贴片电阻 1KΩ", 24, "PCS", level: 3, isCritical: false));
        var bomRepo = new FakeBomRepository(bom);

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.True(result.IsPassed);
        Assert.Empty(result.Items);
        Assert.Equal(OrderStatus.Released, order.Status);
    }

    // ══════════════════════════════════════════════════════
    //  Scenario 12: 小数量订单 — 非整数需求（plannedQty=3, QPU=2 → 需求 6）
    // ══════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ShouldRoundRequiredQuantity()
    {
        var order = CreateCreatedOrder("WO-KC-0012", 3);
        var orderRepo = new FakeOrderRepository(order);
        var batchRepo = new FakeMaterialBatchRepository();
        var bomRepo = new FakeBomRepository(CreateDoubleQtyBom());  // QPU=2 → 需求 6
        var jitRepo = new FakeJitPullSignalRepository();

        batchRepo.SetAvailable("ECU-ESP9-001", 5);  // 短缺 1

        var handler = new KitCheckHandler(orderRepo, batchRepo, bomRepo, jitRepo, Logger);
        var result = await handler.ExecuteAsync(new KitCheckCommand(order.Id), default);

        Assert.False(result.IsPassed);
        var ecuItem = result.Items.Single(i => i.MaterialCode == "ECU-ESP9-001");
        Assert.Equal(6, ecuItem.RequiredQuantity);
        Assert.Equal(5, ecuItem.AvailableQuantity);
        Assert.Equal(1, ecuItem.ShortageQuantity);
    }

    // ══════════════════════════════════════════════════════
    //  辅助方法
    // ══════════════════════════════════════════════════════

    private static ProductionOrder CreateCreatedOrder(string orderNumber, int plannedQty)
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

    private static Bom CreateDoubleQtyBom()
    {
        var bom = Bom.Create(Ulid.NewUlid(), "ESP-9.0", "V1.0", DateTimeOffset.UtcNow);
        bom.AddItem(BomItem.Create("ECU-ESP9-001", "ECU 电子控制单元 V3", 2, "PCS", level: 2, isCritical: true));
        return bom;
    }

    // ══════════════════════════════════════════════════════
    //  Fake 仓储实现
    // ══════════════════════════════════════════════════════

    private sealed class FakeOrderRepository : IProductionOrderRepository
    {
        private readonly ProductionOrder? _storedOrder;
        public int SaveChangesCallCount { get; private set; }

        public FakeOrderRepository(ProductionOrder? storedOrder) => _storedOrder = storedOrder;

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult<ProductionOrder?>(_storedOrder);

        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult<ProductionOrder?>(_storedOrder);

        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default)
        {
            var match = _storedOrder?.OrderNumber == orderNumber ? _storedOrder : null;
            return Task.FromResult(match);
        }

        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default)
        {
            var list = _storedOrder is not null ? new List<ProductionOrder> { _storedOrder } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default)
        {
            var list = _storedOrder is not null ? new List<ProductionOrder> { _storedOrder } : new List<ProductionOrder>();
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
            => Task.FromResult(_storedOrder is not null ? 1 : 0);

        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddAsync(ProductionOrder order, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Update(ProductionOrder order) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeMaterialBatchRepository : IMaterialBatchRepository
    {
        private readonly Dictionary<string, double> _available = [];

        public void SetAvailable(string materialCode, double quantity)
            => _available[materialCode] = quantity;

        public Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult<MaterialBatch?>(null);

        public Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult<MaterialBatch?>(null);

        public Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default)
            => Task.FromResult<MaterialBatch?>(null);

        public Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken ct = default)
            => Task.FromResult(new List<MaterialBatch>());

        public Task<List<MaterialBatch>> GetTrackedPageAsync(string materialCode, int skip, int take, CancellationToken ct = default)
            => Task.FromResult(new List<MaterialBatch>());

        public Task<int> CountAsync(string? materialCode, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task AddAsync(MaterialBatch batch, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => Task.FromResult(1);

        public Task<double> GetAvailableQuantityAsync(string materialCode, CancellationToken ct = default)
            => Task.FromResult(_available.GetValueOrDefault(materialCode, 0.0));

        public Task<Dictionary<string, double>> GetAvailableQuantitiesAsync(
            IEnumerable<string> materialCodes, CancellationToken ct = default)
        {
            var result = new Dictionary<string, double>();
            foreach (var code in materialCodes.Distinct())
                result[code] = _available.GetValueOrDefault(code, 0.0);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeBomRepository : IBomRepository
    {
        private readonly Bom? _bom;

        public FakeBomRepository(Bom? bom) => _bom = bom;

        public Task<Bom?> GetByProductAndVersionAsync(string productCode, string version, CancellationToken ct = default)
            => Task.FromResult(_bom);
    }

    private sealed class FakeJitPullSignalRepository : IJitPullSignalRepository
    {
        private readonly List<JitPullSignal> _trackedPending = [];
        public List<JitPullSignal> AddedSignals { get; } = [];
        public int SaveChangesCallCount { get; private set; }

        public void AddTrackedPending(JitPullSignal signal) => _trackedPending.Add(signal);

        public Task<JitPullSignal?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult<JitPullSignal?>(null);

        public Task<JitPullSignal?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        {
            var match = _trackedPending.Concat(AddedSignals).FirstOrDefault(s => s.Id == id);
            return Task.FromResult(match);
        }

        public Task<List<JitPullSignal>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        {
            var list = _trackedPending.Concat(AddedSignals).Where(s => s.OrderId == orderId).ToList();
            return Task.FromResult(list);
        }

        public Task<List<JitPullSignal>> GetPendingByOrderAndMaterialTrackedAsync(
            Ulid orderId, string materialCode, CancellationToken ct = default)
        {
            var list = _trackedPending
                .Where(s => s.OrderId == orderId && s.MaterialCode == materialCode && s.Status == JitPullStatus.Created)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<List<JitPullSignal>> GetPendingAsync(CancellationToken ct = default)
        {
            var list = _trackedPending.Where(s => s.Status == JitPullStatus.Created).ToList();
            return Task.FromResult(list);
        }

        public Task<List<JitPullSignal>> GetPageAsync(int skip, int take, CancellationToken ct = default)
        {
            var list = _trackedPending.Concat(AddedSignals).Skip(skip).Take(take).ToList();
            return Task.FromResult(list);
        }

        public Task<int> CountAsync(CancellationToken ct = default)
            => Task.FromResult(_trackedPending.Count + AddedSignals.Count);

        public Task AddAsync(JitPullSignal signal, CancellationToken ct = default)
        {
            AddedSignals.Add(signal);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }
}
