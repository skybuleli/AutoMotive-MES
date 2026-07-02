using MesAdmin.Web.Components;
using MesAdmin.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using MesAdmin.Infrastructure.Logging;
using MesAdmin.Infrastructure.Security;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── MudBlazor UI ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// ── JWT 认证 + HttpClient（Web 调用 API 获取 Token，本地验证）──
builder.Services.AddMesJwtAuthentication(builder.Configuration);

// ── Blazor Server 浏览器存储 ──
builder.Services.AddScoped<ProtectedLocalStorage>();

// ── API 客户端（所有页面通过 MesApiClient 调用后端 API，不再直接注入 Application 服务）──
builder.Services.AddScoped<MesApiClient>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, MesAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddHttpClient("MesApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5040/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ── ZLogger 结构化日志 ──
builder.Logging.AddZLogger();

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
