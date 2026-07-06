using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 备件主数据（T2.18）。
/// 定义维护工单可关联的备件物料清单、安全库存和当前库存量。
/// </summary>
[MemoryPackable]
public partial class SparePart
{
    public Ulid Id { get; set; }

    /// <summary>备件物料编码（如 SP-TQSENSOR-01）</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>备件名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>规格型号</summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>当前库存量</summary>
    public double CurrentQuantity { get; set; }

    /// <summary>安全库存阈值（低于此值触发预警）</summary>
    public double SafetyStock { get; set; }

    /// <summary>最低库存阈值（低于此值触发采购申请）</summary>
    public double MinimumStock { get; set; }

    /// <summary>适用设备编码（可为 null，表示通用）</summary>
    public string? EquipmentCode { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static SparePart Create(
        Ulid id,
        string materialCode,
        string materialName,
        string specification,
        string unit,
        double safetyStock,
        double minimumStock,
        string? equipmentCode = null,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);

        var now = DateTimeOffset.UtcNow;
        return new SparePart
        {
            Id = id,
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            Specification = specification.Trim(),
            Unit = unit.Trim(),
            CurrentQuantity = 0,
            SafetyStock = safetyStock,
            MinimumStock = minimumStock,
            EquipmentCode = equipmentCode?.Trim(),
            Remarks = remarks?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// 更新库存数量。
    /// </summary>
    public void UpdateStock(double newQuantity)
    {
        CurrentQuantity = Math.Max(0, newQuantity);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 扣减库存（维护工单消耗备件时调用）。
    /// </summary>
    public bool Consume(double quantity)
    {
        if (quantity <= 0 || CurrentQuantity < quantity)
            return false;

        CurrentQuantity -= quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// 补货入库。
    /// </summary>
    public void Restock(double quantity)
    {
        if (quantity <= 0) return;
        CurrentQuantity += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 检查库存水平。
    /// </summary>
    public InventoryAlertLevel GetStockLevel()
    {
        if (CurrentQuantity < MinimumStock) return InventoryAlertLevel.Red;
        if (CurrentQuantity < SafetyStock) return InventoryAlertLevel.Yellow;
        return InventoryAlertLevel.Normal;
    }

    /// <summary>
    /// 库存是否低于最低阈值（需要触发采购申请）。
    /// </summary>
    public bool NeedsPurchaseRequest => CurrentQuantity < MinimumStock;

    /// <summary>
    /// 建议采购数量 = 安全库存 × 2 - 当前库存。
    /// </summary>
    public double SuggestedPurchaseQuantity => Math.Max(1, SafetyStock * 2 - CurrentQuantity);
}

/// <summary>
/// 备件使用记录（T2.18）。
/// 关联维护工单与消耗的备件及数量。
/// </summary>
[MemoryPackable]
public partial class SparePartUsage
{
    public Ulid Id { get; set; }

    /// <summary>关联备件 Id</summary>
    public Ulid SparePartId { get; set; }

    /// <summary>关联维护工单 Id</summary>
    public Ulid MaintenanceWorkOrderId { get; set; }

    /// <summary>消耗数量</summary>
    public double Quantity { get; set; }

    /// <summary>消耗时单价（留审计）</summary>
    public double? UnitPrice { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static SparePartUsage Create(
        Ulid id,
        Ulid sparePartId,
        Ulid maintenanceWorkOrderId,
        double quantity,
        double? unitPrice = null,
        string? remarks = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        return new SparePartUsage
        {
            Id = id,
            SparePartId = sparePartId,
            MaintenanceWorkOrderId = maintenanceWorkOrderId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Remarks = remarks?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>
/// 采购申请记录（T2.18）。
/// 库存不足时自动或手动生成的采购申请。
/// </summary>
[MemoryPackable]
public partial class PurchaseRequest
{
    public Ulid Id { get; set; }

    /// <summary>采购申请编号 PR-YYYYMMDD-NNNN</summary>
    public string RequestNumber { get; set; } = string.Empty;

    /// <summary>关联备件 Id</summary>
    public Ulid SparePartId { get; set; }

    /// <summary>申请数量</summary>
    public double Quantity { get; set; }

    /// <summary>申请原因</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>状态：Pending / Approved / Ordered / Received / Cancelled</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>申请人（员工号）</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>审批人</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>审批时间</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static PurchaseRequest Create(
        Ulid id,
        Ulid sparePartId,
        double quantity,
        string reason,
        string requestedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        var now = DateTimeOffset.UtcNow;
        return new PurchaseRequest
        {
            Id = id,
            RequestNumber = $"PR-{now:yyyyMMdd}-{Random.Shared.Next(0, 9999):D4}",
            SparePartId = sparePartId,
            Quantity = quantity,
            Reason = reason.Trim(),
            Status = "Pending",
            RequestedBy = requestedBy.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool Approve(string approvedBy)
    {
        if (Status != "Pending") return false;
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = DateTimeOffset.UtcNow;
        Status = "Approved";
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool Cancel(string reason)
    {
        if (Status is "Received" or "Cancelled") return false;
        Reason = reason.Trim();
        Status = "Cancelled";
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
