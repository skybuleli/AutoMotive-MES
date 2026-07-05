using FastEndpoints;
using MesAdmin.Application.DependencyInjection;
using MesAdmin.Application.Features.ProductionOrders;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

public sealed class GeneratedServiceRegistrationTests
{
    [Fact]
    public void AddMesGeneratedServices_ShouldRegisterCommandHandlers()
    {
        var services = new ServiceCollection();

        services.AddMesGeneratedServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<BackflushMaterialsCommand, BackflushResult>) &&
            descriptor.ImplementationType == typeof(BackflushMaterialsHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(BackflushMaterialsHandler) &&
            descriptor.ImplementationType == typeof(BackflushMaterialsHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
