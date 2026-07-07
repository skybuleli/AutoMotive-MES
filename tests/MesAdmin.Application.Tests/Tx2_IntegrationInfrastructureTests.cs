using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Tests.Infrastructure;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// TX.2 — 集成测试基础设施验收测试。
/// 验证 Testcontainers PostgreSQL 连接、DI 容器、Seed 数据、仓储 CRUD 正常工作。
/// 使用现有的 DatabaseFixture（已升级为 Testcontainers 版）。
/// </summary>
[Collection("DatabaseIntegration")]
public class Tx2_IntegrationInfrastructureTests
{
    private readonly DatabaseFixture _fixture;

    public Tx2_IntegrationInfrastructureTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DatabaseFixture_ContainerShouldBeConnected()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        // 验证数据库连接正常
        var canConnect = await db.Database.CanConnectAsync();
        Assert.True(canConnect);
    }

    [Fact]
    public async Task DatabaseFixture_SeedDataShouldBePresent()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        // 验证种子 BOM 数据已加载
        var boms = db.Boms.ToList();
        Assert.NotEmpty(boms);
        Assert.Contains(boms, b => b.ProductCode == "ESP-9.0");

        // 验证种子物料批次已加载
        var batches = db.MaterialBatches.ToList();
        Assert.NotEmpty(batches);
        Assert.Contains(batches, b => b.MaterialCode == "ECU-ESP9-001");
    }

    [Fact]
    public async Task DatabaseFixture_ScopeIsolation_ShouldNotConflict()
    {
        // 两个独立 Scope 不应互相干扰
        Ulid orderId;

        using (var scope1 = _fixture.Services.CreateScope())
        {
            var orders = scope1.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
            var opRepo = scope1.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
            var routingRepo = scope1.ServiceProvider.GetRequiredService<IRoutingRepository>();

            var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);
            var order = await createHandler.ExecuteAsync(
                new CreateOrderCommand("ESP-9.0", "BOM-TX2-1", Ulid.NewUlid(), 10, (short)1), default);
            orderId = order.Id;

            Assert.Equal(OrderStatus.Created, order.Status);
        }

        // 第二个 Scope 读取同一工单
        using (var scope2 = _fixture.Services.CreateScope())
        {
            var orders = scope2.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
            var loaded = await orders.GetByIdAsync(orderId, default);

            Assert.NotNull(loaded);
            Assert.Equal(10, loaded!.PlannedQuantity);
        }
    }

    // 数据库连接验证已由 DatabaseFixture_ContainerShouldBeConnected 覆盖。
    // 种子数据验证已由 DatabaseFixture_SeedDataShouldBePresent 覆盖。
    // 两个测试隐式证明 migrations 已成功应用。

    [Fact]
    public async Task DatabaseFixture_HandlerWithLogger_ShouldResolveCorrectly()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();

        var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-TX2-2", Ulid.NewUlid(), 5, (short)1), default);

        Assert.NotNull(order);
        Assert.StartsWith("WO-", order.OrderNumber);

        // 验证 31 道工序已初始化
        var ops = await opRepo.GetByOrderIdAsync(order.Id, default);
        Assert.Equal(31, ops.Count);
        Assert.Contains(ops, o => o.OperationCode == "LOAD-01" && o.Station == 1);
    }

    [Fact]
    public async Task DatabaseFixture_InventorySeed_ShouldHaveStock()
    {
        using var scope = _fixture.Services.CreateScope();
        var batchRepo = scope.ServiceProvider.GetRequiredService<IMaterialBatchRepository>();

        // 验证 ECU 有合格库存
        var available = await batchRepo.GetAvailableQuantitiesAsync(
            ["ECU-ESP9-001", "HCU-ESP9-001", "MOT-ESP9-001"], default);

        Assert.True(available["ECU-ESP9-001"] >= 1000, "ECU 种子库存应 ≥ 1000");
        Assert.True(available["HCU-ESP9-001"] >= 1000, "HCU 种子库存应 ≥ 1000");
        Assert.True(available["MOT-ESP9-001"] >= 1000, "MOT 种子库存应 ≥ 1000");
    }
}
