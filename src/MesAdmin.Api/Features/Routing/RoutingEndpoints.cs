using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Routing;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Routing;

// ═══════════════════════════════════════════
//  GET /api/v1/routing — 工艺路线列表
// ═══════════════════════════════════════════

public class ListRoutingsEndpoint : MesEndpointWithoutRequest<List<RoutingResponse>>
{
    public override void Configure()
    {
        Get("/routing");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "查询工艺路线列表，支持按产品编码和 ECO 状态过滤");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var productCode = Query<string?>("productCode", isRequired: false);
        var ecoStatus = Query<string?>("ecoStatus", isRequired: false);
        var repo = Resolve<IRoutingRepository>();

        List<Domain.Models.Routing> routings;

        if (ecoStatus is not null && Enum.TryParse<EcoStatus>(ecoStatus, true, out var status))
        {
            routings = await repo.GetByEcoStatusAsync(status, ct);
        }
        else if (productCode is not null)
        {
            routings = await repo.GetByProductAsync(productCode, ct);
        }
        else
        {
            routings = await repo.GetAllAsync(ct);
        }

        // 如果指定了 productCode + ecoStatus 组合，客户端过滤
        if (productCode is not null && ecoStatus is not null)
        {
            routings = routings.Where(r =>
                r.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Response = routings.Select(RoutingMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/routing/active — 当前生效工艺路线
// ═══════════════════════════════════════════

public class GetActiveRoutingEndpoint : MesEndpointWithoutRequest<RoutingResponse>
{
    public override void Configure()
    {
        Get("/routing/active");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "查询指定产品的当前生效工艺路线");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // isRequired: false —— 交由下方自定义校验返回友好错误，而非 FastEndpoints 直接 400
        var productCode = Query<string?>("productCode", isRequired: false);
        if (string.IsNullOrWhiteSpace(productCode))
        {
            AddError("productCode", "产品编码不能为空");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IRoutingRepository>();
        var routing = await repo.GetActiveByProductAsync(productCode!, ct);
        if (routing is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = RoutingMapper.ToResponse(routing);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/routing/{id} — 工艺路线详情
// ═══════════════════════════════════════════

public class GetRoutingByIdEndpoint : MesEndpointWithoutRequest<RoutingResponse>
{
    public override void Configure()
    {
        Get("/routing/{id}");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "查询工艺路线详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var routingId))
        {
            AddError("id", "无效的工艺路线 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IRoutingRepository>();
        var routing = await repo.GetByIdAsync(routingId, ct);
        if (routing is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = RoutingMapper.ToResponse(routing);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/routing — 创建工艺路线
// ═══════════════════════════════════════════

public class CreateRoutingEndpoint : MesEndpoint<CreateRoutingRequest, RoutingResponse>
{
    public override void Configure()
    {
        Post("/routing");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "创建新的工艺路线（含 31 工序 × 7 站定义 + 参数模板）");
    }

    public override async Task HandleAsync(CreateRoutingRequest req, CancellationToken ct)
    {
        var operations = req.Operations.Select(o => new Domain.Models.RoutingOperation
        {
            Sequence = o.Sequence,
            Station = o.Station,
            OperationCode = o.OperationCode.Trim(),
            OperationName = o.OperationName.Trim(),
            StandardTimeSeconds = o.StandardTimeSeconds,
            FixtureCode = o.FixtureCode?.Trim(),
            FixtureName = o.FixtureName?.Trim(),
            ParameterTemplates = o.ParameterTemplates.Select(pt => new Domain.Models.ParameterTemplate
            {
                ParameterCode = pt.ParameterCode.Trim(),
                ParameterName = pt.ParameterName.Trim(),
                StandardValue = pt.StandardValue,
                UpperSpecLimit = pt.UpperSpecLimit,
                LowerSpecLimit = pt.LowerSpecLimit,
                Unit = pt.Unit.Trim(),
                EnableSpc = pt.EnableSpc,
                SpcSubgroupSize = pt.SpcSubgroupSize,
            }).ToList(),
        }).ToList();

        var routing = Domain.Models.Routing.Create(
            Ulid.NewUlid(),
            req.ProductCode,
            req.Name,
            req.Version,
            req.CreatedBy,
            operations,
            req.EcoNumber,
            req.ChangeDescription);

        var repo = Resolve<IRoutingRepository>();
        await repo.AddAsync(routing, ct);

        Response = RoutingMapper.ToResponse(routing);
        await SendCreatedDualAsync<CreateRoutingEndpoint>(new { id = routing.Id.ToString() }, ct);
    }
}

public class CreateRoutingValidator : Validator<CreateRoutingRequest>
{
    public CreateRoutingValidator()
    {
        RuleFor(x => x.ProductCode).NotEmpty().WithMessage("产品编码不能为空");
        RuleFor(x => x.Name).NotEmpty().WithMessage("工艺路线名称不能为空");
        RuleFor(x => x.Version).NotEmpty().WithMessage("版本号不能为空");
        RuleFor(x => x.CreatedBy).NotEmpty().WithMessage("创建人不能为空");
        RuleFor(x => x.Operations).NotEmpty().WithMessage("至少包含一个工序");
        RuleFor(x => x.Operations)
            .Must(ops => ops.Count == 31)
            .WithMessage("ESP 工艺路线必须包含 31 道工序")
            .When(x => x.Operations.Count > 0);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/routing/{id}/submit — 提交 ECO 审批
// ═══════════════════════════════════════════

public class SubmitRoutingEndpoint : MesEndpointWithoutRequest<RoutingResponse>
{
    public override void Configure()
    {
        Post("/routing/{id}/submit");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "提交工艺路线 ECO 审批");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var routingId))
        {
            AddError("id", "无效的工艺路线 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IRoutingRepository>();
        var routing = await repo.GetByIdAsync(routingId, ct);
        if (routing is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            routing.SubmitForApproval();
            await repo.UpdateAsync(routing, ct);
            Response = RoutingMapper.ToResponse(routing);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/routing/{id}/approve — 审批通过
// ═══════════════════════════════════════════

public class ApproveRoutingEndpoint : MesEndpoint<ApproveRoutingRequest, RoutingResponse>
{
    public override void Configure()
    {
        Post("/routing/{id}/approve");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "审批通过工艺路线 ECO");
    }

    public override async Task HandleAsync(ApproveRoutingRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var routingId))
        {
            AddError("id", "无效的工艺路线 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IRoutingRepository>();
        var routing = await repo.GetByIdAsync(routingId, ct);
        if (routing is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            routing.Approve(req.ApprovedBy);
            await repo.UpdateAsync(routing, ct);
            Response = RoutingMapper.ToResponse(routing);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class ApproveRoutingValidator : Validator<ApproveRoutingRequest>
{
    public ApproveRoutingValidator()
    {
        RuleFor(x => x.ApprovedBy).NotEmpty().WithMessage("审批人不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/routing/{id}/release — 发布生效
// ═══════════════════════════════════════════

public class ReleaseRoutingEndpoint : MesEndpointWithoutRequest<RoutingResponse>
{
    public override void Configure()
    {
        Post("/routing/{id}/release");
        Group<RoutingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "发布工艺路线（设为当前有效版本，旧版本自动失效）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var routingId))
        {
            AddError("id", "无效的工艺路线 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IRoutingRepository>();
        var routing = await repo.GetByIdAsync(routingId, ct);
        if (routing is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            // 将当前活跃版本标记为已取代
            var active = await repo.GetActiveByProductAsync(routing.ProductCode, ct);
            if (active is not null && active.Id != routing.Id)
            {
                active.Supersede();
                await repo.UpdateAsync(active, ct);
            }

            routing.Release();
            await repo.UpdateAsync(routing, ct);
            Response = RoutingMapper.ToResponse(routing);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

// ═══════════════════════════════════════════
//  T3.3 POST /api/v1/routing/verify — 防错三重校验
// ═══════════════════════════════════════════

public class VerifyOperationEndpoint : MesEndpoint<VerifyOperationRequest, VerifyOperationResponse>
{
    public override void Configure()
    {
        Post("/routing/verify");
        Group<RoutingGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "防错三重校验（操作员启动工序前验证物料→BOM→设备参数）");
    }

    public override async Task HandleAsync(VerifyOperationRequest req, CancellationToken ct)
    {
        // 解析 Ulid
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        if (!Ulid.TryParse(req.MaterialBatchId, out var batchId))
        {
            AddError("materialBatchId", "无效的物料批次 Id");
            ThrowIfAnyErrors();
        }

        var service = Resolve<TripleCheckService>();

        var parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in req.CurrentParameters)
        {
            parameters[p.ParameterCode] = p.Value;
        }

        var result = await service.ExecuteAsync(
            orderId,
            req.MaterialCode.Trim(),
            batchId,
            req.EquipmentCode.Trim(),
            parameters,
            ct);

        var checks = new List<CheckResultDto>
        {
            new(result.MaterialScanCheck.CheckName, result.MaterialScanCheck.Passed, result.MaterialScanCheck.Message),
            new(result.BomComparisonCheck.CheckName, result.BomComparisonCheck.Passed, result.BomComparisonCheck.Message),
            new(result.EquipmentParamCheck.CheckName, result.EquipmentParamCheck.Passed, result.EquipmentParamCheck.Message),
        };

        Response = new VerifyOperationResponse(result.Passed, checks);

        await SendDualAsync(ct);
    }
}

public class VerifyOperationValidator : Validator<VerifyOperationRequest>
{
    public VerifyOperationValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.MaterialBatchId).NotEmpty().WithMessage("物料批次不能为空");
        RuleFor(x => x.EquipmentCode).NotEmpty().WithMessage("设备编号不能为空");
        // 注：参数校验采用安全优先策略，校验该工站 ALL 工序的参数模板，无需 operationSequence
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/routing/{productCode}/default — ESP 默认工艺路线（种子数据）
// ═══════════════════════════════════════════

public class GetDefaultRoutingEndpoint : MesEndpointWithoutRequest<RoutingResponse>
{
    public override void Configure()
    {
        Get("/routing/{productCode}/default");
        Group<RoutingGroup>();
        AllowAnonymous();
        Summary(s => s.Summary = "获取 ESP 默认 31 工序工艺路线定义");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var productCode = Route<string>("productCode")!;
        var routing = EspDefaultRouting.CreateDefault(productCode);
        Response = RoutingMapper.ToResponse(routing);
        await SendDualAsync(ct);
    }
}
