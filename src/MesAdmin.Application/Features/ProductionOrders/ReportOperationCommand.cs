using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>T1.7 工序报工命令：操作员扫码完成某工序。</summary>
[MemoryPackable]
public sealed partial record ReportOperationCommand(
    Ulid OrderId,
    int Sequence,
    string OperatorId,
    string EquipmentId,
    List<ProcessParameter>? Parameters = null) : IWriteCommand<WorkOrderOperation>;

internal sealed class ReportOperationHandler(
    IWorkOrderOperationRepository operationRepo) : ICommandHandler<ReportOperationCommand, WorkOrderOperation>
{
    public async Task<WorkOrderOperation> ExecuteAsync(ReportOperationCommand cmd, CancellationToken ct)
    {
        var op = await operationRepo.GetByOrderAndSequenceAsync(cmd.OrderId, cmd.Sequence, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 工序 {cmd.Sequence} 不存在");

        var now = DateTimeOffset.UtcNow;
        op.Start(cmd.OperatorId, cmd.EquipmentId, now);
        if (cmd.Parameters is not null)
            op.Parameters.AddRange(cmd.Parameters);
        op.Complete(now);

        operationRepo.Update(op);
        await operationRepo.SaveChangesAsync(ct);
        return op;
    }
}
