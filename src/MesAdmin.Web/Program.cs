using MesAdmin.Web.Components;
using MesAdmin.Web.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ── MudBlazor UI ──
builder.Services.AddMesWebUi();

// ── JWT 认证 + Blazor 状态管理 ──
builder.Services.AddMesWebAuthentication(builder.Configuration);

// ── Blazor Server 浏览器存储 ──
builder.Services.AddMesBrowserStorage();

// ── API 客户端 + SignalR Hub 客户端 ──
builder.Services.AddMesWebApiClients(builder.Configuration);

// ── ZLogger 结构化日志 ──
builder.Logging.AddMesWebLogging();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
