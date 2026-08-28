using MesAdmin.Application.Features.Inspections;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

/// <summary>
/// S02 · 检验量具强制校验集成测试。
/// 验证：过期/已报废量具禁止用于首件检验录入与 SPC 样本登记；有效量具可通过。
/// </summary>
[Collection("DatabaseIntegration")]
public class GaugeEnforcementTests
{
    private readonly DatabaseFixture _fixture;

    public GaugeEnforcementTests(DatabaseFixture fixture) => _fixture = fixture;

    private static Gauge CreateValidGauge(string number)
        => Gauge.Create(Ulid.NewUlid(), number, "测试量具", GaugeType.Caliper,
            "150mm", "0.02mm", "1 级", 365, DateTimeOffset.UtcNow.AddDays(-10));

    private static Gauge CreateExpiredGauge(string number)
        => Gauge.Create(Ulid.NewUlid(), number, "过期量具", GaugeType.Caliper,
            "150mm", "0.02mm", "1 级", 30, DateTimeOffset.UtcNow.AddDays(-60));

    [Fact]
    public async Task FaiRecord_WithoutGauge_ShouldThrow()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var faiRepo = scope.ServiceProvider.GetRequiredService<IFirstArticleInspectionRepository>();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var order = await new CreateOrderHandler(orders, opRepo, routing).ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-IT-1", Ulid.NewUlid(), 10, 1), default);

        var insp = await new CreateInspectionHandler(faiRepo, orders, gaugeRepo)
            .ExecuteAsync(new CreateInspectionCommand(order.Id, "ShiftStart", "OP-1"), default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RecordInspectionValueHandler(faiRepo, gaugeRepo)
                .ExecuteAsync(new RecordInspectionValueCommand(insp.Id, "DIM-01", 12.01), default));
        Assert.Contains("必须选择在校准有效期内", ex.Message);
    }

    [Fact]
    public async Task FaiRecord_WithExpiredGauge_ShouldThrow()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var faiRepo = scope.ServiceProvider.GetRequiredService<IFirstArticleInspectionRepository>();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var order = await new CreateOrderHandler(orders, opRepo, routing).ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-IT-2", Ulid.NewUlid(), 5, 1), default);

        var expired = CreateExpiredGauge($"GT-FE-{Ulid.NewUlid().ToString()[..6]}");
        await gaugeRepo.AddAsync(expired);

        var insp = await new CreateInspectionHandler(faiRepo, orders, gaugeRepo)
            .ExecuteAsync(new CreateInspectionCommand(order.Id, "ShiftStart", "OP-1"), default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RecordInspectionValueHandler(faiRepo, gaugeRepo)
                .ExecuteAsync(new RecordInspectionValueCommand(insp.Id, "DIM-01", 12.0, expired.Id), default));
        Assert.Contains("校准已过期", ex.Message);
    }

    [Fact]
    public async Task FaiRecord_WithValidGauge_ShouldSucceed()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var faiRepo = scope.ServiceProvider.GetRequiredService<IFirstArticleInspectionRepository>();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var order = await new CreateOrderHandler(orders, opRepo, routing).ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-IT-3", Ulid.NewUlid(), 5, 1), default);

        var gauge = CreateValidGauge($"GT-FV-{Ulid.NewUlid().ToString()[..6]}");
        await gaugeRepo.AddAsync(gauge);

        var insp = await new CreateInspectionHandler(faiRepo, orders, gaugeRepo)
            .ExecuteAsync(new CreateInspectionCommand(order.Id, "ShiftStart", "OP-1", gauge.Id), default);

        var updated = await new RecordInspectionValueHandler(faiRepo, gaugeRepo)
            .ExecuteAsync(new RecordInspectionValueCommand(insp.Id, "DIM-01", 12.01, gauge.Id), default);

        Assert.Equal(gauge.Id, updated.GaugeId);
        var item = updated.Items.First(i => i.CharacteristicCode == "DIM-01");
        Assert.NotNull(item.ActualValue);
        Assert.True(item.IsPass);
    }

    [Fact]
    public async Task SpcManual_WithoutGauge_ShouldThrow()
    {
        using var scope = _fixture.Services.CreateScope();
        var sampleRepo = scope.ServiceProvider.GetRequiredService<ISpcSampleRepository>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<ISpcRuleAlertRepository>();
        var planRepo = scope.ServiceProvider.GetRequiredService<IInspectionPlanRepository>();
        var gaugeRepo = scope.ServiceProvider.GetRequiredService<IGaugeRepository>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RecordSpcSampleHandler(sampleRepo, alertRepo, planRepo, gaugeRepo)
                .ExecuteAsync(new RecordSpcSampleCommand("CH-XX", null, null, null, [22.0, 22.1, 22.0, 22.2, 22.1], "Manual", null), default));
        Assert.Contains("必须选择", ex.Message);
    }
}
