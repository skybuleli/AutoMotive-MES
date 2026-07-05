using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Behaviors;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Features.Inventory;
using MesAdmin.Application.Sagas;
using MesAdmin.Infrastructure;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Hubs;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Logging;
using MesAdmin.Infrastructure.Plc;
using MesAdmin.Infrastructure.Workflows;
using MesAdmin.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// ── FastEndpoints（REPR 模式 + 命令/事件总线）──
builder.Services.AddFastEndpoints();
builder.Services.AddMessaging();   // 启用命令总线 + 事件总线（handler 自动发现）
builder.Services.AddCommandMiddleware(c =>
{
    c.Register(typeof(LoggingCommandMiddleware<,>));     // 最外层：日志
    c.Register(typeof(TransactionMiddleware<,>));        // 内层：事务
});
builder.Services.SwaggerDocument();

builder.Services.AddDbContext<MesDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

builder.Services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
builder.Services.AddScoped<IWorkOrderOperationRepository, WorkOrderOperationRepository>();
builder.Services.AddScoped<IFirstArticleInspectionRepository, FirstArticleInspectionRepository>();
builder.Services.AddScoped<ITraceabilityLinkRepository, TraceabilityLinkRepository>();
builder.Services.AddScoped<ISapRejectionRepository, SapRejectionRepository>();
builder.Services.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
builder.Services.AddScoped<IMaterialBatchRepository, MaterialBatchRepository>();
builder.Services.AddScoped<IMaterialBindingRepository, MaterialBindingRepository>();
builder.Services.AddScoped<IJitPullSignalRepository, JitPullSignalRepository>();
builder.Services.AddScoped<IBomRepository, BomRepository>();
builder.Services.AddScoped<IMaterialInventorySettingRepository, MaterialInventorySettingRepository>();
builder.Services.AddScoped<IInventoryAlertRepository, InventoryAlertRepository>();
builder.Services.AddScoped<IMaterialConsumptionRepository, MaterialConsumptionRepository>();
builder.Services.AddScoped<IConsumptionVarianceRepository, ConsumptionVarianceRepository>();
builder.Services.AddScoped<ISapInventorySyncRecordRepository, SapInventorySyncRecordRepository>();
builder.Services.AddHostedService<InventoryMonitoringService>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── PLC + R3 OEE + SignalR 实时管道（T2.12-T2.15）──
// IPlcClient 由 AddRealtimePipeline 内部注册为 OpcUaPlcClient（单例）
builder.Services.AddRealtimePipeline(builder.Configuration);

// ── Cleipnir Saga 注册（Store + Registry 单例；Action 内部创建 Scope）──
builder.Services.AddCleipnirSagas(builder.Configuration);
builder.Services.AddScoped<IProductionOrderSagaRunner, CleipnirProductionOrderSagaRunner>();
builder.Services.AddScoped<ProductionOrderSaga>();

// ── JWT 认证 + 6 角色 RBAC ──
builder.Services.AddMesJwtAuthentication(builder.Configuration);

// ── ZLogger 结构化日志 ──
builder.Logging.AddZLogger();

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

// ── SignalR DashboardHub 端点（MemoryPack 二进制协议，T2.15）──
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();
