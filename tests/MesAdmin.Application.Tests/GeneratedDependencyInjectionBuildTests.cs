using MesAdmin.Application.DependencyInjection;
using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.DependencyInjection;
using MesAdmin.Infrastructure.Sap;
using MesAdmin.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

public sealed class GeneratedDependencyInjectionBuildTests
{
    [Fact]
    public void GeneratedServices_ShouldBuildWithValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MesDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=automes_di_validation;Username=postgres;Password=postgres"));
        services.AddScoped<ISapOrderSyncRecordRepository, SapOrderSyncRecordRepository>();
        services.AddSingleton<IBomCache, BomCache>();
        services.AddMesGeneratedServices();
        services.AddMesGeneratedInfrastructureServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
