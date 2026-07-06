using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// SPC 判异规则枚举（Western Electric 规则/WECO）。
/// </summary>
public enum SpcRuleType
{
    /// <summary>Rule 1: 1 点超出 3σ 控制限</summary>
    Beyond3Sigma = 1,
    /// <summary>Rule 2: 连续 3 点中有 2 点在 2σ-3σ 之间（同侧）</summary>
    TwoOfThreeInZoneA = 2,
    /// <summary>Rule 3: 连续 5 点中有 4 点在 1σ-2σ 之间（同侧）</summary>
    FourOfFiveInZoneB = 3,
    /// <summary>Rule 4: 连续 8 点在控制限同侧（C 区或以上）</summary>
    EightInZoneCOrBeyond = 4,
    /// <summary>Rule 5: 连续 6 点递增或递减</summary>
    SixInTrend = 5,
    /// <summary>Rule 6: 连续 14 点交替上下</summary>
    FourteenAlternating = 6,
    /// <summary>Rule 7: 连续 15 点在 C 区（±1σ 内）</summary>
    FifteenInZoneC = 7,
    /// <summary>Rule 8: 连续 8 点在 C 区以外（两侧）</summary>
    EightOutsideZoneC = 8,
}

/// <summary>
/// 判异规则严重等级。
/// </summary>
public enum SpcAlertLevel
{
    Warning = 0,
    OutOfControl = 1,
    Critical = 2,
}

/// <summary>
/// SPC 判异规则告警（T2.5 Western Electric 规则触发记录）。
/// 当控制图检测到异常模式时生成此记录，关联到对应特性。
/// 用于触发 Andon 报警（T2.20）和质量工程师介入。
/// </summary>
[MemoryPackable]
public partial class SpcRuleAlert
{
    public Ulid Id { get; set; }

    /// <summary>特性编码</summary>
    public string CharacteristicCode { get; set; } = string.Empty;

    /// <summary>触发的判异规则</summary>
    public SpcRuleType RuleType { get; set; }

    /// <summary>告警等级</summary>
    public SpcAlertLevel AlertLevel { get; set; }

    /// <summary>关联子组 Id（触发告警的最后一个子组）</summary>
    public Ulid? TriggerSubgroupId { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>关联设备编码</summary>
    public string? EquipmentCode { get; set; }

    /// <summary>告警描述（如 "Rule 4: X̄ 连续 8 点在 CL 上侧"）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>是否已确认</summary>
    public bool IsAcknowledged { get; set; }

    /// <summary>确认人（员工号）</summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>确认时间</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>处置措施</summary>
    public string? ActionTaken { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static SpcRuleAlert Create(
        SpcRuleType ruleType,
        string characteristicCode,
        Ulid? triggerSubgroupId,
        Ulid? orderId,
        string? equipmentCode,
        SpcAlertLevel alertLevel,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characteristicCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new SpcRuleAlert
        {
            Id = Ulid.NewUlid(),
            CharacteristicCode = characteristicCode.Trim(),
            RuleType = ruleType,
            AlertLevel = alertLevel,
            TriggerSubgroupId = triggerSubgroupId,
            OrderId = orderId,
            EquipmentCode = equipmentCode?.Trim(),
            Description = description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>确认告警</summary>
    public void Acknowledge(string acknowledgedBy, string? actionTaken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acknowledgedBy);
        IsAcknowledged = true;
        AcknowledgedBy = acknowledgedBy.Trim();
        AcknowledgedAt = DateTimeOffset.UtcNow;
        if (actionTaken is not null)
            ActionTaken = actionTaken.Trim();
    }
}
