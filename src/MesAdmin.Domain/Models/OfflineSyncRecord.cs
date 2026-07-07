using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 终端离线同步记录（T4.4）。
/// 工位终端断网期间本地缓存的待同步操作。
/// 终端恢复连接后，通过批量上传接口提交离线期间产生的操作记录。
/// </summary>
[MemoryPackable]
public partial class OfflineSyncRecord
{
    /// <summary>主键（Ulid，按创建时间可排序）</summary>
    public Ulid Id { get; set; }

    /// <summary>终端标识（工作站唯一编码，如 WS-03-01）</summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>操作类型（如 ScanBarcode / RecordMeasurement / CompleteOperation / BindMaterial 等）</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>操作载荷（JSON，包含操作所需的全部参数）</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>关联实体类型（如 ProductionOrder / QualityRecord / TraceabilityLink）</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>关联实体 Id（已知时填写，用于冲突检测）</summary>
    public string? EntityId { get; set; }

    /// <summary>操作时间戳（终端本地时间，用于冲突解决）</summary>
    public DateTimeOffset OperationTimestamp { get; set; }

    /// <summary>同步状态：Pending / Syncing / Synced / Conflict / Failed</summary>
    public string Status { get; set; } = OfflineSyncStatus.Pending;

    /// <summary>重试次数</summary>
    public int RetryCount { get; set; }

    /// <summary>最近一次错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>冲突解决方案（终端用户在冲突时选择：use_local / use_server / manual）</summary>
    public string? ConflictResolution { get; set; }

    /// <summary>记录创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最近一次同步尝试时间</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>同步完成时间</summary>
    public DateTimeOffset? SyncedAt { get; set; }

    public static OfflineSyncRecord Create(
        string terminalId,
        string operationType,
        string payload,
        string entityType,
        string? entityId = null,
        DateTimeOffset? operationTimestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        return new OfflineSyncRecord
        {
            Id = Ulid.NewUlid(),
            TerminalId = terminalId.Trim(),
            OperationType = operationType.Trim(),
            Payload = payload,
            EntityType = entityType.Trim(),
            EntityId = entityId?.Trim(),
            OperationTimestamp = operationTimestamp ?? DateTimeOffset.UtcNow,
            Status = OfflineSyncStatus.Pending,
            RetryCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>离线同步记录状态常量</summary>
public static class OfflineSyncStatus
{
    public const string Pending = "Pending";
    public const string Syncing = "Syncing";
    public const string Synced = "Synced";
    public const string Conflict = "Conflict";
    public const string Failed = "Failed";

    /// <summary>还可以重试的状态（不含 Syncing，避免 ConsumerLoop 和后台轮询竞争）</summary>
    public static readonly string[] Retryable =
        [Pending, Conflict, Failed];

    /// <summary>终端上传时允许的状态</summary>
    public static readonly string[] Uploadable =
        [Pending, Conflict, Failed];
}
