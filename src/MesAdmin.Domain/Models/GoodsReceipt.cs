using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 成品入库单（T1.8 完工确认）。
/// 工单完工后由质量工程师审核放行，生成成品入库记录与追溯标签码。
/// 追溯标签码编码规则：ESP9-{工单日期}-{工单内序号}，含二维码（由 Web 端渲染）。
/// </summary>
[MemoryPackable]
public partial class GoodsReceipt
{
    public Ulid Id { get; set; }

    /// <summary>所属工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>产品编码（ESP-9.0 / ESP-9.1）</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>入库数量（= 合格数量）</summary>
    public int ReceivedQuantity { get; set; }

    /// <summary>质量审核人工号</summary>
    public string ReviewerId { get; set; } = string.Empty;

    /// <summary>追溯标签码（二维码内容：ESP9-YYYYMMDD-NNNN）</summary>
    public string TraceabilityLabelCode { get; set; } = string.Empty;

    /// <summary>是否已同步完工数量到 SAP</summary>
    public bool SapSynced { get; set; }

    /// <summary>SAP 同步时间</summary>
    public DateTimeOffset? SapSyncedAt { get; set; }

    /// <summary>入库时间</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public static GoodsReceipt Create(
        Ulid orderId,
        string orderNumber,
        string productCode,
        int receivedQuantity,
        string reviewerId,
        DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("工单号不能为空", nameof(orderNumber));

        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("产品编码不能为空", nameof(productCode));

        if (receivedQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "入库数量不能为负");

        if (string.IsNullOrWhiteSpace(reviewerId))
            throw new ArgumentException("审核人工号不能为空", nameof(reviewerId));

        // 追溯标签码：ESP9-{工单日期}-{工单号后4位}
        // 编码规则对齐 PRD M04：ESP9-YYYYMMDD-NNNNNN
        var datePart = receivedAt.ToString("yyyyMMdd");
        var orderSuffix = orderNumber.Length >= 4
            ? orderNumber[^4..]
            : orderNumber;
        var labelCode = $"ESP9-{datePart}-{orderSuffix}";

        return new GoodsReceipt
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            ProductCode = productCode.Trim().ToUpperInvariant(),
            ReceivedQuantity = receivedQuantity,
            ReviewerId = reviewerId.Trim(),
            TraceabilityLabelCode = labelCode,
            SapSynced = false,
            ReceivedAt = receivedAt,
        };
    }

    /// <summary>标记已同步完工数量到 SAP</summary>
    public void MarkSapSynced(DateTimeOffset at)
    {
        SapSynced = true;
        SapSyncedAt = at;
    }
}
