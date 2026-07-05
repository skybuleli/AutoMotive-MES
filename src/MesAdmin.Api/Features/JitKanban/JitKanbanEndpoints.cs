using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.JitKanban;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.JitKanban;

public class JitKanbanGroup : Group
{
    public JitKanbanGroup() => Configure("api/v1/jit-kanban", ep => { });
}

// ═══════════════════════════════════════
// GET /api/v1/jit-kanban/pending
// ═══════════════════════════════════════

public class ListPendingJitSignalsEndpoint : MesEndpointWithoutRequest<List<JitSignalResponse>>
{
    public override void Configure()
    {
        Get("/pending");
        Group<JitKanbanGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "待处理 JIT 拉动信号列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var results = await new ListPendingJitSignalsQuery().ExecuteAsync(ct);
        Response = results.Select(JitSignalMapper.MapToResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════
// POST /api/v1/jit-kanban/{signalId}/deliver
// ═══════════════════════════════════════

public class ConfirmDeliveryEndpoint : MesEndpoint<ConfirmDeliveryRequest, JitSignalResponse>
{
    public override void Configure()
    {
        Post("/{signalId}/deliver");
        Group<JitKanbanGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "JIT 拉动送达确认（仓库 PDA 扫码）");
    }

    public override async Task HandleAsync(ConfirmDeliveryRequest req, CancellationToken ct)
    {
        var signalIdStr = Route<string>("signalId")!;
        if (!Ulid.TryParse(signalIdStr, out var id))
        {
            AddError("signalId", "无效的信号 Id");
            ThrowIfAnyErrors();
        }

        var result = await new ConfirmDeliveryCommand(id, req.DeliveredBy).ExecuteAsync(ct);
        Response = JitSignalMapper.MapToResponse(result);
        await SendDualAsync(ct);
    }
}

public partial record ConfirmDeliveryRequest
{
    public string DeliveredBy { get; set; } = string.Empty;
}

public class ConfirmDeliveryValidator : Validator<ConfirmDeliveryRequest>
{
    public ConfirmDeliveryValidator()
    {
        RuleFor(x => x.DeliveredBy).NotEmpty().WithMessage("送达确认操作员不能为空");
    }
}

// ═══════════════════════════════════════
// POST /api/v1/jit-kanban/{signalId}/cancel
// ═══════════════════════════════════════

public class CancelJitSignalEndpoint : MesEndpoint<CancelJitSignalRequest, JitSignalResponse>
{
    public override void Configure()
    {
        Post("/{signalId}/cancel");
        Group<JitKanbanGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "取消 JIT 拉动信号");
    }

    public override async Task HandleAsync(CancelJitSignalRequest req, CancellationToken ct)
    {
        var signalIdStr = Route<string>("signalId")!;
        if (!Ulid.TryParse(signalIdStr, out var id))
        {
            AddError("signalId", "无效的信号 Id");
            ThrowIfAnyErrors();
        }

        var result = await new CancelJitSignalCommand(id, req.Reason).ExecuteAsync(ct);
        Response = JitSignalMapper.MapToResponse(result);
        await SendDualAsync(ct);
    }
}

public partial record CancelJitSignalRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class CancelJitSignalValidator : Validator<CancelJitSignalRequest>
{
    public CancelJitSignalValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("取消原因不能为空");
    }
}

// ═══════════════════════════════════════
// POST /api/v1/jit-kanban/create (空料箱扫码入口)
// ═══════════════════════════════════════

public class CreateJitSignalEndpoint : MesEndpoint<CreateJitSignalRequest, JitSignalResponse>
{
    public override void Configure()
    {
        Post("/create");
        Group<JitKanbanGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "创建 JIT 拉动信号（空料箱扫码/手动叫料）");
    }

    public override async Task HandleAsync(CreateJitSignalRequest req, CancellationToken ct)
    {
        Ulid? orderId = null;
        if (!string.IsNullOrWhiteSpace(req.OrderId) && Ulid.TryParse(req.OrderId, out var parsed))
            orderId = parsed;

        var result = await new CreateJitSignalCommand(
            orderId, req.MaterialCode, req.MaterialName,
            req.ShortageQuantity, req.Unit, req.TargetStation, req.CreatedBy).ExecuteAsync(ct);

        Response = JitSignalMapper.MapToResponse(result);
        await SendCreatedDualAsync<ListPendingJitSignalsEndpoint>(new { }, ct);
    }
}

public partial record CreateJitSignalRequest
{
    public string? OrderId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public double ShortageQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? TargetStation { get; set; }
    public string? CreatedBy { get; set; }
}

public class CreateJitSignalValidator : Validator<CreateJitSignalRequest>
{
    public CreateJitSignalValidator()
    {
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.MaterialName).NotEmpty().WithMessage("物料名称不能为空");
        RuleFor(x => x.ShortageQuantity).GreaterThan(0).WithMessage("数量必须大于 0");
        RuleFor(x => x.Unit).NotEmpty().WithMessage("单位不能为空");
    }
}

// ═══════════════════════════════════════
// GET /api/v1/jit-kanban/history
// ═══════════════════════════════════════

public class ListJitHistoryEndpoint : MesEndpointWithoutRequest<JitHistoryResponse>
{
    public override void Configure()
    {
        Get("/history");
        Group<JitKanbanGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.WarehouseClerk);
        Summary(s => s.Summary = "JIT 拉动历史记录（分页）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var page = Query<int?>("page") ?? 1;
        var size = Query<int?>("size") ?? 20;

        var (items, total) = await new ListAllJitSignalsQuery(page, size).ExecuteAsync(ct);
        Response = new JitHistoryResponse(items.Select(JitSignalMapper.MapToResponse).ToList(), total);
        await SendDualAsync(ct);
    }
}

[MemoryPack.MemoryPackable]
public partial record JitHistoryResponse(
    List<JitSignalResponse> Items,
    int Total);

// ═══════════════════════════════════════
// 响应 DTO
// ═══════════════════════════════════════

[MemoryPack.MemoryPackable]
public partial record JitSignalResponse(
    string Id,
    string MaterialCode,
    string MaterialName,
    double ShortageQuantity,
    string Unit,
    string TargetStation,
    string OrderNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    string? DeliveredBy,
    string? Remarks);

internal static class JitSignalMapper
{
    public static JitSignalResponse MapToResponse(Application.Features.JitKanban.JitSignalDto dto) => new(
        dto.Id, dto.MaterialCode, dto.MaterialName, dto.ShortageQuantity,
        dto.Unit, dto.TargetStation, dto.OrderNumber, dto.Status,
        dto.CreatedAt, dto.DeliveredAt, dto.DeliveredBy, dto.Remarks);
}
