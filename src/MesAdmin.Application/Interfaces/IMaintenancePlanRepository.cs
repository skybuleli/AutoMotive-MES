using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 预防性维护计划仓储接口（T2.17）。
/// </summary>
public interface IMaintenancePlanRepository
{
    Task<MaintenancePlan?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>获取所有启用的维护计划</summary>
    Task<List<MaintenancePlan>> GetActivePlansAsync(CancellationToken ct = default);

    /// <summary>获取全部维护计划（含已停用）</summary>
    Task<List<MaintenancePlan>> GetAllAsync(CancellationToken ct = default);

    /// <summary>按设备编码查询</summary>
    Task<List<MaintenancePlan>> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default);

    Task AddAsync(MaintenancePlan plan, CancellationToken ct = default);
    Task UpdateAsync(MaintenancePlan plan, CancellationToken ct = default);
}
