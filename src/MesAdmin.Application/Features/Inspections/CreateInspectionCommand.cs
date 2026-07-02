using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.Inspections;

/// <summary>创建班次首件检验任务命令。</summary>
[MemoryPackable]
public sealed partial record CreateInspectionCommand(
    Ulid OrderId,
    string InspectionType,
    string OperatorId) : IWriteCommand<FirstArticleInspection>;

internal sealed class CreateInspectionHandler(
    IFirstArticleInspectionRepository repo,
    IProductionOrderRepository orders) : ICommandHandler<CreateInspectionCommand, FirstArticleInspection>
{
    public async Task<FirstArticleInspection> ExecuteAsync(CreateInspectionCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        var items = InspectionControlPlans.Esp9
            .Select(c => InspectionItem.Create(c.Code, c.Name, c.Std, c.Unit, c.Upper, c.Lower))
            .ToList();

        var inspection = FirstArticleInspection.Create(
            cmd.OrderId, order.OrderNumber, order.ProductCode,
            cmd.InspectionType, cmd.OperatorId, items);

        await repo.AddAsync(inspection, ct);
        await repo.SaveChangesAsync(ct);
        return inspection;
    }
}
