using FastEndpoints;
using MemoryPack;

namespace MesAdmin.Api.Features.Traceability;

/// <summary>追溯端点组（api/v1/traceability）</summary>
public class TraceabilityGroup : Group
{
    public TraceabilityGroup() => Configure("api/v1/traceability", ep => { });
}

// ═══════════════════════════════════════════
// 响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record TraceabilityLinkResponse(
    string Id,
    int Level,
    string LevelName,
    string VinOrSerial,
    string OrderId,
    string ComponentBatch,
    string MaterialBatch,
    string PreviousHash,
    string Hash,
    DateTimeOffset CreatedAt,
    bool HashVerified);

public static class TraceabilityMapper
{
    public static TraceabilityLinkResponse ToResponse(Domain.Models.TraceabilityLink link)
        => new(
            link.Id.ToString(),
            (int)link.Level,
            link.Level.ToString(),
            link.VinOrSerial,
            link.OrderId.ToString(),
            link.ComponentBatch,
            link.MaterialBatch,
            link.PreviousHash,
            link.Hash,
            link.CreatedAt,
            link.VerifyHash());
}
