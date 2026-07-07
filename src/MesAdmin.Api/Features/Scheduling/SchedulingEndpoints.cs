using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Features.Scheduling;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Scheduling;

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/plans — 排程列表
// ═══════════════════════════════════════════

public class ListSchedulesEndpoint : MesEndpointWithoutRequest<List<ScheduleResponse>>
{
    public override void Configure()
    {
        Get("/scheduling/plans");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询生产排程列表，支持按日期/设备/状态过滤");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dateFrom = Query<string?>("from");
        var dateTo = Query<string?>("to");
        var equipment = Query<string?>("equipment");
        var status = Query<string?>("status");

        var repo = Resolve<IScheduleRepository>();

        List<ProductionSchedule> schedules;

        if (equipment is not null)
        {
            DateOnly? from = dateFrom is not null ? DateOnly.Parse(dateFrom) : null;
            DateOnly? to = dateTo is not null ? DateOnly.Parse(dateTo) : null;
            schedules = await repo.GetByEquipmentAsync(equipment, from, to, ct);
        }
        else if (dateFrom is not null && dateTo is not null)
        {
            schedules = await repo.GetByDateRangeAsync(
                DateOnly.Parse(dateFrom), DateOnly.Parse(dateTo), ct);
        }
        else if (status is not null && Enum.TryParse<ScheduleStatus>(status, true, out var st))
        {
            schedules = await repo.GetByStatusAsync(st, ct);
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
            schedules = await repo.GetByDateAsync(today, ct);
        }

        Response = schedules.Select(SchedulingMapper.ToScheduleResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/plans/{id} — 排程详情
// ═══════════════════════════════════════════

public class GetScheduleEndpoint : MesEndpointWithoutRequest<ScheduleResponse>
{
    public override void Configure()
    {
        Get("/scheduling/plans/{id}");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询排程详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var scheduleId))
        {
            AddError("id", "无效的排程 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IScheduleRepository>();
        var schedule = await repo.GetByIdAsync(scheduleId, ct);
        if (schedule is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = SchedulingMapper.ToScheduleResponse(schedule);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/scheduling/plans — 创建排程
// ═══════════════════════════════════════════

public class CreateScheduleEndpoint : MesEndpoint<CreateScheduleRequest, ScheduleResponse>
{
    public override void Configure()
    {
        Post("/scheduling/plans");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "创建生产排程（自动冲突检测）");
    }

    public override async Task HandleAsync(CreateScheduleRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError("OrderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }
        if (!Enum.TryParse<ShiftType>(req.Shift, true, out var shift))
        {
            AddError("Shift", "无效的班次，支持 Morning/Afternoon/Night");
            ThrowIfAnyErrors();
        }

        var orderRepo = Resolve<IProductionOrderRepository>();
        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var plannedEnd = req.PlannedStartAt.AddMinutes(req.StandardMinutes + req.ChangeoverMinutes);

        try
        {
            var schedule = ProductionSchedule.Create(
                Ulid.NewUlid(),
                orderId,
                order.OrderNumber,
                order.ProductCode,
                order.PlannedQuantity,
                req.EquipmentCode,
                0, // station resolved from equipment
                shift,
                DateOnly.Parse(req.ScheduleDate),
                req.PlannedStartAt,
                req.StandardMinutes,
                req.ChangeoverMinutes);

            if (!string.IsNullOrWhiteSpace(req.Remarks))
                schedule.Remarks = req.Remarks.Trim();

            var repo = Resolve<IScheduleRepository>();
            await repo.AddAsync(schedule, ct);
            await repo.SaveChangesAsync(ct);

            Response = SchedulingMapper.ToScheduleResponse(schedule);
            await SendCreatedDualAsync<CreateScheduleEndpoint>(new { id = schedule.Id.ToString() }, ct);
        }
        catch (Exception ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class CreateScheduleValidator : Validator<CreateScheduleRequest>
{
    public CreateScheduleValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.EquipmentCode).NotEmpty().WithMessage("设备编码不能为空");
        RuleFor(x => x.Shift).NotEmpty().WithMessage("班次不能为空");
        RuleFor(x => x.StandardMinutes).GreaterThan(0).WithMessage("标准工时必须大于 0");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/scheduling/plans/{id}/start — 开始执行
// ═══════════════════════════════════════════

public class StartScheduleEndpoint : MesEndpointWithoutRequest<ScheduleResponse>
{
    public override void Configure()
    {
        Post("/scheduling/plans/{id}/start");
        Group<SchedulingGroup>();
        Roles(MesRoles.ShiftLeader);
        Summary(s => s.Summary = "开始执行排程");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var scheduleId))
        {
            AddError("id", "无效的排程 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IScheduleRepository>();
        var schedule = await repo.GetByIdAsync(scheduleId, ct);
        if (schedule is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            schedule.Start();
            repo.Update(schedule);
            await repo.SaveChangesAsync(ct);
            Response = SchedulingMapper.ToScheduleResponse(schedule);
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
//  POST /api/v1/scheduling/plans/{id}/complete — 完成排程
// ═══════════════════════════════════════════

public class CompleteScheduleEndpoint : MesEndpointWithoutRequest<ScheduleResponse>
{
    public override void Configure()
    {
        Post("/scheduling/plans/{id}/complete");
        Group<SchedulingGroup>();
        Roles(MesRoles.ShiftLeader, MesRoles.ProductionManager);
        Summary(s => s.Summary = "完成排程");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var scheduleId))
        {
            AddError("id", "无效的排程 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IScheduleRepository>();
        var schedule = await repo.GetByIdAsync(scheduleId, ct);
        if (schedule is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            schedule.Complete();
            repo.Update(schedule);
            await repo.SaveChangesAsync(ct);
            Response = SchedulingMapper.ToScheduleResponse(schedule);
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
//  POST /api/v1/scheduling/plans/{id}/cancel — 取消排程
// ═══════════════════════════════════════════

public class CancelScheduleEndpoint : MesEndpoint<CancelScheduleRequest, ScheduleResponse>
{
    public override void Configure()
    {
        Post("/scheduling/plans/{id}/cancel");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "取消排程");
    }

    public override async Task HandleAsync(CancelScheduleRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var scheduleId))
        {
            AddError("id", "无效的排程 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IScheduleRepository>();
        var schedule = await repo.GetByIdAsync(scheduleId, ct);
        if (schedule is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            schedule.Cancel(req.Reason);
            repo.Update(schedule);
            await repo.SaveChangesAsync(ct);
            Response = SchedulingMapper.ToScheduleResponse(schedule);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class CancelScheduleRequest
{
    public string? Reason { get; set; }
}

// ═══════════════════════════════════════════
//  POST /api/v1/scheduling/plans/{id}/reschedule — 重新排程
// ═══════════════════════════════════════════

public class RescheduleEndpoint : MesEndpoint<RescheduleRequest, ScheduleResponse>
{
    public override void Configure()
    {
        Post("/scheduling/plans/{id}/reschedule");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "重新排程（调整时间/设备）");
    }

    public override async Task HandleAsync(RescheduleRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var scheduleId))
        {
            AddError("id", "无效的排程 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IScheduleRepository>();
        var schedule = await repo.GetByIdAsync(scheduleId, ct);
        if (schedule is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            schedule.Reschedule(req.NewStartAt, req.NewEquipmentCode, req.NewChangeoverMinutes);
            repo.Update(schedule);
            await repo.SaveChangesAsync(ct);
            Response = SchedulingMapper.ToScheduleResponse(schedule);
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
//  POST /api/v1/scheduling/rush-order — 紧急插单
// ═══════════════════════════════════════════

public class InsertRushOrderEndpoint : MesEndpoint<InsertRushOrderRequest, RushOrderResponse>
{
    public override void Configure()
    {
        Post("/scheduling/rush-order");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager);
        Summary(s => s.Summary = "紧急插单（自动计算最早可排时间 + 冲突检测）");
    }

    public override async Task HandleAsync(InsertRushOrderRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(req.OrderId, out var orderId))
        {
            AddError("OrderId", "无效的工单 Id");
            ThrowIfAnyErrors();
        }
        if (!Enum.TryParse<RushOrderType>(req.RushType, true, out var rushType))
        {
            AddError("RushType", "无效的插单类型，支持 OemUrgent/QualityRework/Maintenance");
            ThrowIfAnyErrors();
        }

        var orderRepo = Resolve<IProductionOrderRepository>();
        var order = await orderRepo.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var engine = Resolve<SchedulingEngine>();
        var (schedule, conflicts) = await engine.InsertRushOrderAsync(
            orderId,
            order.OrderNumber,
            order.ProductCode,
            order.PlannedQuantity,
            req.EquipmentCode,
            0,
            req.StandardMinutes,
            req.ChangeoverMinutes,
            rushType,
            req.RushReason,
            ct);

        var repo = Resolve<IScheduleRepository>();
        await repo.AddAsync(schedule, ct);
        await repo.SaveChangesAsync(ct);

        Response = new RushOrderResponse(
            SchedulingMapper.ToScheduleResponse(schedule),
            conflicts.Select(SchedulingMapper.ToConflictResponse).ToList());

        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial record RushOrderResponse(
    ScheduleResponse Schedule,
    List<ScheduleConflictResponse> Conflicts);

public class InsertRushOrderValidator : Validator<InsertRushOrderRequest>
{
    public InsertRushOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("工单 Id 不能为空");
        RuleFor(x => x.EquipmentCode).NotEmpty().WithMessage("设备编码不能为空");
        RuleFor(x => x.RushType).NotEmpty().WithMessage("插单类型不能为空");
        RuleFor(x => x.StandardMinutes).GreaterThan(0).WithMessage("标准工时必须大于 0");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/capacity — 产能利用率
// ═══════════════════════════════════════════

public class GetCapacityUtilizationEndpoint : MesEndpointWithoutRequest<List<CapacityUtilizationResponse>>
{
    public override void Configure()
    {
        Get("/scheduling/capacity");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询设备产能利用率");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dateStr = Query<string>("date");
        var date = dateStr is not null
            ? DateOnly.Parse(dateStr)
            : DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        var engine = Resolve<SchedulingEngine>();
        var calendars = Resolve<ICapacityCalendarRepository>();
        var result = await engine.CalculateUtilizationAsync(date, ct);

        var allCalendars = await calendars.GetAllActiveAsync(ct);
        Response = allCalendars.Select(c => new CapacityUtilizationResponse(
            c.EquipmentCode,
            c.EquipmentName,
            result.GetValueOrDefault(c.EquipmentCode, 0),
            0 // count handled client-side
        )).ToList();

        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/rush-orders — 插单列表
// ═══════════════════════════════════════════

public class ListRushOrdersEndpoint : MesEndpointWithoutRequest<List<ScheduleResponse>>
{
    public override void Configure()
    {
        Get("/scheduling/rush-orders");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询紧急插单列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<IScheduleRepository>();
        var rushOrders = await repo.GetRushOrdersAsync(ct);
        Response = rushOrders.Select(SchedulingMapper.ToScheduleResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/calendars — 产能日历
// ═══════════════════════════════════════════

public class ListCalendarsEndpoint : MesEndpointWithoutRequest<List<CapacityCalendarResponse>>
{
    public override void Configure()
    {
        Get("/scheduling/calendars");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "查询产能日历配置");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<ICapacityCalendarRepository>();
        var calendars = await repo.GetAllActiveAsync(ct);
        Response = calendars.Select(SchedulingMapper.ToCalendarResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/scheduling/calendars — 创建产能日历
// ═══════════════════════════════════════════

public class CreateCalendarEndpoint : MesEndpoint<CreateCalendarRequest, CapacityCalendarResponse>
{
    public override void Configure()
    {
        Post("/scheduling/calendars");
        Group<SchedulingGroup>();
        Roles(MesRoles.EquipmentEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "创建设备产能日历");
    }

    public override async Task HandleAsync(CreateCalendarRequest req, CancellationToken ct)
    {
        var calendar = CapacityCalendar.Create(
            Ulid.NewUlid(),
            req.EquipmentCode,
            req.EquipmentName,
            req.Station,
            req.StandardChangeoverMinutes,
            req.CrossProductChangeoverMinutes);

        var repo = Resolve<ICapacityCalendarRepository>();
        await repo.AddAsync(calendar, ct);
        await repo.SaveChangesAsync(ct);

        Response = SchedulingMapper.ToCalendarResponse(calendar);
        await SendCreatedDualAsync<CreateCalendarEndpoint>(new { id = calendar.Id.ToString() }, ct);
    }
}

public class CreateCalendarValidator : Validator<CreateCalendarRequest>
{
    public CreateCalendarValidator()
    {
        RuleFor(x => x.EquipmentCode).NotEmpty().WithMessage("设备编码不能为空");
        RuleFor(x => x.EquipmentName).NotEmpty().WithMessage("设备名称不能为空");
        RuleFor(x => x.StandardChangeoverMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CrossProductChangeoverMinutes).GreaterThanOrEqualTo(0);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/scheduling/gantt-data — 甘特图数据（供 Bryntum Gantt 前端使用）
// ═══════════════════════════════════════════

public class GetGanttDataEndpoint : MesEndpointWithoutRequest<GanttDataResponse>
{
    public override void Configure()
    {
        Get("/scheduling/gantt-data");
        Group<SchedulingGroup>();
        Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "获取甘特图渲染数据（Bryntum Gantt 格式）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var dateFrom = Query<string?>("from");
        var dateTo = Query<string?>("to");

        var from = dateFrom is not null ? DateOnly.Parse(dateFrom)
            : DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        var to = dateTo is not null ? DateOnly.Parse(dateTo)
            : from.AddDays(7);

        var repo = Resolve<IScheduleRepository>();
        var calendarRepo = Resolve<ICapacityCalendarRepository>();

        var schedules = await repo.GetByDateRangeAsync(from, to, ct);
        var calendars = await calendarRepo.GetAllActiveAsync(ct);

        // 构建 Bryntum Gantt 兼容的数据格式
        var tasks = new List<GanttTaskDto>();
        var dependencies = new List<GanttDependencyDto>();

        foreach (var s in schedules.OrderBy(s => s.PlannedStartAt))
        {
            var taskId = s.Id.ToString();
            tasks.Add(new GanttTaskDto(
                Id: taskId,
                Name: $"{s.OrderNumber} ({s.ProductCode})",
                StartDate: s.PlannedStartAt.ToString("o"),
                EndDate: s.PlannedEndAt.ToString("o"),
                Duration: s.StandardMinutes / 60.0,
                DurationUnit: "h",
                PercentDone: s.Status == ScheduleStatus.Completed ? 100
                    : s.Status == ScheduleStatus.InProgress ? 50 : 0,
                EquipmentCode: s.EquipmentCode,
                AssignedTo: s.EquipmentCode,
                Cls: s.RushType != RushOrderType.None ? "rush-task" : "",
                Status: s.Status.ToString(),
                Priority: s.Priority));
        }

        var resources = calendars.Select(c => new GanttResourceDto(
            Id: c.EquipmentCode,
            Name: $"{c.EquipmentName} ({c.EquipmentCode})",
            Station: c.Station
        )).ToList();

        Response = new GanttDataResponse(tasks, dependencies, resources);
        await SendDualAsync(ct);
    }
}

[MemoryPackable]
public partial record GanttTaskDto(
    string Id,
    string Name,
    string StartDate,
    string EndDate,
    double Duration,
    string DurationUnit,
    int PercentDone,
    string EquipmentCode,
    string AssignedTo,
    string Cls,
    string Status,
    short Priority);

[MemoryPackable]
public partial record GanttDependencyDto(
    string Id,
    string From,
    string To,
    string Type);

[MemoryPackable]
public partial record GanttResourceDto(
    string Id,
    string Name,
    int Station);

[MemoryPackable]
public partial record GanttDataResponse(
    List<GanttTaskDto> Tasks,
    List<GanttDependencyDto> Dependencies,
    List<GanttResourceDto> Resources);
