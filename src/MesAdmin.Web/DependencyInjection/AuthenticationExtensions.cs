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
    /// <summary>登录成功后种的 JWT cookie，供服务端整页导航鉴权兜底（见 OnMessageReceived）。</summary>
    public const string CookieName = "mes_auth_cookie";

    public static IServiceCollection AddMesWebAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Web 端复用 AddMesJwtAuthentication，但覆盖 OnChallenge：
        // 未认证访问受保护页面时重定向到 /login，而非返回 401 空白页。
        services.AddMesJwtAuthentication(configuration);
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // 登录成功后 Web 端会种下 httpOnly JWT cookie（mes_auth_cookie）；
                    // 页面整页加载（登录后 NavigateTo 的浏览器导航）时没有 Authorization 头，
                    // 从 cookie 兜底取 token，否则服务端无法通过 [Authorize] 校验而回跳登录页。
                    if (string.IsNullOrEmpty(context.Token))
                        context.Token = context.Request.Cookies[CookieName];
                    return Task.CompletedTask;
                },
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
