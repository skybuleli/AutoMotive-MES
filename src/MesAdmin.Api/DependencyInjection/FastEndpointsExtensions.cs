using FastEndpoints;
using FastEndpoints.Swagger;
using MesAdmin.Application.Behaviors;
using MesAdmin.Application.DependencyInjection;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// FastEndpoints（REPR 模式 + 命令/事件总线）DI 注册扩展。
/// </summary>
public static class FastEndpointsExtensions
{
    public static IServiceCollection AddMesFastEndpoints(this IServiceCollection services)
    {
        services.AddFastEndpoints();
        services.AddMessaging();   // 启用命令总线 + 事件总线（handler 自动发现）
        services.AddMesGeneratedServices();
        services.AddCommandMiddleware(c =>
        {
            c.Register(typeof(LoggingCommandMiddleware<,>));     // 最外层：日志
            c.Register(typeof(TransactionMiddleware<,>));          // 内层：事务
        });
        services.SwaggerDocument();

        return services;
    }
}
