using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>创建生产工单命令。</summary>
[MemoryPackable]
public sealed partial record CreateOrderCommand(
    string ProductCode,
    string BomVersion,
    Ulid RoutingId,
    int PlannedQuantity,
    short Priority) : IWriteCommand<ProductionOrder>;

internal sealed class CreateOrderHandler(
    IProductionOrderRepository orders,
    IWorkOrderOperationRepository operationRepo,
    IRoutingRepository routingRepo) : ICommandHandler<CreateOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        // 生成工单号 WO-yyyyMMdd-NNNN（基于当天前缀计数 + 碰撞检测）
        var todayPrefix = $"WO-{DateTimeOffset.Now:yyyyMMdd}-";
        var sequence = await orders.CountByOrderNumberPrefixAsync(todayPrefix, ct) + 1;
        var orderNumber = $"{todayPrefix}{sequence:0000}";

        while (await orders.GetByOrderNumberAsync(orderNumber, ct) is not null)
        {
            sequence++;
            orderNumber = $"{todayPrefix}{sequence:0000}";
        }

        // 领域不变量校验由 ProductionOrder.Create 内部保证（ValidateCreateInput），
        // API 层 FluentValidation 负责 HTTP 契约格式校验，此处不重复验证。
        var order = ProductionOrder.Create(
            Ulid.NewUlid(),
            orderNumber,
            cmd.ProductCode,
            cmd.RoutingId,
            cmd.BomVersion,
            cmd.PlannedQuantity,
            cmd.Priority,
            DateTimeOffset.UtcNow);

        await orders.AddAsync(order, ct);

        // ═══ P0 集成：优先从 Routing 表查询工艺路线，未找到则回退到硬编码默认值 ═══
        var routing = await routingRepo.GetByIdAsync(cmd.RoutingId, ct);
        var routingData = routing is not null && routing.Operations.Count > 0
            ? routing.Operations.OrderBy(o => o.Sequence)
                .Select(o => (o.Sequence, o.Station, o.OperationCode, o.OperationName))
                .ToList()
            : null;

        // 初始化 31 道工序记录
        foreach (var (seq, station, code, name) in routingData ?? ProductionRoutings.Default)
        {
            var op = WorkOrderOperation.Create(order.Id, seq, station, code, name);
            await operationRepo.AddAsync(op, ct);
        }

        await orders.SaveChangesAsync(ct);
        return order;
    }
}
