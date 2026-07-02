using FastEndpoints;
using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Application.Events;

/// <summary>
/// 订阅 <see cref="OrderStartedEvent"/>，触发 Cleipnir 生产工单 Saga。
/// 通过事件解耦：命令 handler 只负责发布事件，不直接依赖 Saga 引擎。
/// Saga 内部从 DB 重新读取工单并推进全部状态机（Released→InProgress 等），
/// 消除 handler 与 Saga 双重写入的竞态。
/// </summary>
internal sealed class OrderStartedSagaTrigger(
    IProductionOrderSagaRunner sagaRunner,
    ILogger<OrderStartedSagaTrigger> logger) : IEventHandler<OrderStartedEvent>
{
    public async Task HandleAsync(OrderStartedEvent evt, CancellationToken ct)
    {
        logger.ZLogInformation($"工单 {evt.OrderNumber} 已开工，触发 Saga");
        await sagaRunner.StartAsync(evt.OrderId, ct);
    }
}
