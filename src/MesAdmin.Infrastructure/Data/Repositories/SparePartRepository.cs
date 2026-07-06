using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

public sealed class SparePartRepository(MesDbContext db) : ISparePartRepository
{
    public Task<SparePart?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Set<SparePart>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<SparePart>> GetAllAsync(CancellationToken ct = default)
        => db.Set<SparePart>().AsNoTracking()
            .OrderBy(p => p.MaterialCode)
            .ToListAsync(ct);

    public Task<List<SparePart>> GetByEquipmentAsync(string equipmentCode, CancellationToken ct = default)
        => db.Set<SparePart>().AsNoTracking()
            .Where(p => p.EquipmentCode == equipmentCode || p.EquipmentCode == null)
            .OrderBy(p => p.MaterialCode)
            .ToListAsync(ct);

    public Task<List<SparePart>> GetLowStockAsync(CancellationToken ct = default)
        => db.Set<SparePart>().AsNoTracking()
            .Where(p => p.CurrentQuantity < p.SafetyStock)
            .OrderBy(p => p.CurrentQuantity)
            .ToListAsync(ct);

    public Task<List<SparePart>> GetNeedsRestockAsync(CancellationToken ct = default)
        => db.Set<SparePart>().AsNoTracking()
            .Where(p => p.CurrentQuantity < p.MinimumStock)
            .OrderBy(p => p.CurrentQuantity)
            .ToListAsync(ct);

    public async Task AddAsync(SparePart sparePart, CancellationToken ct = default)
    {
        await db.Set<SparePart>().AddAsync(sparePart, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SparePart sparePart, CancellationToken ct = default)
    {
        db.Set<SparePart>().Update(sparePart);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SparePartUsageRepository(MesDbContext db) : ISparePartUsageRepository
{
    public Task<List<SparePartUsage>> GetByWorkOrderAsync(Ulid workOrderId, CancellationToken ct = default)
        => db.Set<SparePartUsage>().AsNoTracking()
            .Where(u => u.MaintenanceWorkOrderId == workOrderId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

    public Task<List<SparePartUsage>> GetBySparePartAsync(Ulid sparePartId, CancellationToken ct = default)
        => db.Set<SparePartUsage>().AsNoTracking()
            .Where(u => u.SparePartId == sparePartId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SparePartUsage usage, CancellationToken ct = default)
    {
        await db.Set<SparePartUsage>().AddAsync(usage, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class PurchaseRequestRepository(MesDbContext db) : IPurchaseRequestRepository
{
    public Task<PurchaseRequest?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Set<PurchaseRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<PurchaseRequest>> GetListAsync(string? status = null, int limit = 50, CancellationToken ct = default)
    {
        var query = db.Set<PurchaseRequest>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);
        return query.OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<List<PurchaseRequest>> GetPendingBySparePartAsync(Ulid sparePartId, CancellationToken ct = default)
        => db.Set<PurchaseRequest>().AsNoTracking()
            .Where(r => r.SparePartId == sparePartId && r.Status == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(PurchaseRequest request, CancellationToken ct = default)
    {
        await db.Set<PurchaseRequest>().AddAsync(request, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PurchaseRequest request, CancellationToken ct = default)
    {
        db.Set<PurchaseRequest>().Update(request);
        await db.SaveChangesAsync(ct);
    }
}
