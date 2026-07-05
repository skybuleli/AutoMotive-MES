using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 物料消耗记录（T1.17 物料消耗反冲）。
/// 工单完工时按 BOM 标准用量扣减线边库存，每条物料生成一条记录。
/// </summary>
[MemoryPackable]
public partial class MaterialConsumption
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>BOM 标准用量（= 合格数量 × 单台用量）</summary>
    public double StandardQuantity { get; set; }

    /// <summary>实际投料绑定数量（来自 material_bindings 汇总）</summary>
    public double ActualBoundQuantity { get; set; }

    /// <summary>实际消耗批次数量（从 MaterialBatch 扣减的汇总）</summary>
    public double ConsumedQuantity { get; set; }

    /// <summary>差异数量（ConsumedQuantity - StandardQuantity）</summary>
    public double VarianceQuantity { get; set; }

    /// <summary>差异百分比（VarianceQuantity / StandardQuantity × 100%）</summary>
    public double VariancePercent { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否关键物料</summary>
    public bool IsCritical { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static MaterialConsumption Create(
        Ulid orderId, string orderNumber, string materialCode, string materialName,
        double standardQuantity, double actualBoundQuantity, double consumedQuantity,
        string unit, bool isCritical)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        var varianceQty = Math.Round(consumedQuantity - standardQuantity, 4);
        var variancePct = standardQuantity > 0
            ? Math.Round(varianceQty / standardQuantity * 100, 2)
            : 0;

        return new MaterialConsumption
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            StandardQuantity = Math.Round(standardQuantity, 2),
            ActualBoundQuantity = Math.Round(actualBoundQuantity, 2),
            ConsumedQuantity = Math.Round(consumedQuantity, 2),
            VarianceQuantity = varianceQty,
            VariancePercent = variancePct,
            Unit = unit.Trim(),
            IsCritical = isCritical,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>是否超过差异阈值（2%）</summary>
    public bool IsVarianceExceeded => Math.Abs(VariancePercent) > 2;
}

// ═════════════════════════════════════════════════════════

/// <summary>
/// 消耗差异异常报告（T1.17 差异 > 2%）。
/// 当实际消耗与 BOM 标准用量偏差超过 2% 时生成，触发异常处理流程。
/// </summary>
[MemoryPackable]
public partial class ConsumptionVarianceReport
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>BOM 标准用量</summary>
    public double StandardQuantity { get; set; }

    /// <summary>实际消耗量</summary>
    public double ConsumedQuantity { get; set; }

    /// <summary>差异数量</summary>
    public double VarianceQuantity { get; set; }

    /// <summary>差异百分比</summary>
    public double VariancePercent { get; set; }

    /// <summary>差异方向（偏高/偏低）</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否已处理（质量工程师确认）</summary>
    public bool IsResolved { get; set; }

    /// <summary>处理人</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>处理备注</summary>
    public string? Resolution { get; set; }

    /// <summary>处理时间</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static ConsumptionVarianceReport Create(
        Ulid orderId, string orderNumber, string materialCode, string materialName,
        double standard, double consumed, double varianceQty, double variancePct, string unit)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        return new ConsumptionVarianceReport
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            StandardQuantity = Math.Round(standard, 2),
            ConsumedQuantity = Math.Round(consumed, 2),
            VarianceQuantity = Math.Round(varianceQty, 2),
            VariancePercent = Math.Round(variancePct, 2),
            Direction = varianceQty > 0 ? "偏高" : "偏低",
            Unit = unit.Trim(),
            IsResolved = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

// ═════════════════════════════════════════════════════════

/// <summary>
/// SAP 物料移动凭证同步记录（T1.17 物料反冲 → T3.14 SAP 同步）。
/// 工单完工消耗反冲时生成，标记 SapSynced=false，
/// 由 T3.14 后台作业轮询调用 SAP RFC/BAPI 同步。
/// </summary>
[MemoryPackable]
public partial class SapInventorySyncRecord
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>移动类型（如 261=工单发料）</summary>
    public string MovementType { get; set; } = "261";

    /// <summary>数量</summary>
    public double Quantity { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否已同步 SAP</summary>
    public bool SapSynced { get; set; }

    /// <summary>SAP 物料凭证号（同步后回写）</summary>
    public string? SapDocumentNumber { get; set; }

    /// <summary>同步错误信息</summary>
    public string? SyncError { get; set; }

    /// <summary>同步时间</summary>
    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static SapInventorySyncRecord Create(
        Ulid orderId, string orderNumber, string materialCode,
        double quantity, string unit, string? movementType = null)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "数量必须大于 0");

        return new SapInventorySyncRecord
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            MaterialCode = materialCode.Trim(),
            MovementType = movementType ?? "261",
            Quantity = Math.Round(quantity, 2),
            Unit = unit.Trim(),
            SapSynced = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>标记已同步 SAP</summary>
    public void MarkSynced(string documentNumber)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            throw new ArgumentException("SAP 凭证号不能为空", nameof(documentNumber));

        SapDocumentNumber = documentNumber.Trim();
        SapSynced = true;
        SyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>标记同步失败</summary>
    public void MarkError(string error)
    {
        SyncError = error?.Trim();
        SyncedAt = DateTimeOffset.UtcNow;
    }
}
