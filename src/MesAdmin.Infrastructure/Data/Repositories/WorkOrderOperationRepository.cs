using Microsoft.EntityFrameworkCore;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>
/// 工单工序仓储实现。
/// </summary>
public class WorkOrderOperationRepository(MesDbContext db) : IWorkOrderOperationRepository
{
    public Task<WorkOrderOperation?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.WorkOrderOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<WorkOrderOperation?> GetByOrderAndSequenceAsync(Ulid orderId, int sequence, CancellationToken ct = default)
        => db.WorkOrderOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.Sequence == sequence, ct);

    public Task<WorkOrderOperation?> GetByOrderAndSequenceTrackedAsync(Ulid orderId, int sequence, CancellationToken ct = default)
        => db.WorkOrderOperations
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.Sequence == sequence, ct);

    public Task<List<WorkOrderOperation>> GetByOrderIdAsync(Ulid orderId, CancellationToken ct = default)
        => db.WorkOrderOperations
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .OrderBy(o => o.Sequence)
            .ToListAsync(ct);

    public Task<List<WorkOrderOperation>> GetByOrderIdTrackedAsync(Ulid orderId, CancellationToken ct = default)
        => db.WorkOrderOperations
            .Where(o => o.OrderId == orderId)
            .OrderBy(o => o.Sequence)
            .ToListAsync(ct);

    public Task AddAsync(WorkOrderOperation operation, CancellationToken ct = default)
        => db.WorkOrderOperations.AddAsync(operation, ct).AsTask();

    public void Update(WorkOrderOperation operation)
        => db.WorkOrderOperations.Update(operation);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
