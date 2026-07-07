using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Maintenance;

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/spare-parts — 备件列表
// ═══════════════════════════════════════════

public class ListSparePartsEndpoint : MesEndpointWithoutRequest<List<SparePartResponse>>
{
    public override void Configure()
    {
        Get("/spare-parts");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.WarehouseClerk, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询备件列表，支持低库存过滤");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var filter = Query<string?>("filter");
        var repo = Resolve<ISparePartRepository>();

        List<SparePart> parts = filter switch
        {
            "low-stock" => await repo.GetLowStockAsync(ct),
            "needs-restock" => await repo.GetNeedsRestockAsync(ct),
            _ => await repo.GetAllAsync(ct),
        };

        Response = parts.Select(SparePartMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/spare-parts/{id} — 备件详情
// ═══════════════════════════════════════════

public class GetSparePartEndpoint : MesEndpointWithoutRequest<SparePartResponse>
{
    public override void Configure()
    {
        Get("/spare-parts/{id}");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "查询备件详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var partId))
        {
            AddError("id", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISparePartRepository>();
        var part = await repo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = SparePartMapper.ToResponse(part);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/spare-parts — 创建备件
// ═══════════════════════════════════════════

public class CreateSparePartEndpoint : MesEndpoint<CreateSparePartRequest, SparePartResponse>
{
    public override void Configure()
    {
        Post("/spare-parts");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "创建备件主数据");
    }

    public override async Task HandleAsync(CreateSparePartRequest req, CancellationToken ct)
    {
        var part = SparePart.Create(
            Ulid.NewUlid(),
            req.MaterialCode,
            req.MaterialName,
            req.Specification,
            req.Unit,
            req.SafetyStock,
            req.MinimumStock,
            req.EquipmentCode,
            req.Remarks);

        var repo = Resolve<ISparePartRepository>();
        await repo.AddAsync(part, ct);

        Response = SparePartMapper.ToResponse(part);
        await SendCreatedDualAsync<CreateSparePartEndpoint>(new { id = part.Id.ToString() }, ct);
    }
}

public class CreateSparePartValidator : Validator<CreateSparePartRequest>
{
    public CreateSparePartValidator()
    {
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.MaterialName).NotEmpty().WithMessage("物料名称不能为空");
        RuleFor(x => x.Unit).NotEmpty().WithMessage("计量单位不能为空");
        RuleFor(x => x.SafetyStock).GreaterThanOrEqualTo(0).WithMessage("安全库存不能为负");
        RuleFor(x => x.MinimumStock).GreaterThanOrEqualTo(0).WithMessage("最低库存不能为负");
    }
}

// ═══════════════════════════════════════════
//  PUT /api/v1/maintenance/spare-parts/{id}/stock — 更新库存
// ═══════════════════════════════════════════

public class UpdateStockEndpoint : MesEndpoint<UpdateStockRequest, SparePartResponse>
{
    public override void Configure()
    {
        Put("/spare-parts/{id}/stock");
        Group<MaintenanceGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "更新备件库存数量（盘点）");
    }

    public override async Task HandleAsync(UpdateStockRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var partId))
        {
            AddError("id", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISparePartRepository>();
        var part = await repo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        part.UpdateStock(req.NewQuantity);
        await repo.UpdateAsync(part, ct);

        Response = SparePartMapper.ToResponse(part);
        await SendDualAsync(ct);
    }
}

public class UpdateStockValidator : Validator<UpdateStockRequest>
{
    public UpdateStockValidator()
    {
        RuleFor(x => x.NewQuantity).GreaterThanOrEqualTo(0).WithMessage("库存数量不能为负");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/spare-parts/{id}/restock — 补货入库
// ═══════════════════════════════════════════

public class RestockSparePartEndpoint : MesEndpoint<RestockRequest, SparePartResponse>
{
    public override void Configure()
    {
        Post("/spare-parts/{id}/restock");
        Group<MaintenanceGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "备件补货入库");
    }

    public override async Task HandleAsync(RestockRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var partId))
        {
            AddError("id", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISparePartRepository>();
        var part = await repo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        part.Restock(req.Quantity);
        await repo.UpdateAsync(part, ct);

        Response = SparePartMapper.ToResponse(part);
        await SendDualAsync(ct);
    }
}

public class RestockValidator : Validator<RestockRequest>
{
    public RestockValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("补货数量必须大于 0");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/orders/{orderId}/spare-parts — 消耗备件
// ═══════════════════════════════════════════

public class ConsumeSparePartEndpoint : MesEndpoint<ConsumeSparePartRequest, SparePartUsageResponse>
{
    public override void Configure()
    {
        Post("/orders/{orderId}/spare-parts");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "维护工单消耗备件（自动扣减库存）");
    }

    public override async Task HandleAsync(ConsumeSparePartRequest req, CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }
        if (!Ulid.TryParse(req.SparePartId, out var partId))
        {
            AddError("SparePartId", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var spareRepo = Resolve<ISparePartRepository>();
        var orderRepo = Resolve<IMaintenanceWorkOrderRepository>();

        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        if (order.Status != MaintenanceOrderStatus.InProgress && order.Status != MaintenanceOrderStatus.Completed)
        {
            AddError("工单未在执行或已完成状态，不能消耗备件");
            ThrowIfAnyErrors();
        }

        var part = await spareRepo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            AddError("备件不存在");
            ThrowIfAnyErrors();
        }

        if (!part!.Consume(req.Quantity))
        {
            AddError($"备件库存不足，当前库存 {part.CurrentQuantity}，需求 {req.Quantity}");
            ThrowIfAnyErrors();
        }

        await spareRepo.UpdateAsync(part, ct);

        var usage = SparePartUsage.Create(
            Ulid.NewUlid(),
            partId,
            orderId,
            req.Quantity,
            req.UnitPrice,
            req.Remarks);

        var usageRepo = Resolve<ISparePartUsageRepository>();
        await usageRepo.AddAsync(usage, ct);

        Response = SparePartMapper.ToUsageResponse(usage);
        await SendDualAsync(ct);
    }
}

public class ConsumeSparePartValidator : Validator<ConsumeSparePartRequest>
{
    public ConsumeSparePartValidator()
    {
        RuleFor(x => x.SparePartId).NotEmpty().WithMessage("备件 Id 不能为空");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("消耗数量必须大于 0");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/orders/{orderId}/spare-parts — 工单备件使用列表
// ═══════════════════════════════════════════

public class ListSparePartUsagesEndpoint : MesEndpointWithoutRequest<List<SparePartUsageResponse>>
{
    public override void Configure()
    {
        Get("/orders/{orderId}/spare-parts");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询维护工单的备件使用记录");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var orderIdStr = Route<string>("orderId")!;
        if (!Ulid.TryParse(orderIdStr, out var orderId))
        {
            AddError("orderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }

        var usageRepo = Resolve<ISparePartUsageRepository>();
        var usages = await usageRepo.GetByWorkOrderAsync(orderId, ct);
        Response = usages.Select(SparePartMapper.ToUsageResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/spare-parts/{id}/usages — 备件使用历史
// ═══════════════════════════════════════════

public class ListSparePartHistoryEndpoint : MesEndpointWithoutRequest<List<SparePartUsageResponse>>
{
    public override void Configure()
    {
        Get("/spare-parts/{id}/usages");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询备件的使用历史记录");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var partId))
        {
            AddError("id", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var usageRepo = Resolve<ISparePartUsageRepository>();
        var usages = await usageRepo.GetBySparePartAsync(partId, ct);
        Response = usages.Select(SparePartMapper.ToUsageResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/spare-parts/{id}/check-stock — 库存检查+采购申请
// ═══════════════════════════════════════════

public class CheckStockEndpoint : MesEndpointWithoutRequest<StockCheckResponse>
{
    public override void Configure()
    {
        Post("/spare-parts/{id}/check-stock");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "检查备件库存，不足时自动生成采购申请");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var partId))
        {
            AddError("id", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var spareRepo = Resolve<ISparePartRepository>();
        var purchaseRepo = Resolve<IPurchaseRequestRepository>();
        var part = await spareRepo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        PurchaseRequestResponse? existingPr = null;

        if (part.NeedsPurchaseRequest)
        {
            var pendingRequests = await purchaseRepo.GetPendingBySparePartAsync(partId, ct);
            if (pendingRequests.Count == 0)
            {
                var pr = PurchaseRequest.Create(
                    Ulid.NewUlid(),
                    partId,
                    part.SuggestedPurchaseQuantity,
                    $"库存不足（当前 {part.CurrentQuantity}，阈值 {part.MinimumStock}），自动补货",
                    "system");
                await purchaseRepo.AddAsync(pr, ct);
                existingPr = SparePartMapper.ToPurchaseResponse(pr);
            }
            else
            {
                existingPr = SparePartMapper.ToPurchaseResponse(pendingRequests[0]);
            }
        }

        Response = new StockCheckResponse(
            part.Id.ToString(),
            part.MaterialCode,
            part.MaterialName,
            part.CurrentQuantity,
            part.SafetyStock,
            part.MinimumStock,
            part.GetStockLevel().ToString(),
            part.SuggestedPurchaseQuantity,
            existingPr);

        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/maintenance/purchase-requests — 采购申请列表
// ═══════════════════════════════════════════

public class ListPurchaseRequestsEndpoint : MesEndpointWithoutRequest<List<PurchaseRequestResponse>>
{
    public override void Configure()
    {
        Get("/purchase-requests");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询采购申请列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var status = Query<string?>("status");
        var repo = Resolve<IPurchaseRequestRepository>();
        var requests = await repo.GetListAsync(status, ct: ct);
        Response = requests.Select(SparePartMapper.ToPurchaseResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/purchase-requests — 手动创建采购申请
// ═══════════════════════════════════════════

public class CreatePurchaseRequestEndpoint : MesEndpoint<CreatePurchaseRequestRequest, PurchaseRequestResponse>
{
    public override void Configure()
    {
        Post("/purchase-requests");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "手动创建采购申请");
    }

    public override async Task HandleAsync(CreatePurchaseRequestRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.SparePartId, out var partId))
        {
            AddError("SparePartId", "无效的备件 Id");
            ThrowIfAnyErrors();
        }

        var spareRepo = Resolve<ISparePartRepository>();
        var part = await spareRepo.GetByIdAsync(partId, ct);
        if (part is null)
        {
            AddError("备件不存在");
            ThrowIfAnyErrors();
        }

        var purchaseRepo = Resolve<IPurchaseRequestRepository>();
        var pending = await purchaseRepo.GetPendingBySparePartAsync(partId, ct);
        if (pending.Count > 0)
        {
            AddError($"该备件已有未完成的采购申请 (PR-{pending[0].RequestNumber})");
            ThrowIfAnyErrors();
        }

        var quantity = req.Quantity ?? part!.SuggestedPurchaseQuantity;
        var pr = PurchaseRequest.Create(
            Ulid.NewUlid(),
            partId,
            quantity,
            req.Reason,
            "manual");
        await purchaseRepo.AddAsync(pr, ct);

        Response = SparePartMapper.ToPurchaseResponse(pr);
        await SendCreatedDualAsync<CreatePurchaseRequestEndpoint>(new { id = pr.Id.ToString() }, ct);
    }
}

public class CreatePurchaseRequestValidator : Validator<CreatePurchaseRequestRequest>
{
    public CreatePurchaseRequestValidator()
    {
        RuleFor(x => x.SparePartId).NotEmpty().WithMessage("备件 Id 不能为空");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("申请原因不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/purchase-requests/{id}/approve — 审批采购申请
// ═══════════════════════════════════════════

public class ApprovePurchaseRequestEndpoint : MesEndpoint<ApprovePurchaseRequestRequest, PurchaseRequestResponse>
{
    public override void Configure()
    {
        Post("/purchase-requests/{id}/approve");
        Group<MaintenanceGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "审批采购申请");
    }

    public override async Task HandleAsync(ApprovePurchaseRequestRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var prId))
        {
            AddError("id", "无效的采购申请 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPurchaseRequestRepository>();
        var pr = await repo.GetByIdAsync(prId, ct);
        if (pr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!pr.Approve(req.ApprovedBy))
        {
            AddError($"采购申请状态不允许审批（当前状态: {pr.Status}）");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(pr, ct);

        Response = SparePartMapper.ToPurchaseResponse(pr);
        await SendDualAsync(ct);
    }
}

public class ApprovePurchaseRequestValidator : Validator<ApprovePurchaseRequestRequest>
{
    public ApprovePurchaseRequestValidator()
    {
        RuleFor(x => x.ApprovedBy).NotEmpty().WithMessage("审批人不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/maintenance/purchase-requests/{id}/cancel — 取消采购申请
// ═══════════════════════════════════════════

public class CancelPurchaseRequestEndpoint : MesEndpoint<CancelOrderRequest, PurchaseRequestResponse>
{
    public override void Configure()
    {
        Post("/purchase-requests/{id}/cancel");
        Group<MaintenanceGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "取消采购申请");
    }

    public override async Task HandleAsync(CancelOrderRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var prId))
        {
            AddError("id", "无效的采购申请 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPurchaseRequestRepository>();
        var pr = await repo.GetByIdAsync(prId, ct);
        if (pr is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!pr.Cancel(req.Reason))
        {
            AddError($"采购申请状态不允许取消（当前状态: {pr.Status}）");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(pr, ct);

        Response = SparePartMapper.ToPurchaseResponse(pr);
        await SendDualAsync(ct);
    }
}

// CancelOrderRequest 的验证器已在 MaintenanceEndpoints.cs 中定义 (CancelOrderValidator)
// FastEndpoints 不允许同一 DTO 有多个验证器，此处不再重复定义
