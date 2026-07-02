using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工单工序仓储接口。
/// </summary>
public interface IWorkOrderOperationRepository
{
    Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken cancellationToken = default);
    /// <summary>跟踪查询，适用于写操作——修改属性后直接 SaveChangesAsync 即可，无需 Update。</summary>
    Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken cancellationToken = default);
    Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    /// <summary>跟踪查询，适用于写操作——修改属性后直接 SaveChangesAsync 即可，无需 Update。</summary>
    Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkOrderOperation operation, CancellationToken cancellationToken = default);
    void Update(WorkOrderOperation operation);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
