using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Ipqc;

/// <summary>
/// 内置标准 IPQC 过程巡检模板（检验计划表为空时的回退特性集）。
/// 覆盖关键扭矩/液压/电气特性，规格界限与 ESP 控制计划一致。
/// </summary>
internal static class StandardIpqcTemplate
{
    public static List<MeasuredCharacteristic> Create()
    {
        static MeasuredCharacteristic M(string code, string name, double std, string unit, double? usl = null, double? lsl = null)
            => MeasuredCharacteristic.Create(code, name, std, unit, usl, lsl);

        return
        [
            M("TOR-01", "M6 螺栓扭矩", 22.0, "Nm", 23.0, 21.0),
            M("TOR-02", "M6 螺栓角度", 180.0, "°", 185.0, 175.0),
            M("TOR-03", "M8 螺栓扭矩", 45.0, "Nm", 47.0, 43.0),
            M("HYD-01", "建压时间", 200.0, "ms", 250.0, 150.0),
            M("HYD-02", "保压压力", 180.0, "bar", 185.0, 175.0),
            M("HYD-03", "泄漏率", 0.5, "CC/hr", 0.5, null),
            M("VIS-01", "外观检查", 1.0, "-", null, null),
        ];
    }
}

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

// 检验计划 Id 可选：提供有效 Id 时复制计划特性；为空/无效时回退到内置标准 IPQC 模板。
        // 无有效计划时返回 Ulid.Empty 哨兵值，禁止生成随机幽灵 Id（会伪造不存在的计划引用）。
        var planId = Ulid.Empty;
        List<MeasuredCharacteristic> characteristics;

        if (req.Characteristics.Count > 0)
        {
            characteristics = req.Characteristics.Select(c => MeasuredCharacteristic.Create(
                c.CharacteristicCode, c.CharacteristicName, c.StandardValue, c.Unit,
                c.UpperSpecLimit, c.LowerSpecLimit)).ToList();
            if (Ulid.TryParse(req.InspectionPlanId, out var parsedPlanId))
                planId = parsedPlanId;
        }
        else
        {
            var planRepo = Resolve<IInspectionPlanRepository>();
            (planId, characteristics) = await InspectionPlanResolver.ResolveAsync(
                req.InspectionPlanId, planRepo, StandardIpqcTemplate.Create, ct);
        }

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
