using MemoryPack;

namespace MesAdmin.Api.Features.ProductionOrders;

// ═══════════════════════════════════════════
// 响应 DTO（响应可以是 positional record，因为不需要 new() 约束）
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record ProductionOrderSummaryResponse(
    string Id,
    string OrderNumber,
    string ProductCode,
    string Status,
    short Priority,
    string RoutingId,
    string BomVersion,
    int PlannedQuantity,
    int QualifiedQuantity,
    int DefectiveQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

[MemoryPackable]
public partial record ProductionOrderDetailResponse(
    ProductionOrderSummaryResponse Order,
    bool CanRelease,
    bool CanStart,
    bool CanComplete,
    bool CanClose);

[MemoryPackable]
public partial record OperationResponse(
    string Id,
    string OrderId,
    int Sequence,
    int Station,
    string OperationCode,
    string OperationName,
    string Status,
    string? OperatorId,
    string? EquipmentId,
    DateTimeOffset? StartAt,
    DateTimeOffset? EndAt,
    string? FailureReason);

/// <summary>工单摘要/详情映射工具</summary>
public static class OrderMapper
{
    public static ProductionOrderSummaryResponse ToSummary(Domain.Models.ProductionOrder order)
        => new(
            order.Id.ToString(),
            order.OrderNumber,
            order.ProductCode,
            order.Status.ToString(),
            order.Priority,
            order.RoutingId.ToString(),
            order.BomVersion,
            order.PlannedQuantity,
            order.QualifiedQuantity,
            order.DefectiveQuantity,
            order.CreatedAt,
            order.CompletedAt);

    public static ProductionOrderDetailResponse ToDetail(Domain.Models.ProductionOrder order)
        => new(
            ToSummary(order),
            order.CanRelease,
            order.CanStart,
            order.CanComplete,
            order.CanClose);

    public static OperationResponse ToOperationResponse(Domain.Models.WorkOrderOperation op)
        => new(
            op.Id.ToString(),
            op.OrderId.ToString(),
            op.Sequence,
            op.Station,
            op.OperationCode,
            op.OperationName,
            op.Status.ToString(),
            op.OperatorId,
            op.EquipmentId,
            op.StartAt,
            op.EndAt,
            op.FailureReason);
}
