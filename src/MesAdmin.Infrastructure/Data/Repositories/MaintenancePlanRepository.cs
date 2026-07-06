using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

public sealed class MaintenancePlanRepository(MesDbContext db) : IMaintenancePlanRepository
{
    public Task<MaintenancePlan?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<MaintenancePlan>> GetActivePlansAsync(CancellationToken ct = default)
        => db.MaintenancePlans.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.EquipmentCode)
            .ToListAsync(ct);

    public Task<List<MaintenancePlan>> GetAllAsync(CancellationToken ct = default)
        => db.MaintenancePlans.AsNoTracking()
            .OrderBy(p => p.EquipmentCode)
            .ToListAsync(ct);

    public Task<List<MaintenancePlan>> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default)
        => db.MaintenancePlans.AsNoTracking()
            .Where(p => p.EquipmentCode == equipmentCode)
            .ToListAsync(ct);

    public async Task AddAsync(MaintenancePlan plan, CancellationToken ct = default)
    {
        await db.MaintenancePlans.AddAsync(plan, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MaintenancePlan plan, CancellationToken ct = default)
    {
        db.MaintenancePlans.Update(plan);
        await db.SaveChangesAsync(ct);
    }
}
