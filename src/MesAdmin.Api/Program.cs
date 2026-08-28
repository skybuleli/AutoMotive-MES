using MesAdmin.Api.DependencyInjection;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Infrastructure;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
RuntimeSafetyGuards.ValidateNoSimulationInProduction(builder.Configuration, builder.Environment.EnvironmentName);

// ── 端口冲突自动避让（仅非生产环境；生产环境端口必须稳定） ──
if (!builder.Environment.IsProduction())
{
    var configuredUrls = builder.Configuration[WebHostDefaults.ServerUrlsKey];
    if (!string.IsNullOrWhiteSpace(configuredUrls))
    {
        var (resolvedUrls, changed) = PortFallback.Resolve(configuredUrls);
        if (changed)
        {
            builder.WebHost.UseUrls(resolvedUrls);
        }
    }
}

builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
    options.ValidateOnBuild = context.HostingEnvironment.IsDevelopment();
});

// ── FastEndpoints（REPR 模式 + 命令/事件总线） ──
builder.Services.AddMesFastEndpoints();

// ── 数据库 + 生成的基础设施服务 ──
builder.Services.AddMesDatabase(builder.Configuration);

// ── PLC + R3 OEE + SignalR 实时管道 ──
builder.Services.AddMesRealtimePipeline(builder.Configuration);

// ── Cleipnir Saga 注册（Store + Registry 单例；Action 内部创建 Scope） ──
builder.Services.AddMesSagas(builder.Configuration);

// ── JWT 认证 + 6 角色 RBAC ──
builder.Services.AddMesAuthentication(builder.Configuration);

// ── 报表引擎与邮件推送 ──
builder.Services.AddMesReporting(builder.Configuration);

// ── 终端离线缓存与断网重连同步 ──
builder.Services.AddMesOfflineSync();

// ── 质量管理 + 100% 在线液压测试 ──
builder.Services.AddMesQuality();

// ── 设备维护、预防性维护与备件管理 ──
builder.Services.AddMesMaintenance();

// ── 受控文档中心（S03 · IATF 16949）──
builder.Services.AddMesDocuments();

// ── 工艺路线管理与防错三重校验 ──
builder.Services.AddMesRouting();

// ── 生产排程管理 ──
builder.Services.AddMesScheduling();

// ── SQE 供应商质量模块 ──
builder.Services.AddMesSupplierQuality();

// ── BOM 内存缓存 ──
builder.Services.AddMesBomCache();

// ── SAP 集成 ──
builder.Services.AddMesSapIntegration(builder.Configuration);

// ── 可观察性（日志 + 指标） ──
builder.Services.AddMesObservability(builder.Configuration);

var app = builder.Build();

// ── 启动时自动应用 EF Core Migration + 种子数据（幂等）──
await app.UseMesMigrationsAndSeedAsync();

// ── 健康检查端点（所有环境，用于 Docker HEALTHCHECK）──
app.UseMesHealthChecks();

// ── 中间件管道（认证、授权、FastEndpoints、Swagger）──
app.UseMesMiddlewarePipeline();

// ── SignalR 端点（MemoryPack 二进制协议）──
app.UseMesSignalRHubs();

app.Run();
