using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// SAP 工单状态同步记录（T3.14 工单双向同步）。
/// 工单状态变更（Release/Complete/Close）时创建，由后台服务轮询同步 SAP。
/// </summary>
[MemoryPackable]
public partial class SapOrderSyncRecord
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>SAP 外部工单号</summary>
    public string ExternalOrderNumber { get; set; } = string.Empty;

    /// <summary>同步的工单状态</summary>
    public OrderStatus Status { get; set; }

    /// <summary>合格数量（Complete 时同步）</summary>
    public int QualifiedQuantity { get; set; }

    /// <summary>是否已同步 SAP</summary>
    public bool SapSynced { get; set; }

    /// <summary>SAP 返回的凭证号</summary>
    public string? SapDocumentNumber { get; set; }

    /// <summary>同步错误信息</summary>
    public string? SyncError { get; set; }

    /// <summary>同步时间</summary>
    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static SapOrderSyncRecord Create(
        Ulid orderId, string orderNumber, string externalOrderNumber,
        OrderStatus status, int qualifiedQuantity = 0)
    {
        if (string.IsNullOrWhiteSpace(externalOrderNumber))
            throw new ArgumentException("SAP 外部工单号不能为空", nameof(externalOrderNumber));

        return new SapOrderSyncRecord
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            ExternalOrderNumber = externalOrderNumber.Trim(),
            Status = status,
            QualifiedQuantity = qualifiedQuantity,
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
