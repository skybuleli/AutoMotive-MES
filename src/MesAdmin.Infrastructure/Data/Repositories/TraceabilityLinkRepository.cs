using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 追溯链接仓储实现（委托同一 Scoped MesDbContext）。
/// 支持哈希链写入（GetLastLinkAsync 取前驱哈希）+ 4 级正反向查询。
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
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

    public Task<List<TraceabilityLink>> GetByComponentBatchAsync(string batch, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.ComponentBatch == batch)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

    public Task<List<TraceabilityLink>> GetByMaterialBatchAsync(string batch, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.MaterialBatch == batch)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

    public Task<List<TraceabilityLink>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

    /// <summary>获取哈希链最后一条记录（按创建时间+Id 排序）</summary>
    public Task<TraceabilityLink?> GetLastLinkAsync(CancellationToken ct = default)
        => db.TraceabilityLinks
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(TraceabilityLink link, CancellationToken ct = default)
        => db.TraceabilityLinks.AddAsync(link, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
