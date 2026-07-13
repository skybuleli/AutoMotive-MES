using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 供应商质量（SQE）管理 DI 注册扩展。
/// </summary>
public static class SupplierQualityExtensions
{
    public static IServiceCollection AddMesSupplierQuality(this IServiceCollection services)
    {
        // ── SQE 供应商质量模块 ──
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ISupplierScoreCardRepository, SupplierScoreCardRepository>();
        services.AddScoped<IPpapDocumentRepository, PpapDocumentRepository>();
        services.AddScoped<ICriticalSupplierSettingRepository, CriticalSupplierSettingRepository>();

        return services;
    }
}
