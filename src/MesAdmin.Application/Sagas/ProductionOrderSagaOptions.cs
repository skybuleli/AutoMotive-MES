namespace MesAdmin.Application.Sagas;

/// <summary>
/// ProductionOrderSaga 配置选项。
/// 当工艺路线未配置 EquipmentCode / IsStationSentinel / TargetComponent 时，
/// 使用此处配置作为回退映射。
/// </summary>
public sealed class ProductionOrderSagaOptions
{
    private Dictionary<int, string> _fallbackStationEquipment = new()
    {
        { 2, "EQ-ASM-01" },
        { 3, "EQ-TQ-01" },
        { 4, "EQ-HYD-01" },
        { 5, "EQ-FLS-01" },
        { 6, "EQ-FT-01" },
        { 7, "EQ-VN-01" },
    };

    private Dictionary<int, int> _fallbackStationLastSeq = new()
    {
        { 2, 5 },
        { 3, 10 },
        { 4, 23 },
        { 5, 27 },
        { 7, 31 },
    };

    private Dictionary<string, int> _fallbackBoltSequence = new(StringComparer.OrdinalIgnoreCase)
    {
        { "M6-FL", 6 },
        { "M6-FR", 7 },
        { "M8-RL", 8 },
        { "M8-RR", 9 },
    };

    /// <summary>站号 → 设备码回退映射</summary>
    public IReadOnlyDictionary<int, string> FallbackStationEquipment
    {
        get => _fallbackStationEquipment;
        init => _fallbackStationEquipment = value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>站号 → 最后一道工序序号回退映射</summary>
    public IReadOnlyDictionary<int, int> FallbackStationLastSeq
    {
        get => _fallbackStationLastSeq;
        init => _fallbackStationLastSeq = value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>螺栓编码 → 工序序号回退映射（大小写不敏感）</summary>
    public IReadOnlyDictionary<string, int> FallbackBoltSequence
    {
        get => _fallbackBoltSequence;
        init => _fallbackBoltSequence = value.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }
}
