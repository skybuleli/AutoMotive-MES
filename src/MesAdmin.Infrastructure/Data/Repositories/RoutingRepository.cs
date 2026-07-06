using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

public sealed class RoutingRepository(MesDbContext db) : IRoutingRepository
{
    public Task<Routing?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.Set<Routing>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<Routing?> GetActiveByProductAsync(string productCode, CancellationToken ct = default)
        => db.Set<Routing>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductCode == productCode && r.IsActive, ct);

    public Task<List<Routing>> GetByProductAsync(string productCode, CancellationToken ct = default)
        => db.Set<Routing>().AsNoTracking()
            .Where(r => r.ProductCode == productCode)
            .OrderByDescending(r => r.Version)
            .ToListAsync(ct);

    public Task<List<Routing>> GetAllAsync(CancellationToken ct = default)
        => db.Set<Routing>().AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<List<Routing>> GetByEcoStatusAsync(EcoStatus status, CancellationToken ct = default)
        => db.Set<Routing>().AsNoTracking()
            .Where(r => r.EcoStatus == status)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Routing routing, CancellationToken ct = default)
    {
        await db.Set<Routing>().AddAsync(routing, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Routing routing, CancellationToken ct = default)
    {
        db.Set<Routing>().Update(routing);
        await db.SaveChangesAsync(ct);
    }
}
