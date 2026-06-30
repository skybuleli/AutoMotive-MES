using MesAdmin.Web.Components;
using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Sagas;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Logging;
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

// ── 业务服务（Saga 编排，仓储实现待 T1.2 完善）──
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
