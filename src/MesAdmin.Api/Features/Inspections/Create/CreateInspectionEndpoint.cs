using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Inspections;
using MesAdmin.Domain.Models;
using MesAdmin.Api.Features.Inspections.GetById;

namespace MesAdmin.Api.Features.Inspections.Create;

public class CreateInspectionEndpoint : MesEndpoint<CreateInspectionRequest, InspectionResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<InspectionGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建首件检验任务");
    }

    public override async Task HandleAsync(CreateInspectionRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var inspection = await new CreateInspectionCommand(orderId, req.InspectionType, req.OperatorId).ExecuteAsync(ct);
        Response = InspectionMapper.ToResponse(inspection);
        await SendCreatedDualAsync<GetInspectionByIdEndpoint>(
            new { orderId = orderIdStr, inspectionId = inspection.Id.ToString() }, ct);
    }
}

public class CreateInspectionValidator : Validator<CreateInspectionRequest>
{
    public CreateInspectionValidator()
    {
        RuleFor(x => x.InspectionType).NotEmpty().WithMessage("检验类型不能为空");
        RuleFor(x => x.OperatorId).NotEmpty().WithMessage("操作员工号不能为空");
    }
}

[MemoryPackable]
public partial class CreateInspectionRequest
{
    public string InspectionType { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
}
