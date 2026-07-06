using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Ipqc;

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ipqc — 创建 IPQC 检验记录
// ═══════════════════════════════════════════

public class CreateIpqcEndpoint : MesEndpoint<CreateIpqcRequest, QualityRecordResponse>
{
    public override void Configure()
    {
        Post("/ipqc");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "创建 IPQC 过程巡检记录");
    }

    public override async Task HandleAsync(CreateIpqcRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError("OrderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }
        if (!Ulid.TryParse(req.InspectionPlanId, out var planId))
        {
            AddError("InspectionPlanId", "无效的检验计划 Id");
            ThrowIfAnyErrors();
        }

        var characteristics = req.Characteristics.Select(c => MeasuredCharacteristic.Create(
            c.CharacteristicCode, c.CharacteristicName, c.StandardValue, c.Unit,
            c.UpperSpecLimit, c.LowerSpecLimit)).ToList();

        var record = await new CreateIpqcRecordCommand(
            orderId,
            req.OrderNumber,
            req.ProductCode,
            req.ProductName,
            planId,
            req.InspectionPlanName,
            req.InspectorId,
            characteristics,
            req.AcceptNumber,
            req.RejectNumber).ExecuteAsync(ct);

        Response = QualityMapper.ToRecordResponse(record);
        await SendDualAsync(ct);
    }
}

public class CreateIpqcValidator : Validator<CreateIpqcRequest>
{
    public CreateIpqcValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("检验员工号不能为空");
        RuleFor(x => x.Characteristics).NotEmpty().WithMessage("检验特性列表不能为空");
    }
}

[MemoryPackable]
public partial class CreateIpqcRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string InspectionPlanId { get; set; } = string.Empty;
    public string InspectionPlanName { get; set; } = string.Empty;
    public string InspectorId { get; set; } = string.Empty;
    public int AcceptNumber { get; set; }
    public int RejectNumber { get; set; } = 1;
    public List<MeasuredCharacteristicRequest> Characteristics { get; set; } = [];
}

[MemoryPackable]
public partial class MeasuredCharacteristicRequest
{
    public string CharacteristicCode { get; set; } = string.Empty;
    public string CharacteristicName { get; set; } = string.Empty;
    public double StandardValue { get; set; }
    public double? UpperSpecLimit { get; set; }
    public double? LowerSpecLimit { get; set; }
    public string Unit { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/ipqc/{id}/complete — 完成 IPQC
// ═══════════════════════════════════════════

public class CompleteIpqcEndpoint : MesEndpointWithoutRequest<QualityRecordResponse>
{
    public override void Configure()
    {
        Post("/ipqc/{id}/complete");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "完成 IPQC 检验并自动判定");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var recordId))
        {
            AddError("id", "无效的检验记录 Id");
            ThrowIfAnyErrors();
        }

        var record = await new CompleteQualityRecordCommand(recordId).ExecuteAsync(ct);
        Response = QualityMapper.ToRecordResponse(record);
        await SendDualAsync(ct);
    }
}
