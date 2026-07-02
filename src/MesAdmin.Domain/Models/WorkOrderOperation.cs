using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 工序执行状态。
/// Pending=待执行 / InProgress=执行中 / Completed=已完成 / Failed=失败 / Skipped=跳过
/// </summary>
public enum OperationStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// 工单工序执行记录（31 工序 × 7 站）。
/// 每个 ProductionOrder 创建时初始化 31 条工序记录，Saga 按工艺路线依次推进。
/// </summary>
[MemoryPackable]
public partial class WorkOrderOperation
{
    /// <summary>主键（Ulid）</summary>
    public Ulid Id { get; set; }

    /// <summary>所属工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工序序号（1-31，对应工艺路线中的顺序）</summary>
    public int Sequence { get; set; }

    /// <summary>工站编号（1-7）</summary>
    public int Station { get; set; }

    /// <summary>工序编码（如 TQ-01、HY-03）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>工序名称（如 "M6 螺栓拧紧"、"液压功能测试"）</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>工序状态</summary>
    public OperationStatus Status { get; set; } = OperationStatus.Pending;

    /// <summary>操作员工号</summary>
    public string? OperatorId { get; set; }

    /// <summary>设备编号</summary>
    public string? EquipmentId { get; set; }

    /// <summary>开始时间</summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>结束时间</summary>
    public DateTimeOffset? EndAt { get; set; }

    /// <summary>过程参数列表</summary>
    public List<ProcessParameter> Parameters { get; set; } = [];

    /// <summary>异常原因（失败时记录）</summary>
    public string? FailureReason { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static WorkOrderOperation Create(
        Ulid orderId,
        int sequence,
        int station,
        string operationCode,
        string operationName)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "工序序号必须大于 0");

        if (station is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(station), "工站编号仅支持 1-7");

        if (string.IsNullOrWhiteSpace(operationCode))
            throw new ArgumentException("工序编码不能为空", nameof(operationCode));

        if (string.IsNullOrWhiteSpace(operationName))
            throw new ArgumentException("工序名称不能为空", nameof(operationName));

        return new WorkOrderOperation
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            Sequence = sequence,
            Station = station,
            OperationCode = operationCode.Trim(),
            OperationName = operationName.Trim(),
            Status = OperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Start(string operatorId, string equipmentId, DateTimeOffset at)
    {
        if (Status != OperationStatus.Pending)
            throw new InvalidOperationException($"工序 {OperationCode} 状态为 {Status}，无法开始");

        OperatorId = operatorId;
        EquipmentId = equipmentId;
        StartAt = at;
        Status = OperationStatus.InProgress;
    }

    public void Complete(DateTimeOffset at)
    {
        if (Status != OperationStatus.InProgress)
            throw new InvalidOperationException($"工序 {OperationCode} 状态为 {Status}，无法完工");

        EndAt = at;
        Status = OperationStatus.Completed;
    }

    public void Fail(string reason, DateTimeOffset at)
    {
        EndAt = at;
        Status = OperationStatus.Failed;
        FailureReason = reason;
    }
}
