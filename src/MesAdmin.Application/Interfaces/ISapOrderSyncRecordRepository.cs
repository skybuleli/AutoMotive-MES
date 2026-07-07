using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// SAP 工单同步记录仓储接口（T3.14 工单双向同步）。
/// </summary>
public interface ISapOrderSyncRecordRepository
{
    Task AddAsync(SapOrderSyncRecord record, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<SapOrderSyncRecord> records, CancellationToken cancellationToken = default);
    Task<List<SapOrderSyncRecord>> GetPendingSyncAsync(CancellationToken cancellationToken = default);
    Task<List<SapOrderSyncRecord>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
