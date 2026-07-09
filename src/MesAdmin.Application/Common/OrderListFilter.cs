using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Common;

/// <summary>
/// 工单列表查询过滤条件（M01 列表检索）。
/// 所有字段可选，null 表示不按该维度过滤。
/// </summary>
public sealed record OrderListFilter(
    OrderStatus? Status = null,
    string? OrderNumberContains = null,
    string? ProductCode = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null)
{
    public static readonly OrderListFilter Empty = new();
}
