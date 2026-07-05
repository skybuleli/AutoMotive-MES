using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 物料库存阈值设置仓储实现。
/// </summary>
public class MaterialInventorySettingRepository(MesDbContext db) : IMaterialInventorySettingRepository
{
    public Task<MaterialInventorySetting?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialInventorySettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<MaterialInventorySetting?> GetByMaterialCodeAsync(string materialCode, CancellationToken ct = default)
        => db.MaterialInventorySettings
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(s => s.MaterialCode == materialCode, ct);

    public Task<List<MaterialInventorySetting>> GetAllEnabledAsync(CancellationToken ct = default)
        => db.MaterialInventorySettings
            .AsNoTracking()
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.MaterialCode)
            .ToListAsync(ct);

    public Task<List<MaterialInventorySetting>> GetByStationAsync(string stationId, CancellationToken ct = default)
        => db.MaterialInventorySettings
            .AsNoTracking()
            .Where(s => s.StationId == stationId && s.IsEnabled)
            .OrderBy(s => s.MaterialCode)
            .ToListAsync(ct);

    public Task AddAsync(MaterialInventorySetting setting, CancellationToken ct = default)
        => db.MaterialInventorySettings.AddAsync(setting, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

/// <summary>
/// 库存预警记录仓储实现。
/// </summary>
public class InventoryAlertRepository(MesDbContext db) : IInventoryAlertRepository
{
    public Task<InventoryAlert?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.InventoryAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<InventoryAlert>> GetActiveAsync(CancellationToken ct = default)
        => db.InventoryAlerts
            .AsNoTracking()
            .Where(a => !a.IsResolved)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<List<InventoryAlert>> GetByMaterialCodeAsync(string materialCode, CancellationToken ct = default)
        => db.InventoryAlerts
            .AsNoTracking()
            .Where(a => a.MaterialCode == materialCode)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public Task<InventoryAlert?> GetLatestByMaterialAsync(string materialCode, CancellationToken ct = default)
        => db.InventoryAlerts
            .AsNoTracking()
            .Where(a => a.MaterialCode == materialCode)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(InventoryAlert alert, CancellationToken ct = default)
        => db.InventoryAlerts.AddAsync(alert, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public Task<int> CountActiveAsync(CancellationToken ct = default)
        => db.InventoryAlerts
            .CountAsync(a => !a.IsResolved, ct);

    public Task<int> CountByLevelAsync(InventoryAlertLevel level, CancellationToken ct = default)
        => db.InventoryAlerts
            .CountAsync(a => a.AlertLevel == level && !a.IsResolved, ct);
}
