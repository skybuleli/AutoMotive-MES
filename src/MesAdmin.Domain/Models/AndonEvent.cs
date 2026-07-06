using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// Andon 报警状态枚举（三级上报 L1→L2→L3）。
/// Active: 刚触发，L1 工位声光 + 看板
/// EscalatedL2: 5min 未确认 → 升级到班组长
/// EscalatedL3: 10min 未确认 → 升级到生产经理
/// Acknowledged: 已确认，消除脉冲动画
/// Resolved: 已解决（原因已查明+围堵措施已执行）
/// Closed: 已关闭（闭环完成）
/// </summary>
public enum AndonEventStatus
{
    /// <summary>L1 工位报警 — 声光 + 看板，等待确认</summary>
    Active = 0,

    /// <summary>L2 升级 — 班组长 PDA + 企业微信推送</summary>
    EscalatedL2 = 1,

    /// <summary>L3 升级 — 生产经理电话 + 邮件</summary>
    EscalatedL3 = 2,

    /// <summary>已确认 — 已有人接管，脉冲动画消除</summary>
    Acknowledged = 3,

    /// <summary>已解决 — 原因已查明，围堵措施已执行</summary>
    Resolved = 4,

    /// <summary>已关闭 — 闭环完成（NCR 或 8D 关联后关闭）</summary>
    Closed = 5,
}

/// <summary>
/// Andon 报警类型（ESP 总成产线专用）。
/// </summary>
public enum AndonAlarmType
{
    /// <summary>扭矩超差 — M6/M8 螺栓扭矩超出规格限</summary>
    TorqueExceeded = 0,

    /// <summary>泄漏超标 — 液压测试台泄漏率超过上限</summary>
    LeakRateHigh = 1,

    /// <summary>刷写失败 — ECU 刷写失败（含刷写超时/校验失败）</summary>
    FlashFailed = 2,

    /// <summary>CAN 通信异常 — CAN 总线延迟/丢帧/断连</summary>
    CanCommunicationError = 3,

    /// <summary>设备报警 — 通用设备报警（EquipmentStatus.Alarm）</summary>
    EquipmentAlarm = 4,

    /// <summary>过程参数偏差 — 过程参数超出控制限（非扭矩/泄漏）</summary>
    ProcessDeviation = 5,

    /// <summary>物料缺陷 — 来料或过程发现物料缺陷</summary>
    MaterialDefect = 6,
}

/// <summary>
/// Andon 报警严重等级（对应升级优先级）。
/// Critical → L3 直通（生产经理）
/// Major → L2（班组长）
/// Minor → L1（工位）
/// </summary>
public enum AndonSeverity
{
    /// <summary>轻微 — L1 工位处理</summary>
    Minor = 0,

    /// <summary>重要 — L2 升级班组长</summary>
    Major = 1,

    /// <summary>严重 — L3 升级生产经理</summary>
    Critical = 2,
}

/// <summary>
/// Andon 报警事件（T2.20）。
/// 三级上报：L1 工位声光+看板 → L2 班组长 PDA → L3 生产经理。
/// 由 AndonReactivePipeline 在检测到报警条件时创建。
/// 由 AndonEscalationService 按超时升级。
/// </summary>
[MemoryPackable]
public partial class AndonEvent
{
    public Ulid Id { get; set; }

    /// <summary>报警编号（格式 AND-YYYYMMDD-NNNN）</summary>
    public string EventNumber { get; set; } = string.Empty;

    /// <summary>设备编码（如 EQ-TQ-01）</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>所属工站编号（1-7）</summary>
    public int Station { get; set; }

    /// <summary>报警类型</summary>
    public AndonAlarmType AlarmType { get; set; }

    /// <summary>严重等级</summary>
    public AndonSeverity Severity { get; set; }

    /// <summary>当前状态</summary>
    public AndonEventStatus Status { get; set; } = AndonEventStatus.Active;

    /// <summary>报警描述（如 \"M6 螺栓扭矩 23.8Nm 超出上限 23Nm\"）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>触发报警的过程值</summary>
    public double ProcessValue { get; set; }

    /// <summary>过程值标签（如 \"Torque-M6-FL\"）</summary>
    public string? ProcessTag { get; set; }

    /// <summary>规格上限（触发比较用）</summary>
    public double? UpperLimit { get; set; }

    /// <summary>规格下限（触发比较用）</summary>
    public double? LowerLimit { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>关联 NCR Id</summary>
    public Ulid? NonConformanceReportId { get; set; }

    /// <summary>当前升级级别（0=L1, 1=L2, 2=L3）</summary>
    public int EscalationLevel { get; set; }

    /// <summary>升级时间（UTC，最近一次升级）</summary>
    public DateTimeOffset? EscalatedAt { get; set; }

    /// <summary>确认人（员工号）</summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>确认时间</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>解决人</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>解决措施描述</summary>
    public string? Resolution { get; set; }

    /// <summary>解决时间</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>关闭备注</summary>
    public string? CloseRemarks { get; set; }

    /// <summary>关闭时间</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>报警触发时间（UTC）</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 创建 Andon 报警事件。
    /// </summary>
    public static AndonEvent Create(
        string equipmentCode,
        int station,
        AndonAlarmType alarmType,
        AndonSeverity severity,
        string description,
        double processValue,
        string? processTag,
        double? upperLimit,
        double? lowerLimit,
        Ulid? orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var now = DateTimeOffset.UtcNow;
        return new AndonEvent
        {
            Id = Ulid.NewUlid(),
            EventNumber = $"AND-{now:yyyyMMdd}-{Random.Shared.Next(0, 9999):D4}",
            EquipmentCode = equipmentCode.Trim(),
            Station = station,
            AlarmType = alarmType,
            Severity = severity,
            Status = AndonEventStatus.Active,
            Description = description.Trim(),
            ProcessValue = processValue,
            ProcessTag = processTag?.Trim(),
            UpperLimit = upperLimit,
            LowerLimit = lowerLimit,
            OrderId = orderId,
            OccurredAt = now,
            CreatedAt = now,
        };
    }

    /// <summary>确认报警（返回 false 表示状态非法无法确认）</summary>
    public bool Acknowledge(string acknowledgedBy)
    {
        if (Status is AndonEventStatus.Resolved or AndonEventStatus.Closed)
            return false;

        ArgumentException.ThrowIfNullOrWhiteSpace(acknowledgedBy);
        Status = AndonEventStatus.Acknowledged;
        AcknowledgedBy = acknowledgedBy.Trim();
        AcknowledgedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>升级到下一级（L1→L2 或 L2→L3）</summary>
    public void Escalate()
    {
        if (Status == AndonEventStatus.Active && EscalationLevel == 0)
        {
            // L1 → L2（5min 未确认）
            Status = AndonEventStatus.EscalatedL2;
            EscalationLevel = 1;
            EscalatedAt = DateTimeOffset.UtcNow;
        }
        else if (Status == AndonEventStatus.EscalatedL2 && EscalationLevel == 1)
        {
            // L2 → L3（10min 未确认）
            Status = AndonEventStatus.EscalatedL3;
            EscalationLevel = 2;
            EscalatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>解决报警</summary>
    public bool Resolve(string resolvedBy, string resolution)
    {
        if (Status is AndonEventStatus.Resolved or AndonEventStatus.Closed)
            return false;

        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedBy);
        ResolvedBy = resolvedBy.Trim();
        Resolution = resolution.Trim();
        ResolvedAt = DateTimeOffset.UtcNow;
        Status = AndonEventStatus.Resolved;
        return true;
    }

    /// <summary>关闭报警</summary>
    public bool Close(string closeRemarks)
    {
        if (Status == AndonEventStatus.Closed)
            return false;

        CloseRemarks = closeRemarks.Trim();
        ClosedAt = DateTimeOffset.UtcNow;
        Status = AndonEventStatus.Closed;
        return true;
    }

    /// <summary>判断是否超时待升级</summary>
    public bool IsEscalationOverdue(TimeSpan l2Timeout, TimeSpan l3Timeout)
    {
        var elapsed = DateTimeOffset.UtcNow - OccurredAt;
        return (Status == AndonEventStatus.Active && elapsed >= l2Timeout)
            || (Status == AndonEventStatus.EscalatedL2 && elapsed >= l3Timeout);
    }
}
