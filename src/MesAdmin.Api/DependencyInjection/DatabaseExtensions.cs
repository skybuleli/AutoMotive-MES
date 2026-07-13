using MesAdmin.Application.Features.Inventory;
using MesAdmin.Infrastructure;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 数据库与生成的基础设施服务 DI 注册扩展。
/// </summary>
public static class DatabaseExtensions
{
    public static IServiceCollection AddMesDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MesDbContext>(opt =>
            opt.UseNpgsql(configuration.GetConnectionString("PostgreSQL")));

        services.AddMesGeneratedInfrastructureServices();
        services.AddHostedService<InventoryMonitoringService>();

        return services;
    }
}
