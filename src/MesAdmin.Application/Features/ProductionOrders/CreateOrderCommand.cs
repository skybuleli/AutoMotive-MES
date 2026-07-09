using FastEndpoints;
using MemoryPack;
using MesAdmin.Application.Common;
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
    /// <summary>并发唯一冲突时的最大重试次数。</summary>
    private const int MaxOrderNumberAttempts = 5;

    public async Task<ProductionOrder> ExecuteAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        // 工单号 WO-yyyyMMdd-NNNN：统一使用 UTC，与 CreatedAt 保持一致，避免跨时区错号。
        var todayPrefix = $"WO-{DateTimeOffset.UtcNow:yyyyMMdd}-";
        var sequence = await orders.CountByOrderNumberPrefixAsync(todayPrefix, ct) + 1;

        // ═══ P0 集成：优先从 Routing 表查询工艺路线，未找到则回退到硬编码默认值 ═══
        // 预取放在重试循环外，避免并发冲突重试时重复查询。
        var routing = await routingRepo.GetByIdAsync(cmd.RoutingId, ct);
        var routingData = routing is not null && routing.Operations.Count > 0
            ? routing.Operations.OrderBy(o => o.Sequence)
                .Select(o => (o.Sequence, o.Station, o.OperationCode, o.OperationName))
                .ToList()
            : (IReadOnlyList<(int, int, string, string)>?)null;

        // 领域不变量校验由 ProductionOrder.Create 内部保证（ValidateCreateInput）。
        var order = ProductionOrder.Create(
            Ulid.NewUlid(),
            $"{todayPrefix}{sequence:0000}",
            cmd.ProductCode,
            cmd.RoutingId,
            cmd.BomVersion,
            cmd.PlannedQuantity,
            cmd.Priority,
            DateTimeOffset.UtcNow);

        await orders.AddAsync(order, ct);

        // 初始化 31 道工序记录（与工单同一事务提交）
        foreach (var (seq, station, code, name) in routingData ?? ProductionRoutings.Default)
        {
            var op = WorkOrderOperation.Create(order.Id, seq, station, code, name);
            await operationRepo.AddAsync(op, ct);
        }

        // 并发安全：依赖 order_number 唯一索引 + 冲突重试，取代非原子的碰撞预检查询。
        // 冲突时复用已跟踪实体（保持 Added 状态），仅递增序号后重新提交。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await orders.SaveChangesAsync(ct);
                return order;
            }
            catch (DuplicateOrderNumberException) when (attempt < MaxOrderNumberAttempts)
            {
                sequence++;
                order.OrderNumber = $"{todayPrefix}{sequence:0000}";
            }
        }
    }
}
