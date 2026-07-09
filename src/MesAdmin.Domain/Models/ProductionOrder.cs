using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 生产工单状态机。
/// Created → Released → InProgress → Completed → Closed
/// Created/Released → Cancelled（生产开始前可取消，Saga 尚未启动）
/// </summary>
public enum OrderStatus
{
    Created = 0,
    Released = 1,
    InProgress = 2,
    Completed = 3,
    Closed = 4,
    Cancelled = 5
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

    /// <summary>计划开始时间</summary>
    public DateTimeOffset? PlannedStartAt { get; set; }

    /// <summary>计划结束时间</summary>
    public DateTimeOffset? PlannedEndAt { get; set; }

    /// <summary>实际开始时间</summary>
    public DateTimeOffset? ActualStartAt { get; set; }

    /// <summary>实际结束时间</summary>
    public DateTimeOffset? ActualEndAt { get; set; }

    /// <summary>产线 Id</summary>
    public Ulid? LineId { get; set; }

    /// <summary>工作中心编号</summary>
    public string? WorkCenterId { get; set; }

    /// <summary>班次（早/中/晚）</summary>
    public string? Shift { get; set; }

    /// <summary>来源系统（SAP / 手动）</summary>
    public string? SourceSystem { get; set; }

    /// <summary>外部系统工单号（SAP 工单号）</summary>
    public string? ExternalOrderNumber { get; set; }

    /// <summary>取消原因</summary>
    public string? CancelReason { get; set; }

    public bool CanRelease => Status == OrderStatus.Created;
    public bool CanStart => Status == OrderStatus.Released;
    public bool CanComplete => Status == OrderStatus.InProgress;
    public bool CanClose => Status == OrderStatus.Completed;
    /// <summary>生产开始前（Created/Released，Saga 未启动）可取消。</summary>
    public bool CanCancel => Status is OrderStatus.Created or OrderStatus.Released;

    public static ProductionOrder Create(
        Ulid id,
        string orderNumber,
        string productCode,
        Ulid routingId,
        string bomVersion,
        int plannedQuantity,
        short priority,
        DateTimeOffset createdAt)
    {
        ValidateCreateInput(orderNumber, productCode, bomVersion, plannedQuantity, priority);

        return new ProductionOrder
        {
            Id = id,
            OrderNumber = orderNumber,
            ProductCode = NormalizeProductCode(productCode),
            RoutingId = routingId,
            BomVersion = bomVersion.Trim(),
            PlannedQuantity = plannedQuantity,
            Priority = priority,
            CreatedAt = createdAt,
            Status = OrderStatus.Created,
        };
    }

    public void Release()
    {
        EnsureStatus(OrderStatus.Created, "只有 Created 状态才能转为 Released");
        Status = OrderStatus.Released;
    }

    public void Start()
    {
        EnsureStatus(OrderStatus.Released, "只有 Released 状态才能开工");
        Status = OrderStatus.InProgress;
    }

    public void Complete(int qualifiedQuantity, int defectiveQuantity, DateTimeOffset completedAt)
    {
        EnsureStatus(OrderStatus.InProgress, "只有 InProgress 状态才能完工");

        if (qualifiedQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(qualifiedQuantity));

        if (defectiveQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(defectiveQuantity));

        if (qualifiedQuantity + defectiveQuantity > PlannedQuantity)
            throw new InvalidOperationException("完工数量不能超过计划数量");

        QualifiedQuantity = qualifiedQuantity;
        DefectiveQuantity = defectiveQuantity;
        CompletedAt = completedAt;
        Status = OrderStatus.Completed;
    }

    public void Close()
    {
        EnsureStatus(OrderStatus.Completed, "只有 Completed 状态才能关闭");
        Status = OrderStatus.Closed;
    }

    /// <summary>
    /// 取消工单（生产开始前）。仅 Created/Released 状态允许，
    /// 此时 Saga 尚未启动，无需补偿；InProgress 及之后不可取消。
    /// </summary>
    public void Cancel(string reason, DateTimeOffset cancelledAt)
    {
        if (!CanCancel)
            throw new InvalidOperationException($"工单状态为 {Status}，只有 Created/Released 状态才能取消");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("取消原因不能为空", nameof(reason));

        Status = OrderStatus.Cancelled;
        CancelReason = reason.Trim();
        ActualEndAt = cancelledAt;
    }

    private static void ValidateCreateInput(
        string orderNumber,
        string productCode,
        string bomVersion,
        int plannedQuantity,
        short priority)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("工单号不能为空", nameof(orderNumber));

        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("产品编码不能为空", nameof(productCode));

        var normalizedProductCode = NormalizeProductCode(productCode);
        if (normalizedProductCode is not ("ESP-9.0" or "ESP-9.1"))
            throw new ArgumentException("产品编码仅支持 ESP-9.0 / ESP-9.1", nameof(productCode));

        if (string.IsNullOrWhiteSpace(bomVersion))
            throw new ArgumentException("BOM 版本不能为空", nameof(bomVersion));

        if (plannedQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(plannedQuantity), "计划数量必须大于 0");

        if (priority is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(priority), "优先级仅支持 1 或 2");
    }

    private static string NormalizeProductCode(string productCode)
        => productCode.Trim().ToUpperInvariant();

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
            throw new InvalidOperationException(message);
    }
}
