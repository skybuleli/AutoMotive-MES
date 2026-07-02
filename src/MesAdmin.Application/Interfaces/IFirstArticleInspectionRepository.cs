using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 首件检验仓储接口。
/// </summary>
public interface IFirstArticleInspectionRepository
{
    Task<FirstArticleInspection?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    /// <summary>跟踪查询，适用于写操作——修改属性后直接 SaveChangesAsync 即可，无需 Update。</summary>
    Task<FirstArticleInspection?> GetByIdTrackedAsync(Ulid id, CancellationToken ct = default);
    Task<List<FirstArticleInspection>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default);
    Task AddAsync(FirstArticleInspection inspection, CancellationToken ct = default);
    void Update(FirstArticleInspection inspection);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
