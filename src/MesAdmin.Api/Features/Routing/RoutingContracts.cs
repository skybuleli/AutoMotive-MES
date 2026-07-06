using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Routing;

/// <summary>
/// 工艺路线端点组（api/v1/routing）。
/// </summary>
public class RoutingGroup : Group
{
    public RoutingGroup() => Configure("api/v1/routing", ep => { });
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record RoutingResponse(
    string Id,
    string ProductCode,
    string Name,
    string Version,
    string? EcoNumber,
    string EcoStatus,
    int OperationCount,
    bool IsActive,
    string? ChangeDescription,
    string CreatedBy,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? EffectiveDate,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<RoutingOperationDto> Operations);

[MemoryPackable]
public partial record RoutingOperationDto(
    int Sequence,
    int Station,
    string OperationCode,
    string OperationName,
    double? StandardTimeSeconds,
    string? FixtureCode,
    string? FixtureName,
    List<ParameterTemplateDto> ParameterTemplates);

[MemoryPackable]
public partial record ParameterTemplateDto(
    string ParameterCode,
    string ParameterName,
    double StandardValue,
    double? UpperSpecLimit,
    double? LowerSpecLimit,
    string Unit,
    bool EnableSpc,
    int SpcSubgroupSize);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateRoutingRequest
{
    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? EcoNumber { get; set; }
    public string? ChangeDescription { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<CreateRoutingOperationRequest> Operations { get; set; } = [];
}

[MemoryPackable]
public partial class CreateRoutingOperationRequest
{
    public int Sequence { get; set; }
    public int Station { get; set; }
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public double? StandardTimeSeconds { get; set; }
    public string? FixtureCode { get; set; }
    public string? FixtureName { get; set; }
    public List<CreateParameterTemplateRequest> ParameterTemplates { get; set; } = [];
}

[MemoryPackable]
public partial class CreateParameterTemplateRequest
{
    public string ParameterCode { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public double StandardValue { get; set; }
    public double? UpperSpecLimit { get; set; }
    public double? LowerSpecLimit { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool EnableSpc { get; set; }
    public int SpcSubgroupSize { get; set; } = 5;
}

[MemoryPackable]
public partial class ApproveRoutingRequest
{
    public string ApprovedBy { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  T3.3 防错三重校验 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class VerifyOperationRequest
{
    /// <summary>工单 Id</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>扫码物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料批次 Id</summary>
    public string MaterialBatchId { get; set; } = string.Empty;

    /// <summary>设备编号</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>设备当前参数值列表（安全优先：校验该工站 ALL 工序的参数）</summary>
    public List<EquipmentParameterValue> CurrentParameters { get; set; } = [];
}

[MemoryPackable]
public partial class EquipmentParameterValue
{
    /// <summary>参数编码（如 TOR-M6, HYD-01）</summary>
    public string ParameterCode { get; set; } = string.Empty;

    /// <summary>当前实际值</summary>
    public double Value { get; set; }
}

[MemoryPackable]
public partial record VerifyOperationResponse(
    bool Passed,
    List<CheckResultDto> Checks);

[MemoryPackable]
public partial record CheckResultDto(
    string CheckName,
    bool Passed,
    string? Message);

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class RoutingMapper
{
    public static RoutingResponse ToResponse(Domain.Models.Routing r) => new(
        r.Id.ToString(),
        r.ProductCode,
        r.Name,
        r.Version,
        r.EcoNumber,
        r.EcoStatus.ToString(),
        r.OperationCount,
        r.IsActive,
        r.ChangeDescription,
        r.CreatedBy,
        r.ApprovedBy,
        r.ApprovedAt,
        r.EffectiveDate,
        r.ExpirationDate,
        r.CreatedAt,
        r.UpdatedAt,
        r.Operations.Select(ToOpDto).ToList());

    private static RoutingOperationDto ToOpDto(Domain.Models.RoutingOperation op) => new(
        op.Sequence,
        op.Station,
        op.OperationCode,
        op.OperationName,
        op.StandardTimeSeconds,
        op.FixtureCode,
        op.FixtureName,
        op.ParameterTemplates.Select(ToParamDto).ToList());

    private static ParameterTemplateDto ToParamDto(Domain.Models.ParameterTemplate pt) => new(
        pt.ParameterCode,
        pt.ParameterName,
        pt.StandardValue,
        pt.UpperSpecLimit,
        pt.LowerSpecLimit,
        pt.Unit,
        pt.EnableSpc,
        pt.SpcSubgroupSize);
}
