using MesAdmin.Application.Features.Quality;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Tests;

public class InspectionPlanResolverTests
{
    [Fact]
    public async Task ResolveAsync_EmptyPlanId_ShouldReturnUlidEmptySentinel_NotRandomGhost()
    {
        var planRepo = new FakeInspectionPlanRepository();
        var fallback = () => new List<MeasuredCharacteristic> { MeasuredCharacteristic.Create("T-1", "测试", 1, "-") };

        var (planId, characteristics) = await InspectionPlanResolver.ResolveAsync("", planRepo, fallback, default);

        Assert.Equal(Ulid.Empty, planId);   // 哨兵值，而非随机幽灵 Id
        Assert.Single(characteristics);
        Assert.Equal("T-1", characteristics[0].CharacteristicCode);
    }

    [Fact]
    public async Task ResolveAsync_InvalidPlanId_ShouldReturnUlidEmptySentinel_NotRandomGhost()
    {
        var planRepo = new FakeInspectionPlanRepository();
        var fallback = () => new List<MeasuredCharacteristic> { MeasuredCharacteristic.Create("T-1", "测试", 1, "-") };

        var (planId, _) = await InspectionPlanResolver.ResolveAsync("not-a-ulid", planRepo, fallback, default);

        Assert.Equal(Ulid.Empty, planId);
    }

    [Fact]
    public async Task ResolveAsync_ExistingPlanId_ShouldReturnPlanIdAndCopiedCharacteristics()
    {
        var plan = InspectionPlan.Create("来料检验", "v1", InspectionStage.Iq, "每批", 5, 0, 1, DateTimeOffset.UtcNow);
        plan.AddCharacteristic(PlanCharacteristic.CreateVariable("DIM-01", "尺寸", 120.0, "mm", 120.5, 119.5));
        var planRepo = new FakeInspectionPlanRepository(plan);

        var (planId, characteristics) = await InspectionPlanResolver.ResolveAsync(
            plan.Id.ToString(), planRepo, () => [], default);

        Assert.Equal(plan.Id, planId);
        Assert.Single(characteristics);
        Assert.Equal("DIM-01", characteristics[0].CharacteristicCode);
        Assert.Equal(120.5, characteristics[0].UpperSpecLimit);
    }

    [Fact]
    public async Task ResolveAsync_NonExistingPlanId_ShouldReturnUlidEmptySentinel_AndFallbackTemplate()
    {
        var planRepo = new FakeInspectionPlanRepository();
        var fallback = () => new List<MeasuredCharacteristic> { MeasuredCharacteristic.Create("F-1", "回退", 1, "-") };

        var (planId, characteristics) = await InspectionPlanResolver.ResolveAsync(
            Ulid.NewUlid().ToString(), planRepo, fallback, default);

        Assert.Equal(Ulid.Empty, planId);
        Assert.Equal("F-1", characteristics[0].CharacteristicCode);
    }

    private sealed class FakeInspectionPlanRepository(InspectionPlan? plan = null) : IInspectionPlanRepository
    {
        public Task<InspectionPlan?> GetByIdAsync(Ulid id, CancellationToken ct = default)
            => Task.FromResult(plan is not null && plan.Id == id ? plan : null);

        public Task<InspectionPlan?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
            => GetByIdAsync(id, ct);

        public Task<List<InspectionPlan>> GetByProductCodeAsync(string productCode, InspectionStage stage, CancellationToken ct = default)
            => Task.FromResult(new List<InspectionPlan>());

        public Task<List<InspectionPlan>> GetEnabledAsync(CancellationToken ct = default)
            => Task.FromResult(new List<InspectionPlan>());

        public Task AddAsync(InspectionPlan plan, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(InspectionPlan plan) { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}