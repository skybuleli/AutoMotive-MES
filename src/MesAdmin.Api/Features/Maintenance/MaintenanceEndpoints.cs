using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Maintenance;

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/plans — 维护计划列表
// ═══════════════════════════════════════════

public class ListPlansEndpoint : MesEndpointWithoutRequest<List<MaintenancePlanResponse>>
{
    public override void Configure()
    {
        Get("/plans");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "查询预防性维护计划列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IMaintenancePlanRepository>();
        var plans = await repo.GetAllAsync(ct);
        Response = plans.Select(MaintenanceMapper.ToPlanResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/orders — 维护工单列表
// ═══════════════════════════════════════════

public class ListOrdersEndpoint : MesEndpointWithoutRequest<List<MaintenanceOrderResponse>>
{
    public override void Configure()
    {
        Get("/orders");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询维护工单列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var equipmentCode = Query<string?>("equipmentCode", isRequired: false);
        var statusStr = Query<string?>("status", isRequired: false);
        var limit = Query<int?>("limit", isRequired: false) ?? 50;

        MaintenanceOrderStatus? status = Enum.TryParse<MaintenanceOrderStatus>(statusStr, true, out var parsed)
            ? parsed
            : null;

        var repo = Resolve<IMaintenanceWorkOrderRepository>();
        var orders = await repo.GetListAsync(equipmentCode, status, limit, ct);
        Response = orders.Select(MaintenanceMapper.ToOrderResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/orders/{id} — 工单详情
// ═══════════════════════════════════════════

public class GetOrderByIdEndpoint : MesEndpointWithoutRequest<MaintenanceOrderResponse>
{
    public override void Configure()
    {
        Get("/orders/{id}");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询维护工单详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var orderId))
        {
            AddError("id", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IMaintenanceWorkOrderRepository>();
        var order = await repo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = MaintenanceMapper.ToOrderResponse(order);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/orders/{id}/start — 开始执行
// ═══════════════════════════════════════════

public class StartOrderEndpoint : MesEndpoint<StartOrderRequest, MaintenanceOrderResponse>
{
    public override void Configure()
    {
        Post("/orders/{id}/start");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "开始执行维护工单");
    }

    public override async Task HandleAsync(StartOrderRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var orderId))
        {
            AddError("id", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IMaintenanceWorkOrderRepository>();
        var order = await repo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!order.Start(req.AssignedTo))
        {
            AddError("工单状态不允许开始执行");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(order, ct);
        Response = MaintenanceMapper.ToOrderResponse(order);
        await SendDualAsync(ct);
    }
}

public class StartOrderValidator : Validator<StartOrderRequest>
{
    public StartOrderValidator()
    {
        RuleFor(x => x.AssignedTo).NotEmpty().WithMessage("执行人工号不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/orders/{id}/complete — 完成工单
// ═══════════════════════════════════════════

public class CompleteOrderEndpoint : MesEndpoint<CompleteOrderRequest, MaintenanceOrderResponse>
{
    public override void Configure()
    {
        Post("/orders/{id}/complete");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "完成维护工单");
    }

    public override async Task HandleAsync(CompleteOrderRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var orderId))
        {
            AddError("id", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IMaintenanceWorkOrderRepository>();
        var order = await repo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!order.Complete(req.CompletedBy, req.Remarks))
        {
            AddError("工单状态不允许完成");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(order, ct);
        Response = MaintenanceMapper.ToOrderResponse(order);
        await SendDualAsync(ct);
    }
}

public class CompleteOrderValidator : Validator<CompleteOrderRequest>
{
    public CompleteOrderValidator()
    {
        RuleFor(x => x.CompletedBy).NotEmpty().WithMessage("完成人工号不能为空");
        RuleFor(x => x.Remarks).NotEmpty().WithMessage("完成备注不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/orders/{id}/cancel — 取消工单
// ═══════════════════════════════════════════

public class CancelOrderEndpoint : MesEndpoint<CancelOrderRequest, MaintenanceOrderResponse>
{
    public override void Configure()
    {
        Post("/orders/{id}/cancel");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "取消维护工单");
    }

    public override async Task HandleAsync(CancelOrderRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var orderId))
        {
            AddError("id", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IMaintenanceWorkOrderRepository>();
        var order = await repo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!order.Cancel(req.Reason))
        {
            AddError("工单状态不允许取消");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(order, ct);
        Response = MaintenanceMapper.ToOrderResponse(order);
        await SendDualAsync(ct);
    }
}

public class CancelOrderValidator : Validator<CancelOrderRequest>
{
    public CancelOrderValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("取消原因不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/plans — 创建维护计划（设备工程师用）
// ═══════════════════════════════════════════

public class CreatePlanEndpoint : MesEndpoint<CreatePlanRequest, MaintenancePlanResponse>
{
    public override void Configure()
    {
        Post("/plans");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "创建预防性维护计划");
    }

    public override async Task HandleAsync(CreatePlanRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<MaintenanceType>(req.MaintenanceType, true, out var maintenanceType))
        {
            AddError("MaintenanceType", "无效的维护类型，支持 CycleBased / TimeBased");
            ThrowIfAnyErrors();
        }

        var plan = MaintenancePlan.Create(
            Ulid.NewUlid(),
            req.EquipmentCode,
            req.EquipmentName,
            maintenanceType,
            req.ThresholdValue,
            req.TaskDescription,
            req.WorkContent);

        var repo = Resolve<IMaintenancePlanRepository>();
        await repo.AddAsync(plan, ct);

        Response = MaintenanceMapper.ToPlanResponse(plan);
        await SendCreatedDualAsync<CreatePlanEndpoint>(new { id = plan.Id.ToString() }, ct);
    }
}

public class CreatePlanValidator : Validator<CreatePlanRequest>
{
    public CreatePlanValidator()
    {
        RuleFor(x => x.EquipmentCode).NotEmpty().WithMessage("设备编码不能为空");
        RuleFor(x => x.TaskDescription).NotEmpty().WithMessage("任务描述不能为空");
        RuleFor(x => x.ThresholdValue).GreaterThan(0).WithMessage("阈值必须大于 0");
    }
}
