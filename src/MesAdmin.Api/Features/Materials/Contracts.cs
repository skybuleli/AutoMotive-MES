using FastEndpoints;
using MemoryPack;

namespace MesAdmin.Api.Features.Materials;

/// <summary>物料端点组（api/v1/materials）</summary>
public class MaterialGroup : Group
{
    public MaterialGroup() => Configure("api/v1/materials", ep => { });
}

// ═══════════════════════════════════════════
// 响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record MaterialBatchResponse(
    string Id,
    string MaterialCode,
    string MaterialName,
    string BatchNumber,
    string SupplierCode,
    string SupplierName,
    double ReceivedQuantity,
    double RemainingQuantity,
    string Unit,
    bool IsCritical,
    string Status,
    DateTimeOffset? ProductionDate,
    DateTimeOffset ReceivedAt);

[MemoryPackable]
public partial record MaterialBindingResponse(
    string Id,
    string OrderId,
    string MaterialBatchId,
    string MaterialCode,
    string BatchNumber,
    string ProductSerial,
    double Quantity,
    bool PokaYokePassed,
    string OperatorId,
    DateTimeOffset BoundAt);

public static class MaterialMapper
{
    public static MaterialBatchResponse ToResponse(Domain.Models.MaterialBatch b)
        => new(
            b.Id.ToString(), b.MaterialCode, b.MaterialName, b.BatchNumber,
            b.SupplierCode, b.SupplierName, b.ReceivedQuantity, b.RemainingQuantity,
            b.Unit, b.IsCritical, b.Status.ToString(), b.ProductionDate, b.ReceivedAt);

    public static MaterialBindingResponse ToResponse(Domain.Models.MaterialBinding b)
        => new(
            b.Id.ToString(), b.OrderId.ToString(), b.MaterialBatchId.ToString(),
            b.MaterialCode, b.BatchNumber, b.ProductSerial, b.Quantity,
            b.PokaYokePassed, b.OperatorId, b.BoundAt);
}
