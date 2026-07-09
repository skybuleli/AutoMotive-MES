namespace MesAdmin.Application.Common;

/// <summary>
/// 工单号唯一约束冲突。
/// 并发创建时由 Infrastructure 层将 PostgreSQL 唯一冲突（23505）翻译为此异常，
/// 由创建 handler 捕获后递增序号重试，避免直接 500。
/// </summary>
public sealed class DuplicateOrderNumberException(string orderNumber)
    : Exception($"工单号 {orderNumber} 已存在（并发冲突）")
{
    public string OrderNumber { get; } = orderNumber;
}
