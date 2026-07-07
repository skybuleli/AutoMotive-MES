using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Observability;
using MesAdmin.Application.Behaviors;
using MesAdmin.Application.DependencyInjection;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Features.Inventory;
using MesAdmin.Application.Features.Routing;
using MesAdmin.Application.Features.Scheduling;
using MesAdmin.Application.Sagas;
using MesAdmin.Infrastructure;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.DependencyInjection;
using MesAdmin.Infrastructure.Hubs;
using MesAdmin.Infrastructure.Logging;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.Sap;
using MesAdmin.Infrastructure.Caching;
using MesAdmin.Infrastructure.Workflows;
using MesAdmin.Infrastructure.Security;
using MesAdmin.Infrastructure.Reports;
using MesAdmin.Infrastructure.RealTime;
using MesAdmin.Infrastructure.Sync;
using MesAdmin.Infrastructure.Data.Repositories;
using FluentEmail.Smtp;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Net.Mail;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ValidateOnBuild disabled temporarily for preview; QualityReportService DI chain needs resolution
// builder.Host.UseDefaultServiceProvider((context, options) =>
// {
//     options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
//     options.ValidateOnBuild = context.HostingEnvironment.IsDevelopment();
// });

// ── FastEndpoints（REPR 模式 + 命令/事件总线）──
builder.Services.AddFastEndpoints();
builder.Services.AddMessaging();   // 启用命令总线 + 事件总线（handler 自动发现）
builder.Services.AddMesGeneratedServices();
builder.Services.AddCommandMiddleware(c =>
{
    c.Register(typeof(LoggingCommandMiddleware<,>));     // 最外层：日志
    c.Register(typeof(TransactionMiddleware<,>));        // 内层：事务
});
builder.Services.SwaggerDocument();

builder.Services.AddDbContext<MesDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

builder.Services.AddMesGeneratedInfrastructureServices();
builder.Services.AddHostedService<InventoryMonitoringService>();

// ── PLC + R3 OEE + SignalR 实时管道（T2.12-T2.15）──
// IPlcClient 由 AddRealtimePipeline 内部注册为 OpcUaPlcClient（单例）
builder.Services.AddRealtimePipeline(builder.Configuration);

// ── Cleipnir Saga 注册（Store + Registry 单例；Action 内部创建 Scope）──
builder.Services.AddCleipnirSagas(builder.Configuration);
builder.Services.AddScoped<IProductionOrderSagaRunner, CleipnirProductionOrderSagaRunner>();
builder.Services.AddScoped<ProductionOrderSaga>();

// ── JWT 认证 + 6 角色 RBAC ──
builder.Services.AddMesJwtAuthentication(builder.Configuration);

// ── QuestPDF 社区许可 ──
QuestPDF.Settings.License = LicenseType.Community;

// ── FluentEmail SMTP（质量报表邮件推送 T2.9）──
builder.Services.AddFluentEmail(builder.Configuration["QualityReports:Email:From"] ?? "automes@bosch.com")
    .AddSmtpSender(new SmtpClient
    {
        Host = builder.Configuration["QualityReports:Email:SmtpHost"] ?? "localhost",
        Port = builder.Configuration.GetValue<int>("QualityReports:Email:SmtpPort", 25),
        EnableSsl = builder.Configuration.GetValue<bool>("QualityReports:Email:EnableSsl"),
        Credentials = !string.IsNullOrEmpty(builder.Configuration["QualityReports:Email:Username"])
            ? new System.Net.NetworkCredential(
                builder.Configuration["QualityReports:Email:Username"],
                builder.Configuration["QualityReports:Email:Password"])
            : null
    });

// ── 质量报表服务（T2.9）──
builder.Services.AddSingleton<PdfReportGenerator>();
builder.Services.AddSingleton<QualityReportService>();
builder.Services.AddHostedService<QualityReportService>(sp => sp.GetRequiredService<QualityReportService>());

// ── 报表引擎服务（T4.1 + T4.2）──
builder.Services.AddSingleton<OeeReportStore>();
builder.Services.AddSingleton<ReportDataSourceService>();
builder.Services.AddSingleton<ReportEngineService>();

// ── OEE 日报定时推送（T4.2）──
builder.Services.AddSingleton<OeeDailyBackgroundService>();
builder.Services.AddHostedService<OeeDailyBackgroundService>(sp => sp.GetRequiredService<OeeDailyBackgroundService>());

// ── 综合月报定时推送（T4.3）──
builder.Services.AddSingleton<MonthlyBackgroundService>();
builder.Services.AddHostedService<MonthlyBackgroundService>(sp => sp.GetRequiredService<MonthlyBackgroundService>());

// ── T4.4 离线缓存同步 ──
builder.Services.AddScoped<IOfflineSyncRepository, OfflineSyncRepository>();
builder.Services.AddSingleton<OfflineSyncService>();
builder.Services.AddHostedService<OfflineCacheBackgroundService>();

// ── T4.5 断网重连自动同步 ──
builder.Services.AddSingleton<SagaReconciliationService>();
builder.Services.AddSingleton<OfflineReplayService>();
builder.Services.AddHostedService<ReconnectionBackgroundService>();

// ── 100% 在线液压测试管道（T2.6）──
builder.Services.AddScoped<IHydraulicTestRepository, HydraulicTestRepository>();
builder.Services.AddHostedService<HydraulicTestReactivePipeline>();

// ── 预防性维护（T2.17）──
builder.Services.AddScoped<IMaintenancePlanRepository, MaintenancePlanRepository>();
builder.Services.AddScoped<IMaintenanceWorkOrderRepository, MaintenanceWorkOrderRepository>();
builder.Services.AddHostedService<PreventiveMaintenanceService>();

// ── 备件管理（T2.18）──
builder.Services.AddScoped<ISparePartRepository, SparePartRepository>();
builder.Services.AddScoped<ISparePartUsageRepository, SparePartUsageRepository>();
builder.Services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();

// ── 工艺路线管理（T3.1/T3.2 M07）──
builder.Services.AddScoped<IRoutingRepository, RoutingRepository>();

// ── 防错三重校验（T3.3）──
builder.Services.AddScoped<TripleCheckService>();

// ── M09 排程管理 (T3.10-T3.13) ──
builder.Services.AddScoped<IScheduleRepository, ScheduleRepository>();
builder.Services.AddScoped<ICapacityCalendarRepository, CapacityCalendarRepository>();
builder.Services.AddScoped<SchedulingEngine>();

// ── M08 SQE 供应商质量模块 (T3.6-T3.8) ──
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<ISupplierScoreCardRepository, SupplierScoreCardRepository>();
builder.Services.AddScoped<IPpapDocumentRepository, PpapDocumentRepository>();
builder.Services.AddScoped<ICriticalSupplierSettingRepository, CriticalSupplierSettingRepository>();

// ── T1.11 BOM 内存缓存 ──
builder.Services.AddSingleton<IBomCache, BomCache>();
builder.Services.AddHostedService<BomCacheInitializationService>();

// ── SAP 集成 T3.14-T3.17 ──
builder.Services.AddScoped<ISapOrderSyncRecordRepository, SapOrderSyncRecordRepository>();
builder.Services.AddSingleton<ISapClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var useRealSap = config.GetValue<bool>("Sap:UseRealClient", false);
    if (useRealSap)
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<HttpSapClient>>();
        return new HttpSapClient(factory, config, logger);
    }
    var mockLogger = sp.GetRequiredService<ILogger<MockSapClient>>();
    return new MockSapClient(mockLogger);
});

// T3.16: 拒单回写后台服务（Poll pending rejections → writeback SAP）
builder.Services.AddHostedService<SapRejectionWritebackService>();
// T3.14: 工单状态同步后台服务
builder.Services.AddHostedService<SapOrderSyncService>();
// T3.15: 库存同步后台服务
builder.Services.AddHostedService<SapInventorySyncService>();
// T3.17: 物料移动同步后台服务
builder.Services.AddHostedService<SapMaterialMovementSyncService>();

// ── ZLogger 结构化日志 ──
builder.Logging.AddZLogger();

// ── OpenTelemetry Metrics -> GreptimeDB OTLP ──
var otlpEndpoint = NormalizeGreptimeMetricsEndpoint(
    builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4000/v1/otlp");
var otelServiceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "MesAdmin.Api";
var otelServiceNamespace = builder.Configuration["OTEL_SERVICE_NAMESPACE"] ?? "AutoMES";
var greptimeDbName = builder.Configuration["Observability:GreptimeDbName"] ?? "public";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: otelServiceName, serviceNamespace: otelServiceNamespace))
    .WithMetrics(metrics => metrics
        .AddMeter(AutoMesMetrics.MeterName)
        .AddOtlpExporter((exporter, reader) =>
        {
            exporter.Endpoint = new Uri(otlpEndpoint);
            exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
            exporter.Headers = $"X-Greptime-DB-Name={greptimeDbName}";
            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
        }));

var app = builder.Build();

// ── 启动时自动应用 EF Core Migration + 种子数据（幂等）──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    await db.Database.MigrateAsync();

    // T1.4/T1.13 种子数据：ESP BOM、库存阈值、初始库存
    // 仅首次启动时写入，已存在数据则跳过（幂等）
    await MesDataSeeder.SeedAsync(app.Services, app.Services.GetRequiredService<ILogger<Program>>());
}

// ── 健康检查端点（所有环境，用于 Docker HEALTHCHECK）──
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "MesAdmin.Api",
    timestamp = DateTime.UtcNow
}))
    .ExcludeFromDescription();   // 不出现在 Swagger 文档中

// ── 中间件管道 ──
app.UseAuthentication();
app.UseAuthorization();

// ── FastEndpoints 路由注册（ProblemDetails + 全局异常后处理器）──
app.UseFastEndpoints(config =>
{
    config.Errors.UseProblemDetails();
    // 全局后处理器：领域异常 → HTTP 状态码映射（替代原 IMiddleware 中间件）
    config.Endpoints.Configurator = ep =>
    {
        ep.PostProcessor<GlobalExceptionPostProcessor>(Order.After);
    };
});

// ── Swagger UI（开发环境）──
if (app.Environment.IsDevelopment())
    app.UseSwaggerGen();

// ── SignalR 端点（MemoryPack 二进制协议）──
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<AndonHub>("/hubs/andon");

app.Run();

static string NormalizeGreptimeMetricsEndpoint(string configuredEndpoint)
{
    var endpoint = configuredEndpoint.TrimEnd('/');

    if (endpoint.EndsWith("/v1/otlp/v1/metrics", StringComparison.OrdinalIgnoreCase))
        return endpoint;

    if (endpoint.EndsWith("/v1/otlp", StringComparison.OrdinalIgnoreCase))
        return endpoint + "/v1/metrics";

    return endpoint + "/v1/otlp/v1/metrics";
}
