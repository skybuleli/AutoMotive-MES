using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Gauges;

// ═══════════════════════════════════════════
//  GET /api/v1/gauges — 台账列表
// ═══════════════════════════════════════════

public class ListGaugesEndpoint : MesEndpointWithoutRequest<List<GaugeResponse>>
{
    public override void Configure()
    {
        Get("/");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer,
              MesRoles.ProductionManager, MesRoles.Inspector, MesRoles.Technician,
              MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询计量器具台账（可按状态过滤：InService/DueSoon/Overdue/Scrapped）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var statusStr = Query<string?>("status", isRequired: false);
        GaugeStatus? status = Enum.TryParse<GaugeStatus>(statusStr, true, out var parsed) ? parsed : null;

        var repo = Resolve<IGaugeRepository>();
        var gauges = await repo.GetAllAsync(status, ct);

        // 返回前按当前时间刷新展示状态（后台服务每日落库，此处兜底实时性）
        var now = DateTimeOffset.UtcNow;
        Response = gauges
            .Select(g => { g.RefreshStatus(now); return GaugeMapper.ToResponse(g); })
            .OrderBy(r => r.DaysToDue)
            .ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/gauges — 建账
// ═══════════════════════════════════════════

public class CreateGaugeEndpoint : MesEndpoint<CreateGaugeRequest, GaugeResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "新建计量器具台账");
    }

    public override async Task HandleAsync(CreateGaugeRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<GaugeType>(req.Type, true, out var type))
        {
            AddError("Type", "无效的量具类型，支持 TorqueWrench / Caliper / PressureGauge / Multimeter / Other");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IGaugeRepository>();

        // 器具编号唯一约束前置校验（DB 亦有唯一索引兜底）
        if (await repo.GetByNumberAsync(req.GaugeNumber.Trim(), ct) is not null)
        {
            AddError("GaugeNumber", $"器具编号 {req.GaugeNumber} 已存在");
            ThrowIfAnyErrors();
        }

        var gauge = Gauge.Create(
            Ulid.NewUlid(),
            req.GaugeNumber,
            req.Name,
            type,
            req.RangeSpec,
            req.ResolutionSpec,
            req.AccuracyClass,
            req.CalibrationCycleDays,
            req.LastCalibratedAt,
            req.StorageLocation,
            req.Remarks);

        await repo.AddAsync(gauge, ct);

        Response = GaugeMapper.ToResponse(gauge);
        await SendCreatedDualAsync<ListGaugesEndpoint>(new { }, ct);
    }
}

public class CreateGaugeValidator : Validator<CreateGaugeRequest>
{
    public CreateGaugeValidator()
    {
        RuleFor(x => x.GaugeNumber).NotEmpty().WithMessage("器具编号不能为空")
            .MaximumLength(32).WithMessage("器具编号不能超过 32 字符");
        RuleFor(x => x.Name).NotEmpty().WithMessage("器具名称不能为空");
        RuleFor(x => x.CalibrationCycleDays).GreaterThan(0).WithMessage("校准周期必须大于 0 天");
        RuleFor(x => x.LastCalibratedAt).NotEqual(default(DateTimeOffset)).WithMessage("最近校准时间不能为空");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/gauges/{id} — 器具详情
// ═══════════════════════════════════════════

public class GetGaugeByIdEndpoint : MesEndpointWithoutRequest<GaugeResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer,
              MesRoles.ProductionManager, MesRoles.Inspector, MesRoles.Technician,
              MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询量具详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!TryParseId(out var id))
            return;

        var repo = Resolve<IGaugeRepository>();
        var gauge = await repo.GetByIdAsync(id, ct);
        if (gauge is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        gauge.RefreshStatus(DateTimeOffset.UtcNow);
        Response = GaugeMapper.ToResponse(gauge);
        await SendDualAsync(ct);
    }

    private bool TryParseId(out Ulid id)
    {
        if (Ulid.TryParse(Route<string>("id")!, out id)) return true;
        AddError("id", "无效的量具 Id");
        ThrowIfAnyErrors();
        id = Ulid.Empty;
        return false;
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/gauges/{id}/records — 校准历史
// ═══════════════════════════════════════════

public class ListCalibrationRecordsEndpoint : MesEndpointWithoutRequest<List<CalibrationRecordResponse>>
{
    public override void Configure()
    {
        Get("/{id}/records");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer,
              MesRoles.ProductionManager, MesRoles.Inspector, MesRoles.Technician,
              MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询量具校准历史");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id")!, out var id))
        {
            AddError("id", "无效的量具 Id");
            ThrowIfAnyErrors();
        }

        var recordRepo = Resolve<ICalibrationRecordRepository>();
        var records = await recordRepo.GetByGaugeIdAsync(id, ct);
        Response = records.Select(GaugeMapper.ToRecordResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/gauges/{id}/calibrations — 登记校准
// ═══════════════════════════════════════════

public class RecordCalibrationEndpoint : MesEndpoint<RecordCalibrationRequest, GaugeResponse>
{
    public override void Configure()
    {
        Post("/{id}/calibrations");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "登记一次校准，重算到期日并刷新状态");
    }

    public override async Task HandleAsync(RecordCalibrationRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id")!, out var id))
        {
            AddError("id", "无效的量具 Id");
            ThrowIfAnyErrors();
        }

        if (!Enum.TryParse<CalibrationResult>(req.Result, true, out var result))
        {
            AddError("Result", "无效的校准结论，支持 Pass / Fail / Adjusted");
            ThrowIfAnyErrors();
        }

        var gaugeRepo = Resolve<IGaugeRepository>();
        var recordRepo = Resolve<ICalibrationRecordRepository>();

        var gauge = await gaugeRepo.GetByIdAsync(id, ct);
        if (gauge is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var nextDue = req.NextDueAfter ?? req.CalibratedAt.AddDays(gauge.CalibrationCycleDays);

        // 领域状态机：已报废量具拒绝登记；同时落一条校准记录留审计
        if (!gauge.RecordCalibration(req.CalibratedAt, req.NextDueAfter))
        {
            AddError("该量具已报废，禁止登记校准");
            ThrowIfAnyErrors();
        }

        var record = CalibrationRecord.Create(
            Ulid.NewUlid(),
            gauge.Id,
            req.CalibratedAt,
            result,
            req.CertificateNo,
            req.OperatorId,
            nextDue,
            req.Remarks);

        await recordRepo.AddAsync(record, ct);
        await gaugeRepo.UpdateAsync(gauge, ct);

        Response = GaugeMapper.ToResponse(gauge);
        await SendDualAsync(ct);
    }
}

public class RecordCalibrationValidator : Validator<RecordCalibrationRequest>
{
    public RecordCalibrationValidator()
    {
        RuleFor(x => x.CalibratedAt).NotEqual(default(DateTimeOffset)).WithMessage("校准日期不能为空");
        RuleFor(x => x.CertificateNo).NotEmpty().WithMessage("校准证书编号不能为空");
        RuleFor(x => x.OperatorId).NotEmpty().WithMessage("执行人工号不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/gauges/{id}/scrap — 报废（终态）
// ═══════════════════════════════════════════

public class ScrapGaugeEndpoint : MesEndpoint<ScrapGaugeRequest, GaugeResponse>
{
    public override void Configure()
    {
        Post("/{id}/scrap");
        Group<GaugeGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "报废量具（终态，原因追加至备注）");
    }

    public override async Task HandleAsync(ScrapGaugeRequest req, CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id")!, out var id))
        {
            AddError("id", "无效的量具 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IGaugeRepository>();
        var gauge = await repo.GetByIdAsync(id, ct);
        if (gauge is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!gauge.Scrap(req.Reason))
        {
            AddError("该量具已是报废状态");
            ThrowIfAnyErrors();
        }

        await repo.UpdateAsync(gauge, ct);

        Response = GaugeMapper.ToResponse(gauge);
        await SendDualAsync(ct);
    }
}

public class ScrapGaugeValidator : Validator<ScrapGaugeRequest>
{
    public ScrapGaugeValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("报废原因不能为空");
    }
}
