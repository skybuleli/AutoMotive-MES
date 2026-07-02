using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Workflows;

/// <summary>
/// 用 Cleipnir 触发生产工单 Saga。
/// 调用方可 await 等待 Saga 完成（或异常），CancellationToken 正常传播。
/// 只传 Ulid orderId —— Saga 内部从 DB 重新读取最新状态。
/// </summary>
public sealed class CleipnirProductionOrderSagaRunner(
    CleipnirSagaRegistry registry,
    ILogger<CleipnirProductionOrderSagaRunner> logger) : IProductionOrderSagaRunner
{
    public async Task StartAsync(Ulid orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 直接 await Saga 调用，异常会自然传播给调用方
            await registry.InvokeProductionOrderSagaAsync(orderId.ToString(), orderId);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"生产工单 Saga 启动失败: {orderId}");
            throw;
        }
    }
}
