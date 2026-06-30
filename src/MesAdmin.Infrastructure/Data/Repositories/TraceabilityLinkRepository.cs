using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 追溯链接仓储实现（骨架版，委托 MesDbContext）。
/// T1.19 需完善：追溯绑定写入 + 哈希链 + 查询优化。
/// </summary>
public class TraceabilityLinkRepository(MesDbContext db) : ITraceabilityLinkRepository
{
    public async Task<TraceabilityLink?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => await db.TraceabilityLinks.FindAsync([id], ct);

    public async Task<List<TraceabilityLink>> GetByVinOrSerialAsync(string vinOrSerial, CancellationToken ct = default)
        => await db.TraceabilityLinks
            .Where(l => l.VinOrSerial == vinOrSerial)
            .ToListAsync(ct);

    public async Task<List<TraceabilityLink>> GetByComponentBatchAsync(string batch, CancellationToken ct = default)
        => await db.TraceabilityLinks
            .Where(l => l.ComponentBatch == batch)
            .ToListAsync(ct);

    public async Task<List<TraceabilityLink>> GetByMaterialBatchAsync(string batch, CancellationToken ct = default)
        => await db.TraceabilityLinks
            .Where(l => l.MaterialBatch == batch)
            .ToListAsync(ct);

    public async Task AddAsync(TraceabilityLink link, CancellationToken ct = default)
        => await db.TraceabilityLinks.AddAsync(link, ct);
}
