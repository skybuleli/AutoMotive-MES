using FastEndpoints;
using MemoryPack;

namespace MesAdmin.Application.Events;

/// <summary>
/// 工单已开工领域事件。
/// 发布后由 Saga 订阅者触发 Cleipnir 编排；审计/推送等可并行订阅而不改动命令 handler。
/// </summary>
[MemoryPackable]
public sealed partial record OrderStartedEvent(Ulid OrderId, string OrderNumber) : IEvent;
