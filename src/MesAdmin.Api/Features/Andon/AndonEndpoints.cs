using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Observability;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.RealTime;
using MessagePipe;

namespace MesAdmin.Api.Features.Andon;

// ═══════════════════════════════════════════
//  GET /api/v1/andon — 报警列表
// ═══════════════════════════════════════════

public class ListAndonEndpoint : MesEndpointWithoutRequest<List<AndonEventResponse>>
{
    public override void Configure()
    {
        Get("/");
        Group<AndonGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader, MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "查询 Andon 报警列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // isRequired: false —— 这些是可选过滤参数，缺省时不得触发 400（FastEndpoints Query 默认必填）
        var statusStr = Query<string?>("status", isRequired: false);
        var equipmentCode = Query<string?>("equipmentCode", isRequired: false);
        var severityStr = Query<string?>("severity", isRequired: false);
        var limit = Query<int?>("limit", isRequired: false) ?? 50;

        AndonEventStatus? status = Enum.TryParse<AndonEventStatus>(statusStr, true, out var s) ? s : null;
        AndonSeverity? severity = Enum.TryParse<AndonSeverity>(severityStr, true, out var sev) ? sev : null;

        var repo = Resolve<IAndonEventRepository>();
        var events = await repo.GetListAsync(status, equipmentCode, severity, limit, ct);
        Response = events.Select(AndonMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/andon/stats — 统计
// ═══════════════════════════════════════════

public class AndonStatsEndpoint : MesEndpointWithoutRequest<AndonStatsResponse>
{
    public override void Configure()
    {
        Get("/stats");
        Group<AndonGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "Andon 报警统计");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IAndonEventRepository>();
        var active = await repo.GetActiveAsync(ct);
        Response = new AndonStatsResponse(
            active.Count(e => e.Status == AndonEventStatus.Active || e.Status == AndonEventStatus.Acknowledged),
            active.Count(e => e.Status == AndonEventStatus.EscalatedL2),
            active.Count(e => e.Status == AndonEventStatus.EscalatedL3),
            active.Count(e => e.OccurredAt.Date == DateTimeOffset.UtcNow.Date));
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/andon/{id}/acknowledge — 确认报警
// ═══════════════════════════════════════════

public class AcknowledgeAndonEndpoint : MesEndpoint<AcknowledgeAndonRequest, AndonEventResponse>
{
    public override void Configure()
    {
        Post("/{id}/acknowledge");
        Group<AndonGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "确认 Andon 报警");
    }

    public override async Task HandleAsync(AcknowledgeAndonRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var eventId))
        {
            AddError("id", "无效的报警 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IAndonEventRepository>();
        var ev = await repo.GetByIdAsync(eventId, ct);
        if (ev is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!ev.Acknowledge(req.AcknowledgedBy))
        {
            AddError("报警状态不允许确认");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(ev, ct);

        // 发布确认消息
        var publisher = Resolve<IAsyncPublisher<AndonEventAcknowledgedMessage>>();
        await publisher.PublishAsync(new AndonEventAcknowledgedMessage(
            ev.Id.ToString(), ev.AcknowledgedBy!, ev.AcknowledgedAt!.Value), ct);
        AutoMesMetrics.RecordAndonResponseObserved(
            Math.Max(0, (ev.AcknowledgedAt!.Value - ev.OccurredAt).TotalSeconds),
            ev.Station.ToString());

        Response = AndonMapper.ToResponse(ev);
        await SendDualAsync(ct);
    }
}

public class AcknowledgeAndonValidator : Validator<AcknowledgeAndonRequest>
{
    public AcknowledgeAndonValidator()
    {
        RuleFor(x => x.AcknowledgedBy).NotEmpty().WithMessage("确认员工号不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/andon/{id}/resolve — 解决报警
// ═══════════════════════════════════════════

public class ResolveAndonEndpoint : MesEndpoint<ResolveAndonRequest, AndonEventResponse>
{
    public override void Configure()
    {
        Post("/{id}/resolve");
        Group<AndonGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "解决 Andon 报警");
    }

    public override async Task HandleAsync(ResolveAndonRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var eventId))
        {
            AddError("id", "无效的报警 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IAndonEventRepository>();
        var ev = await repo.GetByIdAsync(eventId, ct);
        if (ev is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!ev.Resolve(req.ResolvedBy, req.Resolution))
        {
            AddError("报警状态不允许解决");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(ev, ct);

        var publisher = Resolve<IAsyncPublisher<AndonEventResolvedMessage>>();
        await publisher.PublishAsync(new AndonEventResolvedMessage(
            ev.Id.ToString(), ev.ResolvedBy!, ev.Resolution!, ev.ResolvedAt!.Value), ct);
        AutoMesMetrics.RecordAndonResponseObserved(
            Math.Max(0, (ev.ResolvedAt!.Value - ev.OccurredAt).TotalSeconds),
            ev.Station.ToString());

        Response = AndonMapper.ToResponse(ev);
        await SendDualAsync(ct);
    }
}

public class ResolveAndonValidator : Validator<ResolveAndonRequest>
{
    public ResolveAndonValidator()
    {
        RuleFor(x => x.ResolvedBy).NotEmpty().WithMessage("解决员工号不能为空");
        RuleFor(x => x.Resolution).NotEmpty().WithMessage("解决措施不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/andon/{id}/close — 关闭报警
// ═══════════════════════════════════════════

public class CloseAndonEndpoint : MesEndpoint<CloseAndonRequest, AndonEventResponse>
{
    public override void Configure()
    {
        Post("/{id}/close");
        Group<AndonGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "关闭 Andon 报警");
    }

    public override async Task HandleAsync(CloseAndonRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var eventId))
        {
            AddError("id", "无效的报警 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IAndonEventRepository>();
        var ev = await repo.GetByIdAsync(eventId, ct);
        if (ev is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!ev.Close(req.CloseRemarks))
        {
            AddError("报警状态不允许关闭");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(ev, ct);

        var publisher = Resolve<IAsyncPublisher<AndonEventClosedMessage>>();
        await publisher.PublishAsync(new AndonEventClosedMessage(
            ev.Id.ToString(), ev.CloseRemarks!, ev.ClosedAt!.Value), ct);

        Response = AndonMapper.ToResponse(ev);
        await SendDualAsync(ct);
    }
}

public class CloseAndonValidator : Validator<CloseAndonRequest>
{
    public CloseAndonValidator()
    {
        RuleFor(x => x.CloseRemarks).NotEmpty().WithMessage("关闭备注不能为空");
    }
}
