using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// BOM（物料清单）领域模型。
/// ESP-9.0 含 87 种物料、4 层结构。
/// </summary>
[MemoryPackable]
public partial class Bom
{
    public Ulid Id { get; set; }

    /// <summary>产品编码（ESP-9.0 / ESP-9.1）</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>BOM 版本</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>物料项列表</summary>
    public List<BomItem> Items { get; set; } = [];

    /// <summary>生效日期</summary>
    public DateTimeOffset EffectiveDate { get; set; }

    /// <summary>失效日期（Null=永久有效）</summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static Bom Create(Ulid id, string productCode, string version, DateTimeOffset effectiveDate)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("产品编码不能为空", nameof(productCode));

        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("BOM 版本不能为空", nameof(version));

        return new Bom
        {
            Id = id,
            ProductCode = productCode.Trim().ToUpperInvariant(),
            Version = version.Trim(),
            EffectiveDate = effectiveDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public bool IsEffectiveAt(DateTimeOffset at)
        => at >= EffectiveDate && (ExpirationDate is null || at <= ExpirationDate);

    public void AddItem(BomItem item)
    {
        if (Items.Any(i => i.MaterialCode == item.MaterialCode))
            throw new InvalidOperationException($"物料 {item.MaterialCode} 已存在");

        Items.Add(item);
    }
}

/// <summary>
/// BOM 物料项。
/// </summary>
[MemoryPackable]
public partial class BomItem
{
    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>单台用量</summary>
    public double QuantityPerUnit { get; set; }

    /// <summary>是否关键物料（ECU芯片/电磁阀/压力传感器等）</summary>
    public bool IsCritical { get; set; }

    /// <summary>层级（1=总成 / 2=子总成 / 3=零部件 / 4=原材料）</summary>
    public int Level { get; set; }

    /// <summary>父物料编码（顶层为 Null）</summary>
    public string? ParentMaterialCode { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    public static BomItem Create(
        string materialCode,
        string materialName,
        double quantityPerUnit,
        string unit,
        int level = 1,
        bool isCritical = false,
        string? parentMaterialCode = null)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        if (quantityPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantityPerUnit), "用量必须大于 0");

        return new BomItem
        {
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            QuantityPerUnit = quantityPerUnit,
            Unit = unit.Trim(),
            Level = level,
            IsCritical = isCritical,
            ParentMaterialCode = parentMaterialCode,
        };
    }
}
