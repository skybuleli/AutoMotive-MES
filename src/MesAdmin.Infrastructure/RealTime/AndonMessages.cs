using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// Andon 报警事件创建消息（T2.21）。
/// 由 AndonReactivePipeline 发布，AndonHub 订阅后推送前端。
/// </summary>
[MemoryPackable]
public sealed partial record AndonEventCreatedMessage(
    string EventId,
    string EventNumber,
    string EquipmentCode,
    int Station,
    AndonAlarmType AlarmType,
    AndonSeverity Severity,
    AndonEventStatus Status,
    string Description,
    double ProcessValue,
    string? ProcessTag,
    double? UpperLimit,
    double? LowerLimit,
    DateTimeOffset OccurredAt);

/// <summary>
/// 报警已确认消息。
/// </summary>
[MemoryPackable]
public sealed partial record AndonEventAcknowledgedMessage(
    string EventId,
    string AcknowledgedBy,
    DateTimeOffset AcknowledgedAt);

/// <summary>
/// 报警升级消息（L1→L2 或 L2→L3）。
/// </summary>
[MemoryPackable]
public sealed partial record AndonEventEscalatedMessage(
    string EventId,
    int NewLevel,
    DateTimeOffset EscalatedAt);

/// <summary>
/// 报警已解决消息。
/// </summary>
[MemoryPackable]
public sealed partial record AndonEventResolvedMessage(
    string EventId,
    string ResolvedBy,
    string Resolution,
    DateTimeOffset ResolvedAt);

/// <summary>
/// 报警已关闭消息。
/// </summary>
[MemoryPackable]
public sealed partial record AndonEventClosedMessage(
    string EventId,
    string CloseRemarks,
    DateTimeOffset ClosedAt);
