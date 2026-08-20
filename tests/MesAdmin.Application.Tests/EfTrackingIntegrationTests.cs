using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Caching;
using MesAdmin.Infrastructure.Sap;
using MesAdmin.Infrastructure.RealTime;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Domain.Models;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Features.ProductionOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

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
        var sapSyncRepo = scope.ServiceProvider.GetRequiredService<ISapOrderSyncRecordRepository>();
        var releaseHandler = new ReleaseOrderHandler(orders, sapSyncRepo);
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

        var sapSyncRepo2 = scope.ServiceProvider.GetRequiredService<ISapOrderSyncRecordRepository>();
        await new ReleaseOrderHandler(orders, sapSyncRepo2).ExecuteAsync(new ReleaseOrderCommand(order.Id), default);

        // 开工：StartOrderHandler 只发布事件，实际状态推进由 Saga 负责。
        // 此处模拟 Saga 行为（跟踪查询 → Start → SaveChanges），验证同一 scope 内不冲突。
        var orderForStart = await orders.GetByIdTrackedAsync(order.Id, default);
        Assert.NotNull(orderForStart);
        orderForStart!.Start();
        await orders.SaveChangesAsync(default);

        // 完工
        var goodsReceipts = scope.ServiceProvider.GetRequiredService<IGoodsReceiptRepository>();
        var sapOrderSyncRepo = scope.ServiceProvider.GetRequiredService<ISapOrderSyncRecordRepository>();
        var completeHandler = new CompleteOrderHandler(orders, goodsReceipts, sapOrderSyncRepo, NullLogger<CompleteOrderHandler>.Instance);
        var completed = await completeHandler.ExecuteAsync(
            new CompleteOrderCommand(order.Id, 5, 0, "TEST-REVIEWER"), default);
        Assert.Equal(OrderStatus.Completed, completed.Status);

        // 关闭
        var closeHandler = new CloseOrderHandler(orders, sapOrderSyncRepo);
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

    // ═══════════════════════════════════════════
    //  液压解锁：JSON owned collection + tracked 查询回归
    //  （AsNoTracking + Update 会触发 __synthesizedOrdinal shadow 键异常，
    //    解锁端点已改为 GetByIdTrackedAsync + SaveChanges，此处验证不再抛异常）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task HydraulicUnlock_TrackedQuery_ShouldNotThrowOnJsonOwnedCollection()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IHydraulicTestRepository>();

        // ── 1. 创建一条不合格记录（含 JSON owned 集合 SolenoidTests）→ 自动锁设备 ──
        var result = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-HYD-LOCK", 1);
        result.RecordPressureBuild(198);   // ✓ ≤250ms
        result.RecordHoldPressure(182);    // ✓ 175-185
        result.RecordPressureRelease(388); // ✗ >300ms → 锁止
        result.RecordLeakRate(0.71);       // ✗ >0.5 → 锁止
        result.AddSolenoidTest(new SolenoidValveTest(1, true, 12.5, null, null));
        result.AddSolenoidTest(new SolenoidValveTest(2, false, 89.2, 4.1, "F002")); // ✗
        result.Complete();

        Assert.True(result.EquipmentLocked);
        Assert.Equal(HydraulicTestStatus.Failed, result.Status);

        await repo.AddAsync(result, default);
        await repo.SaveChangesAsync(default);

        // ── 2. 模拟解锁端点：跟踪查询 → 解锁 → SaveChanges（此前在此抛 EF 409）──
        var tracked = await repo.GetByIdTrackedAsync(result.Id, default);
        Assert.NotNull(tracked);
        Assert.Equal(2, tracked!.SolenoidTests.Count); // JSON owned 集合完整加载

        tracked.UnlockEquipment("QE001");
        await repo.SaveChangesAsync(default); // 回归点：不抛 __synthesizedOrdinal 异常

        // ── 3. 验证解锁已持久化 ──
        var verify = await repo.GetByIdAsync(result.Id, default);
        Assert.NotNull(verify);
        Assert.False(verify!.EquipmentLocked);
        Assert.Equal(HydraulicTestStatus.Passed, verify.Status);
        Assert.Equal("QE001", verify.UnlockedBy);
        Assert.NotNull(verify.UnlockedAt);
        Assert.Equal(2, verify.SolenoidTests.Count);
        Assert.False(verify.SolenoidTests[1].ActuationPass); // 数据未被 Update 损坏
    }

    [Fact]
    public async Task HydraulicUnlock_WhenNotLocked_ShouldThrow()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IHydraulicTestRepository>();

        var result = HydraulicTestResult.Create("EQ-HYD-01", null, "SN-HYD-OK", 1);
        result.RecordPressureBuild(200);
        result.RecordHoldPressure(180);
        result.RecordPressureRelease(250);
        result.RecordLeakRate(0.3);
        result.AddSolenoidTest(new SolenoidValveTest(1, true, 11.0, null, null));
        result.Complete();
        Assert.Equal(HydraulicTestStatus.Passed, result.Status);
        Assert.False(result.EquipmentLocked);

        await repo.AddAsync(result, default);
        await repo.SaveChangesAsync(default);

        var tracked = await repo.GetByIdTrackedAsync(result.Id, default);
        Assert.NotNull(tracked);
        Assert.Throws<InvalidOperationException>(() => tracked!.UnlockEquipment("QE001"));
    }

    // ═══════════════════════════════════════════
    //  SAP 拒单写回（P0-3 修复）：跟踪查询 + MarkWrittenBack + SaveChanges 必须持久化
    //  （与液压解锁 GetByIdTrackedAsync 回归同模式；
    //   注意：GetByIdAsync 为 AsNoTracking，修改后 SaveChanges 会静默丢失——写回必须用跟踪查询）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task SapRejectionWriteback_TrackedQuery_ShouldPersistWrittenBack()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISapRejectionRepository>();

        var record = SapRejectionRecord.Create(
            "SAP-EXT-0002", "ESP-9.1", "BOM-IT-2", Ulid.NewUlid().ToString(), 5, "BOM 版本不符");
        await repo.AddAsync(record, default);
        await repo.SaveChangesAsync(default);

        // 跟踪查询 → MarkWrittenBack → SaveChanges（不调 Update，验证跟踪上下文生效）
        var tracked = await repo.GetByIdTrackedAsync(record.Id, default);
        Assert.NotNull(tracked);
        tracked!.MarkWrittenBack(DateTimeOffset.UtcNow);
        await repo.SaveChangesAsync(default);

        // 验证持久化
        var verify = await repo.GetByIdAsync(record.Id, default);
        Assert.NotNull(verify);
        Assert.Equal(RejectionWritebackStatus.WrittenBack, verify!.WritebackStatus);
        Assert.NotNull(verify.WritebackAt);
        Assert.Null(verify.WritebackError);
    }

    [Fact]
    public async Task SapRejectionWriteback_TrackedQuery_ShouldPersistFailed()
    {
        using var scope = _fixture.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISapRejectionRepository>();

        var record = SapRejectionRecord.Create(
            "SAP-EXT-0003", "ESP-9.0", "BOM-IT-3", Ulid.NewUlid().ToString(), 8, "工艺路线版本不符");
        await repo.AddAsync(record, default);
        await repo.SaveChangesAsync(default);

        var tracked = await repo.GetByIdTrackedAsync(record.Id, default);
        Assert.NotNull(tracked);
        tracked!.MarkFailed("SAP 不可达", DateTimeOffset.UtcNow);
        await repo.SaveChangesAsync(default);

        var verify = await repo.GetByIdAsync(record.Id, default);
        Assert.NotNull(verify);
        Assert.Equal(RejectionWritebackStatus.Failed, verify!.WritebackStatus);
        Assert.Equal("SAP 不可达", verify!.WritebackError);
        Assert.NotNull(verify.WritebackAt);
    }

    // ═══════════════════════════════════════════
    //  看板当日产量（P1）：以液压测试完成时间为口径统计合格/不良
    // ═══════════════════════════════════════════

    [Fact]
    public async Task HydraulicCountByCompletedPeriod_ShouldCountTodayQualifiedAndDefective()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IHydraulicTestRepository>();

        // 该计数为全局口径（不限设备/工单），先清空以隔离同集合内其他测试的插入数据。
        db.Set<HydraulicTestResult>().RemoveRange(db.Set<HydraulicTestResult>());
        await db.SaveChangesAsync(default);

        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);
        DateTimeOffset At(int hour) => new(today.Year, today.Month, today.Day, hour, 0, 0, TimeSpan.Zero);

        // 今日：2 合格 + 1 不合格（显式 UTC 偏移，避免 Npgsql Offset 异常）
        var pass1 = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-P1", 1);
        pass1.Complete();  // → Passed
        pass1.CompletedAt = At(9);
        await repo.AddAsync(pass1, default);

        var pass2 = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-P2", 1);
        pass2.Complete();
        pass2.CompletedAt = At(10);
        await repo.AddAsync(pass2, default);

        var fail1 = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-F1", 1);
        fail1.RecordPressureRelease(400);  // 泄压超时 → Failed
        fail1.Complete();
        fail1.CompletedAt = At(11);
        await repo.AddAsync(fail1, default);

        // 昨日：1 合格（不应计入今日）
        var yesterdayPass = HydraulicTestResult.Create("EQ-HYD-01", Ulid.NewUlid(), "SN-YP", 1);
        yesterdayPass.Complete();
        yesterdayPass.CompletedAt = new(today.AddDays(-1).Year, today.AddDays(-1).Month, today.AddDays(-1).Day, 23, 0, 0, TimeSpan.Zero);
        await repo.AddAsync(yesterdayPass, default);

        await repo.SaveChangesAsync(default);

        var (qualified, defective) = await repo.CountByCompletedPeriodAsync(today, tomorrow, default);
        Assert.Equal(2, qualified);
        Assert.Equal(1, defective);
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

    // ═══════════════════════════════════════════
    //  SAP 工单同步表存在性（修复空迁移导致缺表）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task SapOrderSyncRecords_TableShouldExistAndBeQueryable()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        // 插入一条待同步记录（复现 SapOrderSyncService 的写入路径）
        var record = SapOrderSyncRecord.Create(
            Ulid.NewUlid(), "WO-SAP-0001", "SAP-ORD-0001", OrderStatus.Released);
        db.SapOrderSyncRecords.Add(record);
        await db.SaveChangesAsync();

        // 复现后台服务的查询：WHERE NOT SapSynced ORDER BY CreatedAt
        var pending = await db.SapOrderSyncRecords
            .Where(r => !r.SapSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        Assert.Contains(pending, r => r.Id == record.Id);
    }

    // ═══════════════════════════════════════════
    //  工单列表多维过滤（#3 完善）
    // ═══════════════════════════════════════════

    [Fact]
    public async Task GetPage_WithFilter_ShouldFilterByOrderNumberProductAndDate()
    {
        using var scope = _fixture.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IProductionOrderRepository>();
        var opRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderOperationRepository>();
        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var createHandler = new CreateOrderHandler(orders, opRepo, routingRepo);

        var esp90 = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.0", "BOM-FLT-1", Ulid.NewUlid(), 10, (short)1), default);
        var esp91 = await createHandler.ExecuteAsync(
            new CreateOrderCommand("ESP-9.1", "BOM-FLT-2", Ulid.NewUlid(), 10, (short)1), default);

        // 按产品编码过滤（大小写不敏感）
        var byProduct = await orders.GetPageAsync(
            new Application.Common.OrderListFilter(ProductCode: "esp-9.1"), 0, 100, default);
        Assert.Contains(byProduct, o => o.Id == esp91.Id);
        Assert.DoesNotContain(byProduct, o => o.Id == esp90.Id);

        // 按工单号子串过滤
        var byNumber = await orders.GetPageAsync(
            new Application.Common.OrderListFilter(OrderNumberContains: esp90.OrderNumber), 0, 100, default);
        Assert.Single(byNumber);
        Assert.Equal(esp90.Id, byNumber[0].Id);

        // 按日期范围过滤（未来窗口应为空）
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var countFuture = await orders.CountAsync(
            new Application.Common.OrderListFilter(CreatedFrom: future), default);
        Assert.Equal(0, countFuture);
    }
}

/// <summary>
/// TX.2 — 数据库测试夹具：使用 Testcontainers PostgreSQL 17 启动独立容器。
/// 每个测试类集合共享一个容器（[Collection("DatabaseIntegration")] 顺序执行），
/// 每个测试方法用独立 scope（独立 DbContext），模拟真实 HTTP 请求。
///
/// ⚠ 启动容器约需 5-15 秒（首次拉取镜像），后续测试复用容器，速度显著提升。
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public ServiceProvider Services { get; private set; } = null!;

    public DatabaseFixture()
    {
        // ServiceProvider 在 InitializeAsync 中构建
    }

    public async Task InitializeAsync()
    {
        // ── 1. 启动 Testcontainers PostgreSQL ──
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("automes_test")
            .WithUsername("mes")
            .WithPassword("mes_dev_password")
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();

        // ── 2. 构建 DI 容器 ──
        var services = new ServiceCollection();
        services.AddDbContext<MesDbContext>(opt =>
            opt.UseNpgsql(_container.GetConnectionString()));

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

        // SAP 集成仓储（T1.3 拒单回写）
        services.AddScoped<ISapRejectionRepository, SapRejectionRepository>();

        // 质量体系仓储（T2.x）
        services.AddScoped<IQualityRecordRepository, QualityRecordRepository>();
        services.AddScoped<IInspectionPlanRepository, InspectionPlanRepository>();
        services.AddScoped<IHydraulicTestRepository, HydraulicTestRepository>();
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

        // T1.11 BOM 内存缓存
        services.AddSingleton<IBomCache, BomCache>();

        // SAP 集成仓储（T3.14）
        services.AddScoped<ISapOrderSyncRecordRepository, SapOrderSyncRecordRepository>();

        // 无日志 Provider（测试中 ILogger<T> 可正常解析，输出丢弃）
        services.AddLogging(b => b.ClearProviders());

        Services = services.BuildServiceProvider();

        // ── 3. 应用 Migration + 种子数据 ──
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.MigrateAsync();

        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("DatabaseFixture");
        await MesDataSeeder.SeedAsync(Services, logger);
    }

    public async Task DisposeAsync()
    {
        Services.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
