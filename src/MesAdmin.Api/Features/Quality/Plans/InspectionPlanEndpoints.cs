using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Quality.Plans;

// ═══════════════════════════════════════════
//  GET /api/v1/quality/plans — 检验计划列表
// ═══════════════════════════════════════════

public class ListInspectionPlansEndpoint : MesEndpointWithoutRequest<List<InspectionPlanResponse>>
{
    public override void Configure()
    {
        Get("/plans");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector);
        Summary(s => s.Summary = "查询检验计划列表（支持按阶段/产品筛选）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var stageStr = Query<string?>("stage", isRequired: false);
        var productCode = Query<string?>("productCode", isRequired: false);
        var repo = Resolve<IInspectionPlanRepository>();

        List<InspectionPlan> plans;
        if (Enum.TryParse<InspectionStage>(stageStr, true, out var stage))
        {
            plans = !string.IsNullOrWhiteSpace(productCode)
                ? await repo.GetByProductCodeAsync(productCode, stage, ct)
                : (await repo.GetEnabledAsync(ct)).Where(p => p.Stage == stage).ToList();
        }
        else
        {
            plans = await repo.GetEnabledAsync(ct);
        }

        Response = plans.Select(QualityMapper.ToPlanResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/plans — 创建检验计划
// ═══════════════════════════════════════════

public class CreateInspectionPlanEndpoint : MesEndpoint<CreateInspectionPlanRequest, InspectionPlanResponse>
{
    public override void Configure()
    {
        Post("/plans");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建检验计划（含检验特性清单）");
    }

    public override async Task HandleAsync(CreateInspectionPlanRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<InspectionStage>(req.Stage, true, out var stage))
        {
            AddError("Stage", "无效的检验阶段（Iq/Ipqc/Oqc/FirstArticle/OnlineTest）");
            ThrowIfAnyErrors();
        }
        if (req.Characteristics.Count == 0)
        {
            AddError("Characteristics", "检验特性清单不能为空");
            ThrowIfAnyErrors();
        }

        var plan = InspectionPlan.Create(
            req.PlanName, req.Version, stage,
            req.SamplingFrequency, req.SampleSize,
            req.AcceptNumber, req.RejectNumber,
            req.EffectiveDate);

        plan.ProductCode = string.IsNullOrWhiteSpace(req.ProductCode) ? null : req.ProductCode.Trim();
        plan.Station = req.Station;
        plan.AqlValue = req.AqlValue;
        plan.InspectionLevel = string.IsNullOrWhiteSpace(req.InspectionLevel) ? null : req.InspectionLevel.Trim();
        plan.EnableSpcChart = req.EnableSpcChart;
        plan.SpcSubgroupSize = req.SpcSubgroupSize > 0 ? req.SpcSubgroupSize : 5;
        plan.ExpirationDate = req.ExpirationDate;

        foreach (var c in req.Characteristics)
        {
            var pc = c.Type == "Attribute"
                ? PlanCharacteristic.CreateAttribute(c.CharacteristicCode, c.CharacteristicName, c.Unit)
                : PlanCharacteristic.CreateVariable(
                    c.CharacteristicCode, c.CharacteristicName, c.StandardValue, c.Unit,
                    c.UpperSpecLimit, c.LowerSpecLimit, c.IsCritical, c.EnableSpc);
            plan.AddCharacteristic(pc);
        }

        var repo = Resolve<IInspectionPlanRepository>();
        await repo.AddAsync(plan, ct);
        await repo.SaveChangesAsync(ct);

        Response = QualityMapper.ToPlanResponse(plan);
        await SendDualAsync(ct);
    }
}

public class CreateInspectionPlanValidator : Validator<CreateInspectionPlanRequest>
{
    public CreateInspectionPlanValidator()
    {
        RuleFor(x => x.PlanName).NotEmpty().WithMessage("计划名称不能为空");
        RuleFor(x => x.Version).NotEmpty().WithMessage("版本不能为空");
        RuleFor(x => x.SamplingFrequency).NotEmpty().WithMessage("抽样频率不能为空");
        RuleFor(x => x.SampleSize).GreaterThan(0).WithMessage("抽样数量必须大于 0");
    }
}

[MemoryPackable]
public partial class CreateInspectionPlanRequest
{
    public string PlanName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Stage { get; set; } = "Iq";
    public string? ProductCode { get; set; }
    public int? Station { get; set; }
    public string SamplingFrequency { get; set; } = "每批抽5";
    public int SampleSize { get; set; } = 5;
    public double? AqlValue { get; set; }
    public string? InspectionLevel { get; set; }
    public int AcceptNumber { get; set; }
    public int RejectNumber { get; set; } = 1;
    public bool EnableSpcChart { get; set; }
    public int SpcSubgroupSize { get; set; } = 5;
    public DateTimeOffset EffectiveDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpirationDate { get; set; }
    public List<CreatePlanCharacteristicRequest> Characteristics { get; set; } = [];
}

[MemoryPackable]
public partial class CreatePlanCharacteristicRequest
{
    public string CharacteristicCode { get; set; } = string.Empty;
    public string CharacteristicName { get; set; } = string.Empty;
    /// <summary>Variable（计量型）/ Attribute（计数型）</summary>
    public string Type { get; set; } = "Variable";
    public double StandardValue { get; set; }
    public double? UpperSpecLimit { get; set; }
    public double? LowerSpecLimit { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public bool EnableSpc { get; set; }
}

// ═══════════════════════════════════════════
//  POST /api/v1/quality/plans/{id}/toggle — 启用/停用
// ═══════════════════════════════════════════

public class ToggleInspectionPlanEndpoint : MesEndpointWithoutRequest<InspectionPlanResponse>
{
    public override void Configure()
    {
        Post("/plans/{id}/toggle");
        Group<QualityGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "启用/停用检验计划");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var planId))
        {
            AddError("id", "无效的计划 Id");
            ThrowIfAnyErrors();
        }

        // 注意：用跟踪查询加载——Characteristics 是 JSON owned collection，
        // AsNoTracking + Update() 会触发 EF Core '__synthesizedOrdinal' shadow 键异常。
        var repo = Resolve<IInspectionPlanRepository>();
        var plan = await repo.GetByIdTrackedAsync(planId, ct);
        if (plan is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        plan.IsEnabled = !plan.IsEnabled;
        await repo.SaveChangesAsync(ct);

        Response = QualityMapper.ToPlanResponse(plan);
        await SendDualAsync(ct);
    }
}
