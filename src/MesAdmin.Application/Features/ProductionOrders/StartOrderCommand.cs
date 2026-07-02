using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Events;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Features.ProductionOrders;

/// <summary>
/// 开工命令。
/// 只验证前置条件并发布 <see cref="OrderStartedEvent"/>，
/// 状态机推进（Released→InProgress）由 Saga 负责，消除双重写入竞态。
/// </summary>
[MemoryPackable]
public sealed partial record StartOrderCommand(Ulid OrderId) : IWriteCommand<ProductionOrder>;

internal sealed class StartOrderHandler(
    IProductionOrderRepository orders) : ICommandHandler<StartOrderCommand, ProductionOrder>
{
    public async Task<ProductionOrder> ExecuteAsync(StartOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(cmd.OrderId, ct)
            ?? throw new KeyNotFoundException($"工单 {cmd.OrderId} 不存在");

        if (!order.CanStart)
            throw new InvalidOperationException($"工单 {cmd.OrderId} 状态为 {order.Status}，无法开工");

        // ── 事件解耦：发布开工事件，Saga 负责状态推进 + 31 工序编排 ──
        // WaitForAll：确保 Saga 完成 Released→InProgress 转移后再返回
        await new OrderStartedEvent(order.Id, order.OrderNumber).PublishAsync(Mode.WaitForAll, ct);

        // 重新读取以反映 Saga 内部的状态变更（AsNoTracking，纯读）
        return await orders.GetByIdAsync(cmd.OrderId, ct)
            ?? throw new InvalidOperationException($"工单 {cmd.OrderId} 不存在");
    }
}
