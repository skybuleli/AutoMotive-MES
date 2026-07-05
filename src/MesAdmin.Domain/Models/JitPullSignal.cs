using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// JIT 看板拉动状态。
/// </summary>
public enum JitPullStatus
{
    /// <summary>已创建（待仓库响应）</summary>
    Created = 0,
    /// <summary>已送达（仓库已配送）</summary>
    Delivered = 1,
    /// <summary>已取消</summary>
    Cancelled = 2
}

/// <summary>
/// JIT 看板拉动信号（T1.4 物料齐套检查 / T1.14 JIT 看板拉动）。
/// 当线边库存不足时，系统自动生成电子看板信号推送给仓库 PDA，
/// 仓库备料送达扫码确认，全流程时间戳。
/// </summary>
[MemoryPackable]
public partial class JitPullSignal
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

    /// <summary>短缺数量</summary>
    public double ShortageQuantity { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>当前状态</summary>
    public JitPullStatus Status { get; set; } = JitPullStatus.Created;

    /// <summary>创建时间（缺料触发时间）</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>目标工位</summary>
    public string? TargetStation { get; set; }

    /// <summary>送达确认时间</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>送达确认操作员</summary>
    public string? DeliveredBy { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    public static JitPullSignal Create(
        Ulid orderId,
        string orderNumber,
        string materialCode,
        string materialName,
        double shortageQuantity,
        string unit,
        string? targetStation = null)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            throw new ArgumentException("物料编码不能为空", nameof(materialCode));

        if (string.IsNullOrWhiteSpace(materialName))
            throw new ArgumentException("物料名称不能为空", nameof(materialName));

        if (shortageQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(shortageQuantity), "短缺数量必须大于 0");

        return new JitPullSignal
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            ShortageQuantity = shortageQuantity,
            Unit = unit.Trim(),
            Status = JitPullStatus.Created,
            TargetStation = targetStation?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>标记为已送达</summary>
    public void Deliver(string deliveredBy, DateTimeOffset at)
    {
        if (Status != JitPullStatus.Created)
            throw new InvalidOperationException($"JIT 信号 {Id} 状态为 {Status}，无法送达");

        if (string.IsNullOrWhiteSpace(deliveredBy))
            throw new ArgumentException("送达确认操作员不能为空", nameof(deliveredBy));

        DeliveredBy = deliveredBy.Trim();
        DeliveredAt = at;
        Status = JitPullStatus.Delivered;
    }

    /// <summary>取消拉动</summary>
    public void Cancel(string reason)
    {
        if (Status != JitPullStatus.Created)
            throw new InvalidOperationException($"JIT 信号 {Id} 状态为 {Status}，无法取消");

        Remarks = reason?.Trim();
        Status = JitPullStatus.Cancelled;
    }
}
