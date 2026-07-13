using MesAdmin.Application.Features.Scheduling;
using MesAdmin.Application.Interfaces;
using MesAdmin.Infrastructure.Data.Repositories;

namespace MesAdmin.Api.DependencyInjection;

/// <summary>
/// 生产排程管理 DI 注册扩展。
/// </summary>
public static class SchedulingExtensions
{
    public static IServiceCollection AddMesScheduling(this IServiceCollection services)
    {
        // ── 生产排程管理 ──
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<ICapacityCalendarRepository, CapacityCalendarRepository>();
        services.AddScoped<SchedulingEngine>();

        return services;
    }
}
