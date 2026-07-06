using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Maintenance;

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record SparePartResponse(
    string Id,
    string MaterialCode,
    string MaterialName,
    string Specification,
    string Unit,
    double CurrentQuantity,
    double SafetyStock,
    double MinimumStock,
    string? EquipmentCode,
    string? Remarks,
    string StockLevel,
    bool NeedsPurchaseRequest,
    double SuggestedPurchaseQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record SparePartUsageResponse(
    string Id,
    string SparePartId,
    string MaintenanceWorkOrderId,
    double Quantity,
    double? UnitPrice,
    string? Remarks,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record PurchaseRequestResponse(
    string Id,
    string RequestNumber,
    string SparePartId,
    double Quantity,
    string Reason,
    string Status,
    string RequestedBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record StockCheckResponse(
    string SparePartId,
    string MaterialCode,
    string MaterialName,
    double CurrentQuantity,
    double SafetyStock,
    double MinimumStock,
    string StockLevel,
    double SuggestedPurchaseQuantity,
    PurchaseRequestResponse? ExistingPurchaseRequest);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateSparePartRequest
{
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double SafetyStock { get; set; }
    public double MinimumStock { get; set; }
    public string? EquipmentCode { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class UpdateStockRequest
{
    public double NewQuantity { get; set; }
}

[MemoryPackable]
public partial class RestockRequest
{
    public double Quantity { get; set; }
}

[MemoryPackable]
public partial class ConsumeSparePartRequest
{
    public string SparePartId { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public double? UnitPrice { get; set; }
    public string? Remarks { get; set; }
}

[MemoryPackable]
public partial class CreatePurchaseRequestRequest
{
    public string SparePartId { get; set; } = string.Empty;
    public double? Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ApprovePurchaseRequestRequest
{
    public string ApprovedBy { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CheckStockRequest
{
    public string SparePartId { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class SparePartMapper
{
    public static SparePartResponse ToResponse(SparePart p) => new(
        p.Id.ToString(),
        p.MaterialCode,
        p.MaterialName,
        p.Specification,
        p.Unit,
        p.CurrentQuantity,
        p.SafetyStock,
        p.MinimumStock,
        p.EquipmentCode,
        p.Remarks,
        p.GetStockLevel().ToString(),
        p.NeedsPurchaseRequest,
        p.SuggestedPurchaseQuantity,
        p.CreatedAt,
        p.UpdatedAt);

    public static SparePartUsageResponse ToUsageResponse(SparePartUsage u) => new(
        u.Id.ToString(),
        u.SparePartId.ToString(),
        u.MaintenanceWorkOrderId.ToString(),
        u.Quantity,
        u.UnitPrice,
        u.Remarks,
        u.CreatedAt);

    public static PurchaseRequestResponse ToPurchaseResponse(PurchaseRequest r) => new(
        r.Id.ToString(),
        r.RequestNumber,
        r.SparePartId.ToString(),
        r.Quantity,
        r.Reason,
        r.Status,
        r.RequestedBy,
        r.ApprovedBy,
        r.ApprovedAt,
        r.CreatedAt,
        r.UpdatedAt);
}
