using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// SPC 样本子组（T2.5）。
/// 每条记录代表一个子组（通常 n=5），包含该子组的实测值、均值、极差。
/// 用于 X̄-R 控制图的实时计算与展示。
/// 使用 stackalloc Span&lt;double&gt; 零分配计算均值-极差（AGENTS.md 5.1 铁律）。
/// </summary>
[MemoryPackable]
public partial class SpcSample
{
    public Ulid Id { get; set; }

    /// <summary>特性编码（如 TOR-M6）</summary>
    public string CharacteristicCode { get; set; } = string.Empty;

    /// <summary>关联工单 Id</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>关联工单号</summary>
    public string? OrderNumber { get; set; }

    /// <summary>关联设备编码（PLC 自动采集时填写）</summary>
    public string? EquipmentCode { get; set; }

    /// <summary>子组序号（随时间递增）</summary>
    public int SubgroupIndex { get; set; }

    /// <summary>子组大小（n）</summary>
    public int SubgroupSize { get; set; }

    /// <summary>子组内各测量值</summary>
    public List<double> Values { get; set; } = [];

    /// <summary>子组均值 (X̄)</summary>
    public double Mean { get; set; }

    /// <summary>子组极差 (R)</summary>
    public double Range { get; set; }

    /// <summary>子组标准差 (s)</summary>
    public double StdDev { get; set; }

    /// <summary>采集时间</summary>
    public DateTimeOffset CollectedAt { get; set; }

    /// <summary>数据来源（Manual=人工录入 / Plc=PLC 自动采集）</summary>
    public string Source { get; set; } = "Manual";

    /// <summary>
    /// 创建 SPC 样本并计算均值、极差、标准差。
    /// 使用 stackalloc 零分配计算（AGENTS.md 4.3 零分配铁律）。
    /// </summary>
    public static SpcSample Create(
        string characteristicCode,
        int subgroupIndex,
        ReadOnlySpan<double> values,
        Ulid? orderId = null,
        string? orderNumber = null,
        string? equipmentCode = null,
        DateTimeOffset? collectedAt = null)
    {
        if (string.IsNullOrWhiteSpace(characteristicCode))
            throw new ArgumentException("特性编码不能为空", nameof(characteristicCode));

        if (values.Length < 2)
            throw new ArgumentException("子组至少需要 2 个测量值", nameof(values));

        // stackalloc 零分配计算
        var n = values.Length;
        Span<double> sorted = stackalloc double[n];
        values.CopyTo(sorted);

        // 均值
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += values[i];
        var mean = sum / n;

        // 极差
        sorted.Sort();
        var range = sorted[^1] - sorted[0];

        // 样本标准差 (n-1)
        double varianceSum = 0;
        for (int i = 0; i < n; i++)
        {
            var dev = values[i] - mean;
            varianceSum += dev * dev;
        }
        var stdDev = Math.Sqrt(varianceSum / (n - 1));

        return new SpcSample
        {
            Id = Ulid.NewUlid(),
            CharacteristicCode = characteristicCode.Trim(),
            OrderId = orderId,
            OrderNumber = orderNumber?.Trim(),
            EquipmentCode = equipmentCode?.Trim(),
            SubgroupIndex = subgroupIndex,
            SubgroupSize = n,
            Values = [.. values.ToArray()],
            Mean = Math.Round(mean, 4),
            Range = Math.Round(range, 4),
            StdDev = Math.Round(stdDev, 4),
            CollectedAt = collectedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
