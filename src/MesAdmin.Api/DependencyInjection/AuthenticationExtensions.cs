using MesAdmin.Infrastructure.Security;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// JWT 认证与授权 DI 注册扩展。
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddMesAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMesJwtAuthentication(configuration);
        return services;
    }
}
