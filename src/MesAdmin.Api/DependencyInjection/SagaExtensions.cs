using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Sagas;
using MesAdmin.Infrastructure.Workflows;
using Microsoft.Extensions.Options;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// Cleipnir Saga 工作流 DI 注册扩展。
/// </summary>
public static class SagaExtensions
{
    public static IServiceCollection AddMesSagas(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCleipnirSagas(configuration);
        services.AddScoped<IProductionOrderSagaRunner, CleipnirProductionOrderSagaRunner>();
        services.AddScoped<ProductionOrderSaga>();

        services.Configure<ProductionOrderSagaOptions>(configuration.GetSection("Saga:ProductionOrder"));
        services.AddSingleton<IValidateOptions<ProductionOrderSagaOptions>, ProductionOrderSagaOptionsValidator>();
        services.AddOptions<ProductionOrderSagaOptions>().ValidateOnStart();

        return services;
    }
}
