using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

public class FirstArticleInspectionRepository(MesDbContext db) : IFirstArticleInspectionRepository
{
    public Task<FirstArticleInspection?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.FirstArticleInspections
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<List<FirstArticleInspection>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.FirstArticleInspections
            .AsNoTracking()
            .Where(f => f.OrderId == orderId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(FirstArticleInspection inspection, CancellationToken ct = default)
        => db.FirstArticleInspections.AddAsync(inspection, ct).AsTask();

    public void Update(FirstArticleInspection inspection)
        => db.FirstArticleInspections.Update(inspection);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
