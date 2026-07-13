using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Caching;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// BOM 内存缓存 DI 注册扩展。
/// </summary>
public static class BomCacheExtensions
{
    public static IServiceCollection AddMesBomCache(this IServiceCollection services)
    {
        services.AddSingleton<IBomCache, BomCache>();
        services.AddHostedService<BomCacheInitializationService>();

        return services;
    }
}
