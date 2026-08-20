using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Application.Features.Quality;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Iqc;

/// <summary>
/// 内置标准 IQC 来料检验模板（检验计划表为空时的回退特性集）。
/// 覆盖来料材质/尺寸/外观/标识等关键检验项，规格界限可满足 AQL 抽样判定。
/// </summary>
internal static class StandardIqcTemplate
{
    public static List<MeasuredCharacteristic> Create()
    {
        static MeasuredCharacteristic M(string code, string name, double std, string unit, double? usl = null, double? lsl = null)
            => MeasuredCharacteristic.Create(code, name, std, unit, usl, lsl);

        return
        [
            M("DIM-01", "外形尺寸长", 120.0, "mm", 120.5, 119.5),
            M("DIM-02", "外形尺寸宽", 80.0, "mm", 80.5, 79.5),
            M("DIM-03", "安装孔直径", 12.0, "mm", 12.05, 11.95),
            M("TOR-01", "紧固扭矩", 22.0, "Nm", 23.0, 21.0),
            M("MAT-01", "材质硬度", 85.0, "HRB", 90.0, 80.0),
            M("VIS-01", "外观检查", 1.0, "-", null, null),
            M("VIS-02", "标识/标签完整性", 1.0, "-", null, null),
            M("PKG-01", "包装与防护", 1.0, "-", null, null),
        ];
    }
}

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
        // 检验计划 Id 可选：提供有效 Id 时复制计划特性；为空/无效时回退到内置标准 IQC 模板，
        // 保证「创建 → 录入实测值 → 完成判定」全流程可用（计划表为空也能跑通）。
        // 无有效计划时返回 Ulid.Empty 哨兵值，禁止生成随机幽灵 Id（会伪造不存在的计划引用）。
        var planRepo = Resolve<IInspectionPlanRepository>();
        var (planId, characteristics) = await InspectionPlanResolver.ResolveAsync(
            req.InspectionPlanId, planRepo, StandardIqcTemplate.Create, ct);

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
            req.AqlScheme,
            characteristics).ExecuteAsync(ct);

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
