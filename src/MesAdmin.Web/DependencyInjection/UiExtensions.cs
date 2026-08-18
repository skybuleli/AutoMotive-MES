using MesAdmin.Web.Services;
using MudBlazor;
using MudBlazor.Services;

namespace MesAdmin.Web.DependencyInjection;

/// <summary>
/// MudBlazor UI 与 Razor Components DI 注册扩展。
/// </summary>
public static class UiExtensions
{
    public static IServiceCollection AddMesWebUi(this IServiceCollection services)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddMudServices();
        services.AddScoped<ThemeService>();

        return services;
    }
}
