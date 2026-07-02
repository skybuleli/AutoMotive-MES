using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 工单工序仓储接口。
/// </summary>
public interface IWorkOrderOperationRepository
{
    Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken cancellationToken = default);
    Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken cancellationToken = default);
    Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(WorkOrderOperation operation, CancellationToken cancellationToken = default);
    void Update(WorkOrderOperation operation);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
