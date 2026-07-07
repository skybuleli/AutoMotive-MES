using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// SAP 工单同步记录仓储实现（T3.14）。
/// </summary>
public class SapOrderSyncRecordRepository(MesDbContext db) : ISapOrderSyncRecordRepository
{
    public Task AddAsync(SapOrderSyncRecord record, CancellationToken ct = default)
        => db.SapOrderSyncRecords.AddAsync(record, ct).AsTask();

    public Task AddRangeAsync(IEnumerable<SapOrderSyncRecord> records, CancellationToken ct = default)
        => db.SapOrderSyncRecords.AddRangeAsync(records, ct);

    public Task<List<SapOrderSyncRecord>> GetPendingSyncAsync(CancellationToken ct = default)
        => db.SapOrderSyncRecords
            .AsNoTracking()
            .Where(r => !r.SapSynced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<List<SapOrderSyncRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.SapOrderSyncRecords
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
