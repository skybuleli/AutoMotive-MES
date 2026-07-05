using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

public sealed class GeneratedInfrastructureServiceRegistrationTests
{
    [Fact]
    public void AddMesGeneratedInfrastructureServices_ShouldRegisterRepositoriesAndUnitOfWork()
    {
        var services = new ServiceCollection();

        services.AddMesGeneratedInfrastructureServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IProductionOrderRepository) &&
            descriptor.ImplementationType == typeof(ProductionOrderRepository) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IUnitOfWork) &&
            descriptor.ImplementationType == typeof(UnitOfWork) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
