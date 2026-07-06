using MesAdmin.Web.Components;
using MesAdmin.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// Web 端复用 AddMesJwtAuthentication，但覆盖 OnChallenge：
// 未认证访问受保护页面时重定向到 /login，而非返回 401 空白页。
builder.Services.AddMesJwtAuthentication(builder.Configuration);
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            // 只对浏览器页面导航重定向；API 调用（带 Accept: application/json）仍返回 401
            var acceptHeader = context.Request.Headers.Accept.ToString();
            if (!context.Response.HasStarted && !acceptHeader.Contains("application/json"))
            {
                var returnUrl = context.Request.Path + context.Request.QueryString;
                context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
                context.HandleResponse();
            }
            return Task.CompletedTask;
        }
    };
});

// ── Blazor Server 浏览器存储 ──
builder.Services.AddScoped<ProtectedLocalStorage>();

// ── API 客户端（所有页面通过 MesApiClient 调用后端 API，不再直接注入 Application 服务）──
builder.Services.AddScoped<MesApiClient>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, MesAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// ── OEE SignalR Hub 客户端（T2.19，连接 Api 的 /hubs/dashboard）──
builder.Services.AddSingleton<OeeHubClient>();

// ── Andon SignalR Hub 客户端（T2.22，连接 Api 的 /hubs/andon）──
builder.Services.AddSingleton<AndonHubClient>();

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
