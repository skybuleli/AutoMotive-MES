using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// SAP 拒单记录仓储实现（委托同一 Scoped MesDbContext）。
/// </summary>
public class SapRejectionRepository(MesDbContext db) : ISapRejectionRepository
{
    public Task<SapRejectionRecord?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.SapRejectionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<SapRejectionRecord>> GetPendingWritebackAsync(CancellationToken ct = default)
        => db.SapRejectionRecords
            .AsNoTracking()
            .Where(r => r.WritebackStatus == RejectionWritebackStatus.Pending)
            .OrderBy(r => r.RejectedAt)
            .ToListAsync(ct);

    public Task<List<SapRejectionRecord>> GetByExternalOrderNumberAsync(string externalOrderNumber, CancellationToken ct = default)
        => db.SapRejectionRecords
            .AsNoTracking()
            .Where(r => r.ExternalOrderNumber == externalOrderNumber)
            .OrderByDescending(r => r.RejectedAt)
            .ToListAsync(ct);

    public Task AddAsync(SapRejectionRecord record, CancellationToken ct = default)
        => db.SapRejectionRecords.AddAsync(record, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

/// <summary>
/// 成品入库单仓储实现（委托同一 Scoped MesDbContext）。
/// </summary>
public class GoodsReceiptRepository(MesDbContext db) : IGoodsReceiptRepository
{
    public Task<GoodsReceipt?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.GoodsReceipts
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<GoodsReceipt?> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.GoodsReceipts
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.OrderId == orderId, ct);

    public Task<List<GoodsReceipt>> GetPageAsync(int skip, int take, CancellationToken ct = default)
        => db.GoodsReceipts
            .AsNoTracking()
            .OrderByDescending(g => g.ReceivedAt)
            .ThenByDescending(g => g.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default)
        => db.GoodsReceipts.CountAsync(ct);

    public Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default)
        => db.GoodsReceipts.AddAsync(receipt, ct).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
