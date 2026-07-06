using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Iqc;

// ═══════════════════════════════════════════
//  POST /api/v1/quality/iqc — 创建 IQC 检验记录
// ═══════════════════════════════════════════

public class CreateIqcEndpoint : MesEndpoint<CreateIqcRequest, QualityRecordResponse>
{
    public override void Configure()
    {
        Post("/iqc");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "创建 IQC 来料检验记录");
    }

    public override async Task HandleAsync(CreateIqcRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.InspectionPlanId, out var planId))
        {
            AddError("InspectionPlanId", "无效的检验计划 Id");
            ThrowIfAnyErrors();
        }

        var record = await new CreateIqcRecordCommand(
            planId,
            req.InspectionPlanName,
            req.MaterialCode,
            req.MaterialName,
            req.BatchNumber,
            req.SupplierCode,
            req.SupplierName,
            req.InspectorId,
            req.SampleSize,
            req.AcceptNumber,
            req.RejectNumber,
            req.AqlScheme).ExecuteAsync(ct);

        Response = QualityMapper.ToRecordResponse(record);
        await SendDualAsync(ct);
    }
}

public class CreateIqcValidator : Validator<CreateIqcRequest>
{
    public CreateIqcValidator()
    {
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.BatchNumber).NotEmpty().WithMessage("批次号不能为空");
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("检验员工号不能为空");
        RuleFor(x => x.SampleSize).GreaterThan(0).WithMessage("抽样数量必须大于 0");
    }
}

[MemoryPackable]
public partial class CreateIqcRequest
{
    public string InspectionPlanId { get; set; } = string.Empty;
    public string InspectionPlanName { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string InspectorId { get; set; } = string.Empty;
    public int SampleSize { get; set; } = 5;
    public int AcceptNumber { get; set; } = 0;
    public int RejectNumber { get; set; } = 1;
    public string? AqlScheme { get; set; }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/iqc/{id}/measure — 记录实测值
// ═══════════════════════════════════════════

public class RecordIqcMeasurementEndpoint : MesEndpoint<RecordIqcMeasurementRequest, QualityRecordResponse>
{
    public override void Configure()
    {
        Post("/iqc/{id}/measure");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "记录 IQC 检验特性实测值");
    }

    public override async Task HandleAsync(RecordIqcMeasurementRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var recordId))
        {
            AddError("id", "无效的检验记录 Id");
            ThrowIfAnyErrors();
        }

        var record = await new RecordIqcMeasurementCommand(recordId, req.CharacteristicCode, req.ActualValue).ExecuteAsync(ct);
        Response = QualityMapper.ToRecordResponse(record);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class RecordIqcMeasurementRequest
{
    public string CharacteristicCode { get; set; } = string.Empty;
    public double ActualValue { get; set; }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/iqc/{id}/complete — 完成检验
// ═══════════════════════════════════════════

public class CompleteIqcEndpoint : MesEndpointWithoutRequest<QualityRecordResponse>
{
    public override void Configure()
    {
        Post("/iqc/{id}/complete");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "完成 IQC 检验并自动判定（不合格自动生成 NCR）");
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
