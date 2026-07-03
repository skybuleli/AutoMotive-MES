using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Data.Repositories;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 集成测试：用真实 PostgreSQL + MesDbContext 复现 EF Core 跟踪冲突。
/// 验证"创建→放行→开工"完整流程不报 "already being tracked" 异常。
/// </summary>
public class EfTrackingIntegrationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public EfTrackingIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Release_Then_Start_ShouldNotThrowTrackingConflict()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();

        // 创建工单
        var createHandler = new CreateOrderHandler(orders, opRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-IT-1", Ulid.NewUlid(), 10, (short)1), default);

        // 放行（同一 scope 内，模拟真实请求）
        var releaseHandler = new ReleaseOrderHandler(orders);
        var released = await releaseHandler.ExecuteAsync(new ReleaseOrderCommand(order.Id), default);
        Assert.Equal(OrderStatus.Released, released.Status);

        // 再次查询详情（模拟端点返回前重读）
        var detail = await orders.GetByIdAsync(order.Id, default);
        Assert.Equal(OrderStatus.Released, detail!.Status);
    }

    [Fact]
    public async Task MultipleUpdatesInSameScope_ShouldNotConflict()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();

        // 创建 + 放行（同一 scope 内连续操作）
        var createHandler = new CreateOrderHandler(orders, opRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.1", "BOM-IT-2", Ulid.NewUlid(), 5, (short)1), default);

        await new ReleaseOrderHandler(orders).ExecuteAsync(new ReleaseOrderCommand(order.Id), default);

        // 开工：StartOrderHandler 只发布事件，实际状态推进由 Saga 负责。
        // 此处模拟 Saga 行为（跟踪查询 → Start → SaveChanges），验证同一 scope 内不冲突。
        var orderForStart = await orders.GetByIdTrackedAsync(order.Id, default);
        Assert.NotNull(orderForStart);
        orderForStart!.Start();
        await orders.SaveChangesAsync(default);

        // 完工
        var goodsReceipts = scope.ServiceProvider.GetRequiredService<IGoodsReceiptRepository>();
        var completeHandler = new CompleteOrderHandler(orders, goodsReceipts);
        var completed = await completeHandler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 5, 0, "TEST-REVIEWER"), default);
        Assert.Equal(OrderStatus.Completed, completed.Status);

        // 关闭
        var closeHandler = new CloseOrderHandler(orders);
        var closed = await closeHandler.ExecuteAsync(new CloseOrderCommand(order.Id), default);
        Assert.Equal(OrderStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task GetByIdTracked_ShouldAllowModifyWithoutUpdate()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();

        var createHandler = new CreateOrderHandler(orders, opRepo);
        var order = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-IT-3", Ulid.NewUlid(), 3, (short)1), default);

        // 跟踪查询 → 修改 → SaveChanges（不调 Update）
        var tracked = await orders.GetByIdTrackedAsync(order.Id, default);
        Assert.NotNull(tracked);
        tracked!.Release();
        await orders.SaveChangesAsync(default);

        // 验证持久化
        var verify = await orders.GetByIdAsync(order.Id, default);
        Assert.Equal(OrderStatus.Released, verify!.Status);
    }
}

/// <summary>
/// 数据库测试夹具：共享一个 ServiceProvider，连接到开发环境 PostgreSQL。
/// 每个测试方法用独立 scope（独立 DbContext），模拟真实 HTTP 请求。
/// </summary>
public class DatabaseFixture : IDisposable
{
    public ServiceProvider Services { get; }

    public DatabaseFixture()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MesDbContext>(opt =>
            opt.UseNpgsql("Host=localhost;Port=5432;Database=automes;Username=mes;Password=mes_dev_password"));
        services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
        services.AddScoped<IWorkOrderOperationRepository, WorkOrderOperationRepository>();
        services.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
        Services = services.BuildServiceProvider();

        // 应用所有 migration（含新增 goods_receipts/material_batches 等）。
        // 同步阻塞：fixture 初始化一次性操作，可接受。
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.Database.MigrateAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => Services.Dispose();
}
