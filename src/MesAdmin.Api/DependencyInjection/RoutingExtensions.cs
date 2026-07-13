using MesAdmin.Application.Features.Routing;
using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 工艺路线管理与防错三重校验 DI 注册扩展。
/// </summary>
public static class RoutingExtensions
{
    public static IServiceCollection AddMesRouting(this IServiceCollection services)
    {
        // ── 工艺路线管理 ──
        services.AddScoped<IRoutingRepository, RoutingRepository>();

        // ── 防错三重校验 ──
        services.AddScoped<TripleCheckService>();

        return services;
    }
}
