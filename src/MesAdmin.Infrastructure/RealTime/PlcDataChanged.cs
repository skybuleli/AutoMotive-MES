using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Infrastructure.RealTime;

/// <summary>
/// PLC 数据变更消息（T2.14，MessagePipe 进程内消息）。
/// 由 OeeReactivePipeline 发布，DashboardHub 订阅后推送给前端。
/// </summary>
[MemoryPackable]
public sealed partial record PlcDataChanged(OeeRecord Oee);

/// <summary>
/// Channel 健康度消息（T2.15，10s 推送）。
/// </summary>
[MemoryPackable]
public sealed partial record ChannelHealthMessage(
    string EquipmentCount,
    long Written,
    long Read,
    double Utilization);
