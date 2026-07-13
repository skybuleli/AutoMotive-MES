using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Sync;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 终端离线缓存与断网重连同步 DI 注册扩展。
/// </summary>
public static class OfflineSyncExtensions
{
    public static IServiceCollection AddMesOfflineSync(this IServiceCollection services)
    {
        // ── 离线缓存同步 ──
        services.AddScoped<IOfflineSyncRepository, OfflineSyncRepository>();
        services.AddSingleton<OfflineSyncService>();
        services.AddHostedService<OfflineCacheBackgroundService>();

        // ── 断网重连自动同步 ──
        services.AddSingleton<SagaReconciliationService>();
        services.AddSingleton<OfflineReplayService>();
        services.AddHostedService<ReconnectionBackgroundService>();

        return services;
    }
}
