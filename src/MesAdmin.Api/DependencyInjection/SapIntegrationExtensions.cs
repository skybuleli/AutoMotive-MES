using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.Sap;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// SAP 集成（工单/库存/物料移动/拒单回写）DI 注册扩展。
/// </summary>
public static class SapIntegrationExtensions
{
    public static IServiceCollection AddMesSapIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        // ── SAP 工单同步记录仓储 ──
        services.AddScoped<ISapOrderSyncRecordRepository, SapOrderSyncRecordRepository>();

        // ── SAP 客户端（真实 / Mock）──
        services.AddSingleton<ISapClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var useRealSap = config.GetValue<bool>("Sap:UseRealClient", false);
            if (useRealSap)
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<HttpSapClient>>();
                return new HttpSapClient(factory, config, logger);
            }
            var mockLogger = sp.GetRequiredService<ILogger<MockSapClient>>();
            return new MockSapClient(mockLogger);
        });

        // 拒单回写后台服务（Poll pending rejections → writeback SAP）
        services.AddHostedService<SapRejectionWritebackService>();
        // 工单状态同步后台服务
        services.AddHostedService<SapOrderSyncService>();
        // 库存同步后台服务
        services.AddHostedService<SapInventorySyncService>();
        // 物料移动同步后台服务
        services.AddHostedService<SapMaterialMovementSyncService>();

        return services;
    }
}
