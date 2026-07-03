using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 物料批次状态。
/// </summary>
public enum MaterialBatchStatus
{
    /// <summary>已入库（待检/待用）</summary>
    Received = 0,

    /// <summary>检验合格（可用）</summary>
    Qualified = 1,

    /// <summary>检验不合格（隔离）</summary>
    Rejected = 2,

    /// <summary>已消耗完</summary>
    Consumed = 3
}

/// <summary>
/// 来料批次（T1.12 来料扫码入库）。
/// GS1-128 条码解析：物料编码(14位) + 批次号( variable) + 数量 + 生产日期。
/// 合格供应商名录校验通过后写入 material_batches 表。
/// </summary>
[MemoryPackable]
public partial class MaterialBatch
{
    public Ulid Id { get; set; }

    /// <summary>物料编码（如 ECU-ESP9、VALVE-SOLENOID）</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>批次号</summary>
    public string BatchNumber { get; set; } = string.Empty;

    /// <summary>供应商编码</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>供应商名称</summary>
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>接收数量</summary>
    public double ReceivedQuantity { get; set; }

    /// <summary>剩余数量（消耗反冲时扣减）</summary>
    public double RemainingQuantity { get; set; }

    /// <summary>单位（PCS / SET / KG）</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否关键物料（ECU芯片/电磁阀/压力传感器）</summary>
    public bool IsCritical { get; set; }

    /// <summary>批次状态</summary>
    public MaterialBatchStatus Status { get; set; } = MaterialBatchStatus.Received;

    /// <summary>生产日期（GS1-128 解析）</summary>
    public DateTimeOffset? ProductionDate { get; set; }

    /// <summary>入库时间</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public static MaterialBatch Create(
        string materialCode,
        string materialName,
        string batchNumber,
        string supplierCode,
        string supplierName,
        double receivedQuantity,
        string unit,
        bool isCritical,
        DateTimeOffset? productionDate = null)
    {
        ValidateInput(materialCode, materialName, batchNumber, supplierCode, receivedQuantity, unit);

        return new MaterialBatch
        {
            Id = Ulid.NewUlid(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            BatchNumber = batchNumber.Trim(),
            SupplierCode = supplierCode.Trim(),
            SupplierName = supplierName.Trim(),
            ReceivedQuantity = receivedQuantity,
            RemainingQuantity = receivedQuantity,
            Unit = unit.Trim(),
            IsCritical = isCritical,
            Status = MaterialBatchStatus.Received,
            ProductionDate = productionDate,
            ReceivedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>标记检验合格（可用）</summary>
    public void Qualify()
    {
        if (Status != MaterialBatchStatus.Received)
            throw new InvalidOperationException($"批次 {BatchNumber} 状态为 {Status}，无法标记合格");
        Status = MaterialBatchStatus.Qualified;
    }

    /// <summary>标记检验不合格（隔离）</summary>
    public void Reject()
    {
        if (Status is not MaterialBatchStatus.Received and not MaterialBatchStatus.Qualified)
            throw new InvalidOperationException($"批次 {BatchNumber} 状态为 {Status}，无法标记不合格");
        Status = MaterialBatchStatus.Rejected;
    }

    /// <summary>消耗数量（投料绑定 / 物料反冲时扣减）</summary>
    public void Consume(double quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "消耗数量必须大于 0");

        if (Status != MaterialBatchStatus.Qualified)
            throw new InvalidOperationException($"批次 {BatchNumber} 未检验合格，不可消耗");

        if (quantity > RemainingQuantity)
            throw new InvalidOperationException($"批次 {BatchNumber} 剩余 {RemainingQuantity}，不足以消耗 {quantity}");

        RemainingQuantity -= quantity;
        if (RemainingQuantity <= 0)
            Status = MaterialBatchStatus.Consumed;
    }

    private static void ValidateInput(
        string materialCode, string materialName, string batchNumber,
        string supplierCode, double receivedQuantity, string unit)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(materialName))
            throw new ArgumentException("物料名称不能为空", nameof(materialName));
        if (string.IsNullOrWhiteSpace(batchNumber))
            throw new ArgumentException("批次号不能为空", nameof(batchNumber));
        if (string.IsNullOrWhiteSpace(supplierCode))
            throw new ArgumentException("供应商编码不能为空", nameof(supplierCode));
        if (receivedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "接收数量必须大于 0");
        if (string.IsNullOrWhiteSpace(unit))
            throw new ArgumentException("单位不能为空", nameof(unit));
    }
}
