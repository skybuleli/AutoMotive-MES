using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MesAdmin.Application.Features.ProductionOrders;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Infrastructure.RealTime;
using Microsoft.Extensions.Logging;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 数据库集成测试集合定义。确保所有需要 PostgreSQL 的测试类按顺序执行，
/// 避免并行创建工单导致的 OrderNumber 唯一约束冲突。
/// </summary>
[CollectionDefinition("DatabaseIntegration")]
public class DatabaseIntegrationTestCollection : ICollectionFixture<DatabaseFixture> { }

/// <summary>用于测试的最小化 NullLogger 实现。</summary>
internal sealed class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

/// <summary>
/// 集成测试：用真实 PostgreSQL + MesDbContext 复现 EF Core 跟踪冲突。
/// 验证"创建→放行→开工"完整流程不报 "already being tracked" 异常。
/// </summary>
[Collection("DatabaseIntegration")]
public class EfTrackingIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public EfTrackingIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Release_Then_Start_ShouldNotThrowTrackingConflict()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();

        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();

        // 创建工单
        var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);
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

        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();

        // 创建 + 放行（同一 scope 内连续操作）
        var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);
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
        var completeHandler = new CompleteOrderHandler(orders, goodsReceipts, NullLogger<CompleteOrderHandler>.Instance);
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

        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);
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

    [Fact]
    public async Task InventoryAlertRepository_ShouldScopeLatestAlertByStation()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var alerts = new InventoryAlertRepository(db);

        var stn02 = InventoryAlert.Create(
            "ECU-ESP9-001", "ECU 电子控制单元 V3", 120, 750, 250,
            InventoryAlertLevel.Red, "STN-02");
        var stn04 = InventoryAlert.Create(
            "ECU-ESP9-001", "ECU 电子控制单元 V3", 180, 750, 250,
            InventoryAlertLevel.Yellow, "STN-04");

        await alerts.AddAsync(stn02, default);
        await alerts.AddAsync(stn04, default);
        await alerts.SaveChangesAsync(default);

        var latestStn02 = await alerts.GetLatestByMaterialAsync("ECU-ESP9-001", "STN-02", default);
        var latestStn04 = await alerts.GetLatestByMaterialAsync("ECU-ESP9-001", "STN-04", default);

        Assert.Equal(stn02.Id, latestStn02!.Id);
        Assert.Equal(InventoryAlertLevel.Red, latestStn02.AlertLevel);
        Assert.Equal(stn04.Id, latestStn04!.Id);
        Assert.Equal(InventoryAlertLevel.Yellow, latestStn04.AlertLevel);
    }
}

/// <summary>
/// 数据库测试夹具：共享一个 ServiceProvider，连接到开发环境 PostgreSQL。
/// 每个测试方法用独立 scope（独立 DbContext），模拟真实 HTTP 请求。
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public ServiceProvider Services { get; }

    public DatabaseFixture()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MesDbContext>(opt =>
            opt.UseNpgsql("Host=localhost;Port=5432;Database=automes_test;Username=mes;Password=mes_dev_password"));

        // 仓储注册：全生命周期（T1.x - T1.17）
        services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
        services.AddScoped<IWorkOrderOperationRepository, WorkOrderOperationRepository>();
        services.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
        services.AddScoped<IMaterialBatchRepository, MaterialBatchRepository>();
        services.AddScoped<IMaterialBindingRepository, MaterialBindingRepository>();
        services.AddScoped<IBomRepository, BomRepository>();
        services.AddScoped<IJitPullSignalRepository, JitPullSignalRepository>();
        services.AddScoped<IMaterialConsumptionRepository, MaterialConsumptionRepository>();
        services.AddScoped<IConsumptionVarianceRepository, ConsumptionVarianceRepository>();
        services.AddScoped<ISapInventorySyncRecordRepository, SapInventorySyncRecordRepository>();

        // 质量体系仓储（T2.x）
        services.AddScoped<IQualityRecordRepository, QualityRecordRepository>();
        services.AddScoped<IInspectionPlanRepository, InspectionPlanRepository>();
        services.AddScoped<ISpcSampleRepository, SpcSampleRepository>();
        services.AddScoped<ISpcRuleAlertRepository, SpcRuleAlertRepository>();
        services.AddScoped<INonConformanceReportRepository, NonConformanceReportRepository>();
        services.AddScoped<IEightDReportRepository, EightDReportRepository>();

        // Andon 报警仓储（T2.20-T2.23）
        services.AddScoped<IAndonEventRepository, AndonEventRepository>();

        // 预防性维护仓储（T2.17）
        services.AddScoped<IMaintenancePlanRepository, MaintenancePlanRepository>();
        services.AddScoped<IMaintenanceWorkOrderRepository, MaintenanceWorkOrderRepository>();

        // 备件管理仓储（T2.18）
        services.AddScoped<ISparePartRepository, SparePartRepository>();
        services.AddScoped<ISparePartUsageRepository, SparePartUsageRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();

        // 工艺路线仓储（T3.1/T3.2 M07）
        services.AddScoped<IRoutingRepository, RoutingRepository>();

        // 无日志 Provider（测试中 ILogger<T> 可正常解析，输出丢弃）
        services.AddLogging(b => b.ClearProviders());

        Services = services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        // 应用所有 migration
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // 种子 BOM + 物料库存数据（幂等：已存在则跳过）
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DatabaseFixture");
        await MesDataSeeder.SeedAsync(Services, logger);
    }

    public Task DisposeAsync()
    {
        Services.Dispose();
        return Task.CompletedTask;
    }
}
