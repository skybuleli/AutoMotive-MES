using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 生产工单状态机。
/// Created → Released → InProgress → Completed → Closed
/// </summary>
public enum OrderStatus
{
    Created = 0,
    Released = 1,
    InProgress = 2,
    Completed = 3,
    Closed = 4
}

/// <summary>
/// ESP 总成生产工单（聚合根）。
/// 对应 PRD M01 — 管理工单从 ERP 下达到成品入库的完整生命周期。
/// 主键使用 Ulid（可排序 UUID），禁止 Guid.NewGuid / 自增 ID。
/// </summary>
[MemoryPackable]
public partial class ProductionOrder
{
    /// <summary>主键（Ulid，存入 PG uuid 列）</summary>
    public Ulid Id { get; set; }

    /// <summary>工单号 WO-YYYYMMDD-NNNN</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>产品编码 ESP-9.0 / ESP-9.1</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>工单状态</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Created;

    /// <summary>优先级（1=正常, 2=紧急插单）</summary>
    public short Priority { get; set; } = 1;

    /// <summary>工艺路线 Id</summary>
    public Ulid RoutingId { get; set; }

    /// <summary>BOM 版本</summary>
    public string BomVersion { get; set; } = string.Empty;

    /// <summary>计划数量</summary>
    public int PlannedQuantity { get; set; }

    /// <summary>合格数量（完工时统计）</summary>
    public int QualifiedQuantity { get; set; }

    /// <summary>不良数量</summary>
    public int DefectiveQuantity { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>完工时间</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
