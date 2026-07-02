using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 追溯链接仓储实现（委托同一 Scoped MesDbContext）。
/// T1.19 需完善：追溯绑定写入 + 哈希链 + 查询优化。
/// </summary>
public class TraceabilityLinkRepository(MesDbContext db) : ITraceabilityLinkRepository
{
    public Task<TraceabilityLink?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(link => link.Id == id, ct);

    public Task<List<TraceabilityLink>> GetByVinOrSerialAsync(string vinOrSerial, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.VinOrSerial == vinOrSerial)
            .ToListAsync(ct);

    public Task<List<TraceabilityLink>> GetByComponentBatchAsync(string batch, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.ComponentBatch == batch)
            .ToListAsync(ct);

    public Task<List<TraceabilityLink>> GetByMaterialBatchAsync(string batch, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.MaterialBatch == batch)
            .ToListAsync(ct);

    public Task AddAsync(TraceabilityLink link, CancellationToken ct = default)
        => db.TraceabilityLinks.AddAsync(link, ct).AsTask();
}
