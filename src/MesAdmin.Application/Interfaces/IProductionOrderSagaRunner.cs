namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 生产工单 Cleipnir Saga 启动器。
/// 只传 Ulid orderId —— Saga 内部从 DB 重新读取最新状态，避免传入过期聚合。
/// </summary>
public interface IProductionOrderSagaRunner
{
    Task StartAsync(Ulid orderId, CancellationToken cancellationToken = default);
}
