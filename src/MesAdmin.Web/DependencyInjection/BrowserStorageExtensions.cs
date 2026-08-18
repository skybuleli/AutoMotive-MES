using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MesAdmin.Web.DependencyInjection;

/// <summary>
/// Blazor Server 浏览器存储 DI 注册扩展。
/// </summary>
public static class BrowserStorageExtensions
{
    public static IServiceCollection AddMesBrowserStorage(this IServiceCollection services)
    {
        services.AddScoped<ProtectedLocalStorage>();
        return services;
    }
}
