using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 库存预警级别。
/// </summary>
public enum InventoryAlertLevel
{
    /// <summary>正常（高于安全库存）</summary>
    Normal = 0,
    /// <summary>黄色预警（低于安全库存但高于最低库存）</summary>
    Yellow = 1,
    /// <summary>红色报警（低于最低库存）</summary>
    Red = 2
}

/// <summary>
/// 物料库存阈值设置（T1.13 线边库存实时监控）。
/// 每工位每种物料可配置安全库存和最低库存阈值。
/// 低于安全库存 → 黄色预警；低于最低库存 → 红色报警 + 自动叫料。
/// </summary>
[MemoryPackable]
public partial class MaterialInventorySetting
{
    public Ulid Id { get; set; }

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>工位编号（Null=通用设置）</summary>
    public string? StationId { get; set; }

    /// <summary>安全库存阈值（低于此值触发黄色预警）</summary>
    public double SafetyStock { get; set; }

    /// <summary>最低库存阈值（低于此值触发红色报警 + 自动叫料）</summary>
    public double MinimumStock { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否启用监控</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否关键物料</summary>
    public bool IsCritical { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后更新人</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>最后更新时间</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public static MaterialInventorySetting Create(
        string materialCode,
        string materialName,
        double safetyStock,
        double minimumStock,
        string unit,
        string? stationId = null,
        bool isCritical = false)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        if (string.IsNullOrWhiteSpace(materialName))
            throw new ArgumentException("物料名称不能为空", nameof(materialName));

        if (safetyStock <= 0)
            throw new ArgumentOutOfRangeException(nameof(safetyStock), "安全库存必须大于 0");

        if (minimumStock <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumStock), "最低库存必须大于 0");

        if (minimumStock >= safetyStock)
            throw new ArgumentException("最低库存必须小于安全库存", nameof(minimumStock));

        if (string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("单位不能为空", nameof(unit));

        return new MaterialInventorySetting
        {
            Id = Ulid.NewUlid(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            StationId = stationId?.Trim(),
            SafetyStock = safetyStock,
            MinimumStock = minimumStock,
            Unit = unit.Trim(),
            IsEnabled = true,
            IsCritical = isCritical,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>根据当前库存量计算预警级别。</summary>
    public InventoryAlertLevel GetAlertLevel(double currentQuantity)
        => currentQuantity switch
        {
            < 0 => InventoryAlertLevel.Red,
            _ when currentQuantity < MinimumStock => InventoryAlertLevel.Red,
            _ when currentQuantity < SafetyStock => InventoryAlertLevel.Yellow,
            _ => InventoryAlertLevel.Normal,
        };

    /// <summary>更新阈值。</summary>
    public void UpdateThresholds(double safetyStock, double minimumStock, string updatedBy)
    {
        if (safetyStock <= 0)
            throw new ArgumentOutOfRangeException(nameof(safetyStock), "安全库存必须大于 0");

        if (minimumStock <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumStock), "最低库存必须大于 0");

        if (minimumStock >= safetyStock)
            throw new ArgumentException("最低库存必须小于安全库存", nameof(minimumStock));

        if (string.IsNullOrWhiteSpace(updatedBy))
            throw new ArgumentException("更新人不能为空", nameof(updatedBy));

        SafetyStock = safetyStock;
        MinimumStock = minimumStock;
        UpdatedBy = updatedBy.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// 库存监控预警记录（T1.13）。
/// 当线边库存突破阈值时自动生成。
/// </summary>
[MemoryPackable]
public partial class InventoryAlert
{
    public Ulid Id { get; set; }

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>工位编号</summary>
    public string? StationId { get; set; }

    /// <summary>当前库存量（预警产生时的值）</summary>
    public double CurrentQuantity { get; set; }

    /// <summary>安全库存阈值</summary>
    public double SafetyStock { get; set; }

    /// <summary>最低库存阈值</summary>
    public double MinimumStock { get; set; }

    /// <summary>预警级别</summary>
    public InventoryAlertLevel AlertLevel { get; set; }

    /// <summary>是否已处理</summary>
    public bool IsResolved { get; set; }

    /// <summary>处理人</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>处理时间</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>处理备注</summary>
    public string? Resolution { get; set; }

    /// <summary>预警触发时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>关联的 JIT 拉动信号 Id（红色报警自动叫料时生成）</summary>
    public Ulid? JitPullSignalId { get; set; }

    public static InventoryAlert Create(
        string materialCode,
        string materialName,
        double currentQuantity,
        double safetyStock,
        double minimumStock,
        InventoryAlertLevel alertLevel,
        string? stationId = null,
        Ulid? jitPullSignalId = null)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        return new InventoryAlert
        {
            Id = Ulid.NewUlid(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            StationId = stationId?.Trim(),
            CurrentQuantity = currentQuantity,
            SafetyStock = safetyStock,
            MinimumStock = minimumStock,
            AlertLevel = alertLevel,
            IsResolved = false,
            JitPullSignalId = jitPullSignalId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>标记预警已处理。</summary>
    public void Resolve(string resolvedBy, string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolvedBy))
            throw new ArgumentException("处理人不能为空", nameof(resolvedBy));

        if (string.IsNullOrWhiteSpace(resolution))
            throw new ArgumentException("处理备注不能为空", nameof(resolution));

        IsResolved = true;
        ResolvedBy = resolvedBy.Trim();
        ResolvedAt = DateTimeOffset.UtcNow;
        Resolution = resolution.Trim();
    }
}
