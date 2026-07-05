using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

public class MaterialConsumptionRepository(MesDbContext db) : IMaterialConsumptionRepository
{
    public Task AddAsync(MaterialConsumption consumption, CancellationToken ct = default)
        => db.MaterialConsumptions.AddAsync(consumption, ct).AsTask();

    public Task AddRangeAsync(IEnumerable<MaterialConsumption> consumptions, CancellationToken ct = default)
        => db.MaterialConsumptions.AddRangeAsync(consumptions, ct);

    public Task<List<MaterialConsumption>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.MaterialConsumptions
            .AsNoTracking()
            .Where(c => c.OrderId == orderId)
            .OrderBy(c => c.MaterialCode)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

public class ConsumptionVarianceRepository(MesDbContext db) : IConsumptionVarianceRepository
{
    public Task AddAsync(ConsumptionVarianceReport report, CancellationToken ct = default)
        => db.ConsumptionVarianceReports.AddAsync(report, ct).AsTask();

    public Task<List<ConsumptionVarianceReport>> GetUnresolvedAsync(CancellationToken ct = default)
        => db.ConsumptionVarianceReports
            .AsNoTracking()
            .Where(r => !r.IsResolved)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

public class SapInventorySyncRecordRepository(MesDbContext db) : ISapInventorySyncRecordRepository
{
    public Task AddAsync(SapInventorySyncRecord record, CancellationToken ct = default)
        => db.SapInventorySyncRecords.AddAsync(record, ct).AsTask();

    public Task AddRangeAsync(IEnumerable<SapInventorySyncRecord> records, CancellationToken ct = default)
        => db.SapInventorySyncRecords.AddRangeAsync(records, ct);

    public Task<List<SapInventorySyncRecord>> GetPendingSyncAsync(CancellationToken ct = default)
        => db.SapInventorySyncRecords
            .AsNoTracking()
            .Where(r => !r.SapSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
