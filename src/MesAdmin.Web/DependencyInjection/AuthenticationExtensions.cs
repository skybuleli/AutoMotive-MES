using MesAdmin.Infrastructure.Security;
using MesAdmin.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;

namespace MesAdmin.Web.DependencyInjection;

/// <summary>
/// Web 端认证、授权与 Blazor 状态管理 DI 注册扩展。
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddMesWebAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Web 端复用 AddMesJwtAuthentication，但覆盖 OnChallenge：
        // 未认证访问受保护页面时重定向到 /login，而非返回 401 空白页。
        services.AddMesJwtAuthentication(configuration);
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
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

        services.AddScoped<AuthService>();
        services.AddScoped<AuthenticationStateProvider, MesAuthenticationStateProvider>();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
