namespace MesAdmin.Application.Common;

/// <summary>
/// 工单不存在。
/// Saga 或查询操作在找不到指定工单时抛出，由 API 全局异常处理器映射为 404 Not Found。
/// </summary>
public sealed class OrderNotFoundException(Ulid orderId)
    : Exception($"工单 {orderId} 不存在")
{
    public Ulid OrderId { get; } = orderId;
}
