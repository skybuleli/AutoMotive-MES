using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// SAP 拒单记录仓储接口（T1.3 拒单回写）。
/// </summary>
public interface ISapRejectionRepository
{
    Task<SapRejectionRecord?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<List<SapRejectionRecord>> GetPendingWritebackAsync(CancellationToken cancellationToken = default);
    Task<List<SapRejectionRecord>> GetByExternalOrderNumberAsync(string externalOrderNumber, CancellationToken cancellationToken = default);
    Task AddAsync(SapRejectionRecord record, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 成品入库单仓储接口（T1.8 完工确认）。
/// </summary>
public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task<List<GoodsReceipt>> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task AddAsync(GoodsReceipt receipt, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
