using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.RealTime;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 质量管理与 100% 在线液压测试 DI 注册扩展。
/// </summary>
public static class QualityExtensions
{
    public static IServiceCollection AddMesQuality(this IServiceCollection services)
    {
        // ── 100% 在线液压测试管道 ──
        services.AddScoped<IHydraulicTestRepository, HydraulicTestRepository>();
        services.AddHostedService<HydraulicTestReactivePipeline>();

        return services;
    }
}
