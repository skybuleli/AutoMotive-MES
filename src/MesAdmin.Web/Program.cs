using MesAdmin.Web.Components;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Sagas;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Logging;
using MesAdmin.Infrastructure.Plc;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── MudBlazor UI ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// ── 数据库（PostgreSQL 17 + EF Core + Ulid 主键）──
builder.Services.AddDbContext<MesDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

// ── 仓储（骨架版，委托 MesDbContext；T1.2 需完善查询优化）──
builder.Services.AddScoped<IProductionOrderRepository, ProductionOrderRepository>();
builder.Services.AddScoped<ITraceabilityLinkRepository, TraceabilityLinkRepository>();

// ── PLC 客户端（骨架版；T2.12 需替换为 OpcUaPlcClient 真实驱动）──
builder.Services.AddScoped<IPlcClient, StubPlcClient>();

// ── 业务服务（Saga 编排）──
builder.Services.AddScoped<ProductionOrderSaga>();

// ── ZLogger 结构化日志（零分配，IBufferWriter 直写，禁止字符串拼接）──
builder.Logging.AddZLogger();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
