using MemoryPack;

namespace MesAdmin.Api.Features.Inspections;

// ═══════════════════════════════════════════
// 响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record InspectionResponse(
    string Id,
    string OrderId,
    string OrderNumber,
    string ProductCode,
    string InspectionType,
    string Status,
    string OperatorId,
    string? InspectorId,
    List<InspectionItemResponse> Items,
    string? Conclusion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

[MemoryPackable]
public partial record InspectionItemResponse(
    string CharacteristicCode,
    string CharacteristicName,
    double StandardValue,
    double? UpperLimit,
    double? LowerLimit,
    string Unit,
    double? ActualValue,
    bool IsPass);

public static class InspectionMapper
{
    public static InspectionResponse ToResponse(Domain.Models.FirstArticleInspection inspection)
        => new(
            inspection.Id.ToString(),
            inspection.OrderId.ToString(),
            inspection.OrderNumber,
            inspection.ProductCode,
            inspection.InspectionType,
            inspection.Status.ToString(),
            inspection.OperatorId,
            inspection.InspectorId,
            inspection.Items.Select(i => new InspectionItemResponse(
                i.CharacteristicCode,
                i.CharacteristicName,
                i.StandardValue,
                i.UpperLimit,
                i.LowerLimit,
                i.Unit,
                i.ActualValue,
                i.IsPass)).ToList(),
            inspection.Conclusion,
            inspection.CreatedAt,
            inspection.CompletedAt);
}
