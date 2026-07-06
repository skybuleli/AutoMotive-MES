using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 预防性维护工单仓储接口（T2.17）。
/// </summary>
public interface IMaintenanceWorkOrderRepository
{
    Task<MaintenanceWorkOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default);

    /// <summary>查询工单列表（支持按设备/状态过滤）</summary>
    Task<List<MaintenanceWorkOrder>> GetListAsync(
        string? equipmentCode = null,
        MaintenanceOrderStatus? status = null,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>获取某计划的所有未关闭工单</summary>
    Task<List<MaintenanceWorkOrder>> GetOpenByPlanAsync(Ulid planId, CancellationToken ct = default);

    Task AddAsync(MaintenanceWorkOrder order, CancellationToken ct = default);
    Task UpdateAsync(MaintenanceWorkOrder order, CancellationToken ct = default);
}
