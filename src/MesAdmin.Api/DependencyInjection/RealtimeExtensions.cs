using MesAdmin.Infrastructure;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// PLC + R3 OEE + SignalR 实时管道 DI 注册扩展。
/// </summary>
public static class RealtimeExtensions
{
    public static IServiceCollection AddMesRealtimePipeline(this IServiceCollection services, IConfiguration configuration)
    {
        // IPlcClient 由 AddRealtimePipeline 内部注册为 OpcUaPlcClient（单例）
        services.AddRealtimePipeline(configuration);
        return services;
    }
}
