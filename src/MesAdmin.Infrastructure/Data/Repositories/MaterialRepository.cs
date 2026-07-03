using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 物料批次仓储实现（委托同一 Scoped MesDbContext）。
/// </summary>
public class MaterialBatchRepository(MesDbContext db) : IMaterialBatchRepository
{
    public Task<MaterialBatch?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<MaterialBatch?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBatches
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<MaterialBatch?> GetByBatchNumberAsync(string batchNumber, CancellationToken ct = default)
        => db.MaterialBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchNumber == batchNumber, ct);

    public Task<List<MaterialBatch>> GetPageAsync(string? materialCode, int skip, int take, CancellationToken ct = default)
    {
        var query = db.MaterialBatches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(b => b.MaterialCode == materialCode);
        return query
            .OrderByDescending(b => b.ReceivedAt)
            .ThenByDescending(b => b.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(string? materialCode, CancellationToken ct = default)
    {
        var query = db.MaterialBatches.AsQueryable();
        if (!string.IsNullOrWhiteSpace(materialCode))
            query = query.Where(b => b.MaterialCode == materialCode);
        return query.CountAsync(ct);
    }

    public Task AddAsync(MaterialBatch batch, CancellationToken ct = default)
        => db.MaterialBatches.AddAsync(batch, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

/// <summary>
/// 物料投料绑定仓储实现。
/// </summary>
public class MaterialBindingRepository(MesDbContext db) : IMaterialBindingRepository
{
    public Task<MaterialBinding?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<List<MaterialBinding>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .Where(b => b.OrderId == orderId)
            .OrderBy(b => b.BoundAt)
            .ToListAsync(ct);

    public Task<List<MaterialBinding>> GetByProductSerialAsync(string productSerial, CancellationToken ct = default)
        => db.MaterialBindings
            .AsNoTracking()
            .Where(b => b.ProductSerial == productSerial)
            .OrderBy(b => b.BoundAt)
            .ToListAsync(ct);

    public Task AddAsync(MaterialBinding binding, CancellationToken ct = default)
        => db.MaterialBindings.AddAsync(binding, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
