using MesAdmin.Application.Features.Dashboard;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

public class DashboardSummaryHandlerTests
{
    [Fact]
    public async Task Execute_ShouldNotRunRepositoryQueriesConcurrently()
    {
        var orders = new ConcurrencyCheckingOrderRepository();
        var alerts = new ConcurrencyCheckingAlertRepository();
        var hydraulics = new FakeHydraulicRepository();
        var handler = new DashboardSummaryHandler(orders, alerts, hydraulics);

        var summary = await handler.ExecuteAsync(new DashboardSummaryQuery(), default);

        Assert.Equal(1, summary.CreatedCount);
        Assert.Equal(2, summary.ReleasedCount);
        Assert.Equal(3, summary.InProgressCount);
        Assert.Equal(4, summary.CompletedCount);
        Assert.Equal(5, summary.ClosedCount);
        Assert.Equal(6, summary.ActiveAlerts);
        Assert.Equal(7, summary.RedAlerts);
        Assert.Equal(8, summary.YellowAlerts);
    }

    [Fact]
    public async Task Execute_ShouldReturnTodayQualifiedAndDefectiveFromHydraulicTests()
    {
        var orders = new ConcurrencyCheckingOrderRepository();
        var alerts = new ConcurrencyCheckingAlertRepository();
        var hydraulics = new FakeHydraulicRepository(qualified: 120, defective: 3);
        var handler = new DashboardSummaryHandler(orders, alerts, hydraulics);

        var summary = await handler.ExecuteAsync(new DashboardSummaryQuery(), default);

        Assert.Equal(120, summary.TodayQualified);
        Assert.Equal(3, summary.TodayDefective);
    }

    private sealed class FakeHydraulicRepository : IHydraulicTestRepository
    {
        private readonly int _qualified;
        private readonly int _defective;

        public FakeHydraulicRepository(int qualified = 0, int defective = 0)
        {
            _qualified = qualified;
            _defective = defective;
        }

        public Task<(int Qualified, int Defective)> CountByCompletedPeriodAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
            => Task.FromResult((_qualified, _defective));

        public Task<HydraulicTestResult?> GetByIdAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<HydraulicTestResult?>(null);
        public Task<HydraulicTestResult?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<HydraulicTestResult?>(null);
        public Task<List<HydraulicTestResult>> GetByEquipmentAsync(string equipmentCode, int limit = 50, CancellationToken ct = default) => Task.FromResult(new List<HydraulicTestResult>());
        public Task<List<HydraulicTestResult>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default) => Task.FromResult(new List<HydraulicTestResult>());
        public Task<HydraulicTestResult?> GetLatestByEquipmentAsync(string equipmentCode, CancellationToken ct = default) => Task.FromResult<HydraulicTestResult?>(null);
        public Task AddAsync(HydraulicTestResult result, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(HydraulicTestResult result) { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class ConcurrencyGate
    {
        private int _active;

        public async Task<T> RunAsync<T>(T value)
        {
            if (Interlocked.Increment(ref _active) != 1)
                throw new InvalidOperationException("Repository query ran concurrently");

            await Task.Delay(1);
            Interlocked.Decrement(ref _active);
            return value;
        }
    }

    private sealed class ConcurrencyCheckingOrderRepository : IProductionOrderRepository
    {
        private readonly ConcurrencyGate _gate = new();

        public Task<int> CountAsync(OrderStatus? status, CancellationToken ct = default)
            => _gate.RunAsync(status switch
            {
                OrderStatus.Created => 1,
                OrderStatus.Released => 2,
                OrderStatus.InProgress => 3,
                OrderStatus.Completed => 4,
                OrderStatus.Closed => 5,
                _ => 0,
            });

        public Task<ProductionOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<ProductionOrder?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<ProductionOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken ct = default) => Task.FromResult<ProductionOrder?>(null);
        public Task<List<ProductionOrder>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(new List<ProductionOrder>());
        public Task<List<ProductionOrder>> GetPageAsync(OrderStatus? status, int skip, int take, CancellationToken ct = default) => Task.FromResult(new List<ProductionOrder>());
        public Task<int> CountByOrderNumberPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddAsync(ProductionOrder order, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(ProductionOrder order) { }
        public Task<List<ProductionOrder>> GetByPeriodAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default) => Task.FromResult(new List<ProductionOrder>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class ConcurrencyCheckingAlertRepository : IInventoryAlertRepository
    {
        private readonly ConcurrencyGate _gate = new();

        public Task<int> CountActiveAsync(CancellationToken ct = default) => _gate.RunAsync(6);

        public Task<int> CountByLevelAsync(InventoryAlertLevel level, CancellationToken ct = default)
            => _gate.RunAsync(level == InventoryAlertLevel.Red ? 7 : 8);

        public Task<InventoryAlert?> GetByIdAsync(Ulid id, CancellationToken ct = default) => Task.FromResult<InventoryAlert?>(null);
        public Task<List<InventoryAlert>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult(new List<InventoryAlert>());
        public Task<List<InventoryAlert>> GetByMaterialCodeAsync(string materialCode, CancellationToken ct = default) => Task.FromResult(new List<InventoryAlert>());
        public Task<InventoryAlert?> GetLatestByMaterialAsync(string materialCode, string? stationId, CancellationToken ct = default) => Task.FromResult<InventoryAlert?>(null);
        public Task<InventoryAlert?> GetLatestByMaterialTrackedAsync(string materialCode, string? stationId, CancellationToken ct = default) => Task.FromResult<InventoryAlert?>(null);
        public Task AddAsync(InventoryAlert alert, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
