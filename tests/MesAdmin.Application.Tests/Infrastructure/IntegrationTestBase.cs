using MesAdmin.Infrastructure.Caching;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Sap;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace MesAdmin.Application.Tests.Infrastructure;

/// <summary>
/// TX.2 — 集成测试基类。
/// 提供 Testcontainers PostgreSQL 容器生命周期 + DI ServiceProvider + Scope 创建。
/// 所有使用 DatabaseFixture 的集成测试应改为继承此类。
///
/// 使用方式：
/// <code>
/// public class MyIntegrationTest : IntegrationTestBase
/// {
///     [Fact]
///     public async Task MyTest()
///     {
///         using var scope = CreateScope();
///         var repo = scope.ServiceProvider.GetRequiredService&lt;IMyRepository&gt;();
///         // ... 测试逻辑
///     }
/// }
/// </code>
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private ServiceProvider? _serviceProvider;

    /// <summary>PostgreSQL 测试容器实例。</summary>
    protected PostgreSqlContainer Container => _container ?? throw new InvalidOperationException("容器未初始化");

    /// <summary>服务提供器。</summary>
    protected ServiceProvider Services => _serviceProvider ?? throw new InvalidOperationException("服务未初始化");

    /// <summary>创建 DI Scope（每个测试方法创建独立 Scope = 独立 DbContext）。调用方负责 Dispose。</summary>
    protected IServiceScope CreateScope() => Services.CreateScope();

    public virtual async Task InitializeAsync()
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

        // 仓储注册（全生命周期 T1.x-T3.x）
        RegisterRepositories(services);

        services.AddLogging(b => b.ClearProviders());

        _serviceProvider = services.BuildServiceProvider();

        // ── 3. 应用 Migration + 种子数据 ──
        await InitializeDatabaseAsync();
    }

    /// <summary>
    /// 注册所有仓储到 DI 容器。
    /// 子类可重写此方法以注册额外服务。
    /// </summary>
    protected virtual void RegisterRepositories(IServiceCollection services)
    {
        // 工单模块（T1.x）
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

        // 质量体系（T2.x）
        services.AddScoped<IQualityRecordRepository, QualityRecordRepository>();
        services.AddScoped<IInspectionPlanRepository, InspectionPlanRepository>();
        services.AddScoped<ISpcSampleRepository, SpcSampleRepository>();
        services.AddScoped<ISpcRuleAlertRepository, SpcRuleAlertRepository>();
        services.AddScoped<INonConformanceReportRepository, NonConformanceReportRepository>();
        services.AddScoped<IEightDReportRepository, EightDReportRepository>();

        // Andon 报警（T2.20-T2.23）
        services.AddScoped<IAndonEventRepository, AndonEventRepository>();

        // 预防性维护（T2.17）
        services.AddScoped<IMaintenancePlanRepository, MaintenancePlanRepository>();
        services.AddScoped<IMaintenanceWorkOrderRepository, MaintenanceWorkOrderRepository>();

        // 备件管理（T2.18）
        services.AddScoped<ISparePartRepository, SparePartRepository>();
        services.AddScoped<ISparePartUsageRepository, SparePartUsageRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();

        // 工艺路线（T3.1/T3.2）
        services.AddScoped<IRoutingRepository, RoutingRepository>();

        // SAP 集成（T3.14）
        services.AddScoped<ISapOrderSyncRecordRepository, SapOrderSyncRecordRepository>();

        // BOM 内存缓存（T1.11）
        services.AddSingleton<IBomCache, BomCache>();
    }

    /// <summary>
    /// 应用 Migration + 种子数据。
    /// 子类可重写以使用自定义种子逻辑。
    /// </summary>
    protected virtual async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.MigrateAsync();

        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("IntegrationTestBase");
        await MesDataSeeder.SeedAsync(Services, logger);
    }

    public virtual async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
