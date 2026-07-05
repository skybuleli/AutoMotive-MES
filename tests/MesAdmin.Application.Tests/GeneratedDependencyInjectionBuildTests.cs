using MesAdmin.Application.DependencyInjection;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.DependencyInjection;
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
        services.AddMesGeneratedServices();
        services.AddMesGeneratedInfrastructureServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
