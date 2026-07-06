using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

public sealed class MaintenanceWorkOrderRepository(MesDbContext db) : IMaintenanceWorkOrderRepository
{
    public Task<MaintenanceWorkOrder?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaintenanceWorkOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<List<MaintenanceWorkOrder>> GetListAsync(
        string? equipmentCode = null,
        MaintenanceOrderStatus? status = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = db.MaintenanceWorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(equipmentCode))
            query = query.Where(o => o.EquipmentCode == equipmentCode);
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        return query.OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<List<MaintenanceWorkOrder>> GetOpenByPlanAsync(Ulid planId, CancellationToken ct = default)
        => db.MaintenanceWorkOrders.AsNoTracking()
            .Where(o => o.MaintenancePlanId == planId
                && (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(MaintenanceWorkOrder order, CancellationToken ct = default)
    {
        await db.MaintenanceWorkOrders.AddAsync(order, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MaintenanceWorkOrder order, CancellationToken ct = default)
    {
        db.MaintenanceWorkOrders.Update(order);
        await db.SaveChangesAsync(ct);
    }
}
