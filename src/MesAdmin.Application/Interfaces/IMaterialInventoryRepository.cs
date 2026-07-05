using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 物料库存阈值设置仓储接口（T1.13 线边库存实时监控）。
/// 管理每物料的安群库存和最低库存阈值。
/// </summary>
public interface IMaterialInventorySettingRepository
{
    Task<MaterialInventorySetting?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<MaterialInventorySetting?> GetByMaterialCodeAsync(string materialCode, CancellationToken cancellationToken = default);
    Task<List<MaterialInventorySetting>> GetAllEnabledAsync(CancellationToken cancellationToken = default);
    Task<List<MaterialInventorySetting>> GetByStationAsync(string stationId, CancellationToken cancellationToken = default);
    Task AddAsync(MaterialInventorySetting setting, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 库存预警记录仓储接口（T1.13）。
/// </summary>
public interface IInventoryAlertRepository
{
    Task<InventoryAlert?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<List<InventoryAlert>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<List<InventoryAlert>> GetByMaterialCodeAsync(string materialCode, CancellationToken cancellationToken = default);
    Task<InventoryAlert?> GetLatestByMaterialAsync(string materialCode, string? stationId, CancellationToken cancellationToken = default);
    Task<InventoryAlert?> GetLatestByMaterialTrackedAsync(string materialCode, string? stationId, CancellationToken cancellationToken = default);
    Task AddAsync(InventoryAlert alert, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);
    Task<int> CountByLevelAsync(InventoryAlertLevel level, CancellationToken cancellationToken = default);
}
