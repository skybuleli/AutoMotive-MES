using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;
using MesAdmin.Infrastructure.RealTime;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 设备维护、预防性维护与备件管理 DI 注册扩展。
/// </summary>
public static class MaintenanceExtensions
{
    public static IServiceCollection AddMesMaintenance(this IServiceCollection services)
    {
        // ── 预防性维护 ──
        services.AddScoped<IMaintenancePlanRepository, MaintenancePlanRepository>();
        services.AddScoped<IMaintenanceWorkOrderRepository, MaintenanceWorkOrderRepository>();
        services.AddHostedService<PreventiveMaintenanceService>();

        // ── 备件管理 ──
        services.AddScoped<ISparePartRepository, SparePartRepository>();
        services.AddScoped<ISparePartUsageRepository, SparePartUsageRepository>();
        services.AddScoped<IPurchaseRequestRepository, PurchaseRequestRepository>();

        // ── 计量器具台账与校准提醒（S01 · IATF 16949）──
        services.AddScoped<GaugeRepository>();
        services.AddScoped<IGaugeRepository>(sp => sp.GetRequiredService<GaugeRepository>());
        services.AddScoped<ICalibrationRecordRepository>(sp => sp.GetRequiredService<GaugeRepository>());
        services.AddSingleton<IFeishuNotifier, FeishuNotifier>();
        services.AddHostedService<GaugeDueReminderService>();

        return services;
    }
}
