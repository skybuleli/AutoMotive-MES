using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>量具类型。</summary>
public enum GaugeType
{
    /// <summary>扭矩扳手 / 扭矩仪</summary>
    TorqueWrench = 0,

    /// <summary>卡尺 / 千分尺</summary>
    Caliper = 1,

    /// <summary>压力表 / 压力校验仪</summary>
    PressureGauge = 2,

    /// <summary>万用表 / 电参数仪</summary>
    Multimeter = 3,

    /// <summary>其他计量器具</summary>
    Other = 4,
}

/// <summary>
/// 量具状态。
/// InService=在用（校准有效）→ DueSoon=临期（30 天内到期）→ Overdue=已过期 → 校准后复位；
/// Scrapped 为终态，任何操作不可逆。
/// </summary>
public enum GaugeStatus
{
    /// <summary>在用 — 校准有效且距到期 > 30 天</summary>
    InService = 0,

    /// <summary>临期 — 距校准到期 ≤ 30 天</summary>
    DueSoon = 1,

    /// <summary>已过期 — 超过校准有效期，必须停用送检</summary>
    Overdue = 2,

    /// <summary>已报废 — 终态，禁止参与任何检验</summary>
    Scrapped = 3,
}

/// <summary>校准结论。</summary>
public enum CalibrationResult
{
    /// <summary>合格 — 按原周期顺延</summary>
    Pass = 0,

    /// <summary>不合格 — 器具应停用/报废处置</summary>
    Fail = 1,

    /// <summary>调修后合格 — 按新周期起算</summary>
    Adjusted = 2,
}

/// <summary>
/// 计量器具台账（S01 · IATF 16949 计量管理）。
/// 每把量具持唯一编号、校准周期与下次到期日；状态由 NextDueAt 相对当前时间推导。
/// </summary>
[MemoryPackable]
public partial class Gauge
{
    /// <summary>临期预警窗口（天）。</summary>
    public const int DueSoonWindowDays = 30;

    public Ulid Id { get; set; }

    /// <summary>器具编号（唯一，如 GT-TQ-001）</summary>
    public string GaugeNumber { get; set; } = string.Empty;

    /// <summary>器具名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型</summary>
    public GaugeType Type { get; set; }

    /// <summary>量程规格（如 "0-100 Nm"）</summary>
    public string RangeSpec { get; set; } = string.Empty;

    /// <summary>分辨力规格（如 "0.01 Nm"）</summary>
    public string ResolutionSpec { get; set; } = string.Empty;

    /// <summary>精度等级（如 "0.5 级"）</summary>
    public string AccuracyClass { get; set; } = string.Empty;

    /// <summary>校准周期（天）</summary>
    public int CalibrationCycleDays { get; set; }

    /// <summary>最近一次校准时间</summary>
    public DateTimeOffset? LastCalibratedAt { get; set; }

    /// <summary>下次校准到期时间</summary>
    public DateTimeOffset? NextDueAt { get; set; }

    /// <summary>状态</summary>
    public GaugeStatus Status { get; set; } = GaugeStatus.InService;

    /// <summary>存放位置</summary>
    public string? StorageLocation { get; set; }

    /// <summary>备注（报废原因等追加于此）</summary>
    public string? Remarks { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Gauge Create(
        Ulid id,
        string gaugeNumber,
        string name,
        GaugeType type,
        string rangeSpec,
        string resolutionSpec,
        string accuracyClass,
        int calibrationCycleDays,
        DateTimeOffset lastCalibratedAt,
        string? storageLocation = null,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gaugeNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calibrationCycleDays);

        // Npgsql 写 timestamptz 仅接受 UTC offset=0，入口处统一归一化
        var lastCal = lastCalibratedAt.ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        var gauge = new Gauge
        {
            Id = id,
            GaugeNumber = gaugeNumber.Trim(),
            Name = name.Trim(),
            Type = type,
            RangeSpec = rangeSpec.Trim(),
            ResolutionSpec = resolutionSpec.Trim(),
            AccuracyClass = accuracyClass.Trim(),
            CalibrationCycleDays = calibrationCycleDays,
            LastCalibratedAt = lastCal,
            StorageLocation = storageLocation?.Trim(),
            Remarks = remarks?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        gauge.NextDueAt = lastCal.AddDays(calibrationCycleDays);
        gauge.Status = EvaluateStatus(gauge.NextDueAt, GaugeStatus.InService, now);
        return gauge;
    }

    /// <summary>
    /// 登记一次校准：更新最近校准时间并重算到期日、刷新状态。
    /// nextDueAfter 为空时按 CalibratedAt + 周期天数 推算，否则采用外部指定值
    /// （校准证书通常直接给出下次有效截止日）。
    /// </summary>
    /// <returns>false 表示量具已报废，拒绝登记。</returns>
    public bool RecordCalibration(DateTimeOffset calibratedAt, DateTimeOffset? nextDueAfter = null)
    {
        if (Status == GaugeStatus.Scrapped) return false;

        // Npgsql 写 timestamptz 仅接受 UTC offset=0，入口处统一归一化
        var cal = calibratedAt.ToUniversalTime();
        LastCalibratedAt = cal;
        NextDueAt = nextDueAfter.HasValue ? nextDueAfter.Value.ToUniversalTime() : cal.AddDays(CalibrationCycleDays);
        Status = EvaluateStatus(NextDueAt, GaugeStatus.InService, DateTimeOffset.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// 报废（终态）。原因追加至备注。
    /// </summary>
    /// <returns>false 表示已是报废状态。</returns>
    public bool Scrap(string reason)
    {
        if (Status == GaugeStatus.Scrapped) return false;
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = GaugeStatus.Scrapped;
        Remarks = string.IsNullOrWhiteSpace(Remarks) ? $"报废：{reason.Trim()}" : $"{Remarks}；报废：{reason.Trim()}";
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// 按当前时间重新推导状态（后台提醒服务周期性调用以持久化 DueSoon/Overdue 流转）。
    /// </summary>
    public void RefreshStatus(DateTimeOffset now)
    {
        if (Status == GaugeStatus.Scrapped) return;
        Status = EvaluateStatus(NextDueAt, Status, now);
        UpdatedAt = now;
    }

    /// <summary>
    /// 校准是否在有效期内（供检验记录引用校验，S02 复用）。
    /// </summary>
    public bool IsWithinCalibration(DateTimeOffset now)
        => Status != GaugeStatus.Scrapped && NextDueAt is not null && NextDueAt >= now;

    /// <summary>纯函数状态推导：已过期 &gt; 临期窗口 &gt; 维持 fallback。</summary>
    public static GaugeStatus EvaluateStatus(DateTimeOffset? nextDueAt, GaugeStatus fallback, DateTimeOffset now)
    {
        if (nextDueAt is null) return fallback;
        if (nextDueAt.Value < now) return GaugeStatus.Overdue;
        if (nextDueAt.Value <= now.AddDays(DueSoonWindowDays)) return GaugeStatus.DueSoon;
        return fallback is GaugeStatus.DueSoon or GaugeStatus.Overdue
            ? GaugeStatus.InService
            : fallback;
    }
}

/// <summary>
/// 校准记录（S01）。每次送检/外校登记一条，保留证书号留审计追溯。
/// </summary>
[MemoryPackable]
public partial class CalibrationRecord
{
    public Ulid Id { get; set; }

    /// <summary>关联量具 Id</summary>
    public Ulid GaugeId { get; set; }

    /// <summary>校准日期</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>校准结论</summary>
    public CalibrationResult Result { get; set; }

    /// <summary>校准证书编号</summary>
    public string CertificateNo { get; set; } = string.Empty;

    /// <summary>执行人（员工号）</summary>
    public string OperatorId { get; set; } = string.Empty;

    /// <summary>本次登记后的下次到期日（快照留档）</summary>
    public DateTimeOffset NextDueAfter { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static CalibrationRecord Create(
        Ulid id,
        Ulid gaugeId,
        DateTimeOffset calibratedAt,
        CalibrationResult result,
        string certificateNo,
        string operatorId,
        DateTimeOffset nextDueAfter,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        return new CalibrationRecord
        {
            Id = id,
            GaugeId = gaugeId,
            // Npgsql 写 timestamptz 仅接受 UTC offset=0，入口处统一归一化
            CalibratedAt = calibratedAt.ToUniversalTime(),
            Result = result,
            CertificateNo = certificateNo.Trim(),
            OperatorId = operatorId.Trim(),
            NextDueAfter = nextDueAfter.ToUniversalTime(),
            Remarks = remarks?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
