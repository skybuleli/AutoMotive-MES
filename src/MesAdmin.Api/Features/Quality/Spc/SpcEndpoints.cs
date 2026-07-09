using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;

namespace MesAdmin.Api.Features.Quality.Spc;

// ═══════════════════════════════════════════
//  POST /api/v1/quality/spc/samples — 记录 SPC 样本
// ═══════════════════════════════════════════

public class RecordSpcSampleEndpoint : MesEndpoint<RecordSpcSampleRequest, SpcSampleResultResponse>
{
    public override void Configure()
    {
        Post("/spc/samples");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Technician);
        Summary(s => s.Summary = "记录 SPC 测量样本（自动触发电控图判异检查）");
    }

    public override async Task HandleAsync(RecordSpcSampleRequest req, CancellationToken ct)
    {
        var result = await new RecordSpcSampleCommand(
            req.CharacteristicCode,
            req.OrderId is not null ? Ulid.Parse(req.OrderId) : null,
            req.OrderNumber,
            req.EquipmentCode,
            req.Values,
            req.Source ?? "Manual").ExecuteAsync(ct);

        Response = new SpcSampleResultResponse(
            QualityMapper.ToSampleResponse(result.Sample),
            result.Alerts.Select(QualityMapper.ToRuleAlertResponse).ToList());
        await SendDualAsync(ct);
    }
}

public class RecordSpcSampleValidator : Validator<RecordSpcSampleRequest>
{
    public RecordSpcSampleValidator()
    {
        RuleFor(x => x.CharacteristicCode).NotEmpty().WithMessage("特性编码不能为空");
        RuleFor(x => x.Values).NotEmpty().WithMessage("测量值不能为空")
            .Must(v => v.Count >= 2).WithMessage("至少需要 2 个测量值");
    }
}

[MemoryPackable]
public partial class RecordSpcSampleRequest
{
    public string CharacteristicCode { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? EquipmentCode { get; set; }
    public List<double> Values { get; set; } = [];
    public string? Source { get; set; }
}

// ═══════════════════════════════════════════
//  GET /api/v1/quality/spc/samples?charCode=&limit=
//  — 查询 SPC 样本（按特性编码）
// ═══════════════════════════════════════════

public class ListSpcSamplesEndpoint : MesEndpointWithoutRequest<List<SpcSampleResponse>>
{
    public override void Configure()
    {
        Get("/spc/samples");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Technician, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询 SPC 样本列表（支持按特性编码筛选）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var charCode = Query<string?>("charCode", isRequired: false);
        var limitStr = Query<string?>("limit", isRequired: false);
        var limit = int.TryParse(limitStr, out var l) ? l : 25;

        var repo = Resolve<ISpcSampleRepository>();
        List<Domain.Models.SpcSample> samples;

        if (!string.IsNullOrWhiteSpace(charCode))
            samples = await repo.GetByCharacteristicAsync(charCode, limit, ct);
        else
        {
            Response = [];
            await SendDualAsync(ct);
            return;
        }

        Response = samples.Select(QualityMapper.ToSampleResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/quality/spc/alerts — 未确认告警列表
// ═══════════════════════════════════════════

public class ListSpcAlertsEndpoint : MesEndpointWithoutRequest<List<SpcRuleAlertResponse>>
{
    public override void Configure()
    {
        Get("/spc/alerts");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询未确认的 SPC 判异告警");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var charCode = Query<string?>("charCode", isRequired: false);
        var repo = Resolve<ISpcRuleAlertRepository>();

        var alerts = await repo.GetUnacknowledgedAsync(
            string.IsNullOrWhiteSpace(charCode) ? null : charCode, ct);
        Response = alerts.Select(QualityMapper.ToRuleAlertResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/spc/alerts/{id}/ack
//  — 确认 SPC 告警
// ═══════════════════════════════════════════

public class AcknowledgeSpcAlertEndpoint : MesEndpoint<AcknowledgeSpcAlertRequest, SpcRuleAlertResponse>
{
    public override void Configure()
    {
        Post("/spc/alerts/{id}/ack");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "确认 SPC 判异告警");
    }

    public override async Task HandleAsync(AcknowledgeSpcAlertRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var alertId))
        {
            AddError("id", "无效的告警 Id");
            ThrowIfAnyErrors();
        }

        var alert = await new AcknowledgeSpcAlertCommand(alertId, req.AcknowledgedBy, req.ActionTaken).ExecuteAsync(ct);
        Response = QualityMapper.ToRuleAlertResponse(alert);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial class AcknowledgeSpcAlertRequest
{
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string? ActionTaken { get; set; }
}
