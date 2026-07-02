using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>查询工单的工序列表。</summary>
[MemoryPackable]
public sealed partial record GetOperationsQuery(Ulid OrderId) : ICommand<List<WorkOrderOperation>>;

internal sealed class GetOperationsHandler(
    IWorkOrderOperationRepository operationRepo) : ICommandHandler<GetOperationsQuery, List<WorkOrderOperation>>
{
    public Task<List<WorkOrderOperation>> ExecuteAsync(GetOperationsQuery query, CancellationToken ct)
        => operationRepo.GetByOrderIdAsync(query.OrderId, ct);
}
