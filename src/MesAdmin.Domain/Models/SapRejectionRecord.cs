using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// SAP Webhook 拒单回写状态。
/// </summary>
public enum RejectionWritebackStatus
{
    /// <summary>待回写 SAP</summary>
    Pending = 0,

    /// <summary>已成功回写 SAP</summary>
    WrittenBack = 1,

    /// <summary>回写失败（SAP 不可达等）</summary>
    Failed = 2
}

/// <summary>
/// SAP 工单拒单记录（T1.3）。
/// 当 SAP Webhook 校验失败（产品编码/BOM 版本/工艺路线版本不符）时，
/// 记录拒单原因并需回写 SAP，避免 ERP 侧工单状态不一致。
/// </summary>
[MemoryPackable]
public partial class SapRejectionRecord
{
    public Ulid Id { get; set; }

    /// <summary>SAP 外部工单号（原始请求中的 ExternalOrderNumber）</summary>
    public string? ExternalOrderNumber { get; set; }

    /// <summary>产品编码（原始请求）</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>BOM 版本（原始请求）</summary>
    public string BomVersion { get; set; } = string.Empty;

    /// <summary>工艺路线 Id（原始请求）</summary>
    public string RoutingId { get; set; } = string.Empty;

    /// <summary>计划数量（原始请求）</summary>
    public int PlannedQuantity { get; set; }

    /// <summary>拒单原因（产品编码不支持 / BOM 版本不符 / 工艺路线版本不符 等）</summary>
    public string RejectionReason { get; set; } = string.Empty;

    /// <summary>回写 SAP 状态</summary>
    public RejectionWritebackStatus WritebackStatus { get; set; } = RejectionWritebackStatus.Pending;

    /// <summary>回写失败的错误信息（WritebackStatus=Failed 时记录）</summary>
    public string? WritebackError { get; set; }

    /// <summary>回写时间</summary>
    public DateTimeOffset? WritebackAt { get; set; }

    /// <summary>拒单时间</summary>
    public DateTimeOffset RejectedAt { get; set; }

    public static SapRejectionRecord Create(
        string? externalOrderNumber,
        string productCode,
        string bomVersion,
        string routingId,
        int plannedQuantity,
        string rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
            throw new ArgumentException("拒单原因不能为空", nameof(rejectionReason));

        return new SapRejectionRecord
        {
            Id = Ulid.NewUlid(),
            ExternalOrderNumber = externalOrderNumber,
            ProductCode = productCode ?? string.Empty,
            BomVersion = bomVersion ?? string.Empty,
            RoutingId = routingId ?? string.Empty,
            PlannedQuantity = plannedQuantity,
            RejectionReason = rejectionReason.Trim(),
            WritebackStatus = RejectionWritebackStatus.Pending,
            RejectedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>标记已成功回写 SAP</summary>
    public void MarkWrittenBack(DateTimeOffset at)
    {
        WritebackStatus = RejectionWritebackStatus.WrittenBack;
        WritebackAt = at;
        WritebackError = null;
    }

    /// <summary>标记回写失败</summary>
    public void MarkFailed(string error, DateTimeOffset at)
    {
        WritebackStatus = RejectionWritebackStatus.Failed;
        WritebackError = error;
        WritebackAt = at;
    }
}
