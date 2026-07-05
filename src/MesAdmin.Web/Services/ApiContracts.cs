namespace MesAdmin.Web.Services;

/// <summary>Web 端 API 响应 DTO（镜像 API 端 DTO，不依赖 API 项目）</summary>

public record OrderSummary(
    string Id, string OrderNumber, string ProductCode, string Status,
    short Priority, string RoutingId, string BomVersion,
    int PlannedQuantity, int QualifiedQuantity, int DefectiveQuantity,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public record OrderDetail(
    OrderSummary Order, bool CanRelease, bool CanStart, bool CanComplete, bool CanClose);

public record OperationDto(
    string Id, string OrderId, int Sequence, int Station,
    string OperationCode, string OperationName, string Status,
    string? OperatorId, string? EquipmentId,
    DateTimeOffset? StartAt, DateTimeOffset? EndAt, string? FailureReason);

public record CreateOrderBody(string ProductCode, string BomVersion, string RoutingId, int PlannedQuantity, short Priority);
public record ChangeStatusBody(string Status);
public record CompleteOrderBody(int QualifiedQuantity, int DefectiveQuantity);
public record ReportOperationBody(string OperatorId, string EquipmentId);

public record TraceabilityLinkDto(
    string Id,
    int Level,
    string LevelName,
    string VinOrSerial,
    string OrderId,
    string ComponentBatch,
    string MaterialBatch,
    string PreviousHash,
    string Hash,
    DateTimeOffset CreatedAt,
    bool HashVerified);

/// <summary>T1.4 齐套检查响应 DTO。</summary>
public record KitCheckResponse(
    bool IsPassed,
    List<KitCheckItemResponse> Items,
    List<string> JitPullSignalIds);

/// <summary>T1.4 齐套检查单项结果 DTO。</summary>
public record KitCheckItemResponse(
    string MaterialCode,
    string MaterialName,
    double RequiredQuantity,
    double AvailableQuantity,
    double ShortageQuantity,
    string Unit,
    bool IsCritical);

/// <summary>工单状态枚举字符串（与 API 端 OrderStatus.ToString() 一致）</summary>
public static class OrderStatusNames
{
    public const string Created = "Created";
    public const string Released = "Released";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Closed = "Closed";
}
