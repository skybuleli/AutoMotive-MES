using System.Text.Json;
using FastEndpoints;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.HydraulicTest;

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/hydraulic-test/latest — 最新测试结果
// ═══════════════════════════════════════════════════════════

public class GetLatestHydraulicTestEndpoint : EndpointWithoutRequest
{
    private readonly IHydraulicTestRepository _repo;

    public GetLatestHydraulicTestEndpoint(IHydraulicTestRepository repo) => _repo = repo;

    public override void Configure()
    {
        Get("/hydraulic-test/latest");
        Group<HydraulicTestGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector, MesRoles.Technician);
        Summary(s => s.Summary = "获取液压测试台最新测试结果");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await _repo.GetLatestByEquipmentAsync("EQ-HYD-01", ct);
        if (result is null)
        {
            HttpContext.Response.StatusCode = 404;
            await HttpContext.Response.WriteAsync("{\"error\":\"未找到测试记录\"}", ct);
            return;
        }

        var response = HydraulicTestMapper.ToResponse(result);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/hydraulic-test/history — 历史测试结果
// ═══════════════════════════════════════════════════════════

public class GetHydraulicTestHistoryEndpoint : EndpointWithoutRequest
{
    private readonly IHydraulicTestRepository _repo;

    public GetHydraulicTestHistoryEndpoint(IHydraulicTestRepository repo) => _repo = repo;

    public override void Configure()
    {
        Get("/hydraulic-test/history");
        Group<HydraulicTestGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector, MesRoles.Technician);
        Summary(s => s.Summary = "获取液压测试历史记录");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var limitRaw = Query<int?>("limit", false);
        var limit = limitRaw.HasValue ? Math.Clamp(limitRaw.Value, 1, 200) : 50;
        var results = await _repo.GetByEquipmentAsync("EQ-HYD-01", limit, ct);
        var response = results.Select(HydraulicTestMapper.ToResponse).ToList();
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  POST /api/v1/hydraulic-test/{id}/unlock — 解锁设备
// ═══════════════════════════════════════════════════════════

public class UnlockHydraulicEquipmentEndpoint : Endpoint<UnlockRequest>
{
    private readonly IHydraulicTestRepository _repo;

    public UnlockHydraulicEquipmentEndpoint(IHydraulicTestRepository repo) => _repo = repo;

    public override void Configure()
    {
        Post("/hydraulic-test/{id}/unlock");
        Group<HydraulicTestGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "质量工程师解锁液压测试设备");
    }

    public override async Task HandleAsync(UnlockRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var recordId))
        {
            AddError("id", "无效的记录 Id");
            ThrowIfAnyErrors();
        }

        if (string.IsNullOrWhiteSpace(req.UnlockedBy))
        {
            AddError("UnlockedBy", "解锁人工号不能为空");
            ThrowIfAnyErrors();
        }

        var result = await _repo.GetByIdAsync(recordId, ct);
        if (result is null)
        {
            HttpContext.Response.StatusCode = 404;
            await HttpContext.Response.WriteAsync("{\"error\":\"未找到测试记录\"}", ct);
            return;
        }

        result.UnlockEquipment(req.UnlockedBy);
        _repo.Update(result);
        await _repo.SaveChangesAsync(ct);

        await HttpContext.Response.WriteAsJsonAsync(new
        {
            Id = result.Id.ToString(),
            result.EquipmentCode,
            result.EquipmentLocked,
            result.UnlockedBy,
            result.UnlockedAt,
            Message = $"设备 {result.EquipmentCode} 已由 {req.UnlockedBy} 解锁"
        }, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  GET /api/v1/hydraulic-test/{id} — 按 ID 查询
// ═══════════════════════════════════════════════════════════

public class GetHydraulicTestByIdEndpoint : EndpointWithoutRequest
{
    private readonly IHydraulicTestRepository _repo;

    public GetHydraulicTestByIdEndpoint(IHydraulicTestRepository repo) => _repo = repo;

    public override void Configure()
    {
        Get("/hydraulic-test/{id}");
        Group<HydraulicTestGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.Inspector, MesRoles.Technician);
        Summary(s => s.Summary = "按 Id 查询液压测试结果");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var recordId))
        {
            AddError("id", "无效的记录 Id");
            ThrowIfAnyErrors();
        }

        var result = await _repo.GetByIdAsync(recordId, ct);
        if (result is null)
        {
            HttpContext.Response.StatusCode = 404;
            await HttpContext.Response.WriteAsync("{\"error\":\"未找到测试记录\"}", ct);
            return;
        }

        var response = HydraulicTestMapper.ToResponse(result);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

// ═══════════════════════════════════════════════════════════
//  端点组
// ═══════════════════════════════════════════════════════════

public class HydraulicTestGroup : Group
{
    public HydraulicTestGroup() => Configure("api/v1", ep => { });
}

// ═══════════════════════════════════════════════════════════
//  DTO
// ═══════════════════════════════════════════════════════════

public class UnlockRequest
{
    public string UnlockedBy { get; set; } = string.Empty;
}

public class HydraulicTestResponse
{
    public string Id { get; set; } = string.Empty;
    public string EquipmentCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CycleNumber { get; set; }
    public double? PressureBuildTimeMs { get; set; }
    public bool? PressureBuildPass { get; set; }
    public double? HoldPressureBar { get; set; }
    public bool? HoldPressurePass { get; set; }
    public double? PressureReleaseTimeMs { get; set; }
    public bool? PressureReleasePass { get; set; }
    public double? LeakRateCcHr { get; set; }
    public bool? LeakRatePass { get; set; }
    public int SolenoidTestCount { get; set; }
    public int SolenoidPassCount { get; set; }
    public bool OverallPass { get; set; }
    public string? FailureReason { get; set; }
    public bool EquipmentLocked { get; set; }
    public string? UnlockedBy { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

// ═══════════════════════════════════════════════════════════
//  Mapper
// ═══════════════════════════════════════════════════════════

public static class HydraulicTestMapper
{
    public static HydraulicTestResponse ToResponse(HydraulicTestResult r)
        => new()
        {
            Id = r.Id.ToString(),
            EquipmentCode = r.EquipmentCode,
            Status = r.Status.ToString(),
            CycleNumber = r.CycleNumber,
            PressureBuildTimeMs = r.PressureBuildTimeMs,
            PressureBuildPass = r.PressureBuildPass,
            HoldPressureBar = r.HoldPressureBar,
            HoldPressurePass = r.HoldPressurePass,
            PressureReleaseTimeMs = r.PressureReleaseTimeMs,
            PressureReleasePass = r.PressureReleasePass,
            LeakRateCcHr = r.LeakRateCcHr,
            LeakRatePass = r.LeakRatePass,
            SolenoidTestCount = r.SolenoidTests.Count,
            SolenoidPassCount = r.SolenoidTests.Count(t => t.ActuationPass),
            OverallPass = r.OverallPass,
            FailureReason = r.FailureReason,
            EquipmentLocked = r.EquipmentLocked,
            UnlockedBy = r.UnlockedBy,
            StartedAt = r.StartedAt,
            CompletedAt = r.CompletedAt,
        };
}
