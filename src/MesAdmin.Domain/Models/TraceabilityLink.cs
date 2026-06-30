using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 追溯层级。
/// L1 车辆(VIN) / L2 ESP总成(S/N) / L3 零部件 / L4 原材料
/// </summary>
public enum TraceabilityLevel
{
    /// <summary>L1 车辆 — VIN 码（17位），整车厂装车后回传</summary>
    Vehicle = 1,

    /// <summary>L2 ESP 总成 — 产品序列号 ESP9-YYYYMMDD-NNNNNN</summary>
    Assembly = 2,

    /// <summary>L3 关键零部件 — ECU/HCU/电机 S/N + 电磁阀批次</summary>
    Component = 3,

    /// <summary>L4 原材料 — 阀体铝合金批次 / PCB 板材批次</summary>
    Material = 4
}

/// <summary>
/// 全链路追溯链接（4 级双向追溯）。
/// 对应 PRD M04 — ESP 安全件 24 小时内 VIN→总成→零部件→原材料全链路追溯。
/// 写入时使用 Effect.AtLeastOnce + DB 唯一约束防重复；哈希链保证不可篡改。
/// </summary>
[MemoryPackable]
public partial class TraceabilityLink
{
    public Ulid Id { get; set; }

    /// <summary>追溯层级</summary>
    public TraceabilityLevel Level { get; set; }

    /// <summary>VIN 码或产品序列号</summary>
    public string VinOrSerial { get; set; } = string.Empty;

    /// <summary>所属工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>零部件批次（电磁阀/ECU 批次）</summary>
    public string ComponentBatch { get; set; } = string.Empty;

    /// <summary>原材料批次（阀体铝合金批次）</summary>
    public string MaterialBatch { get; set; } = string.Empty;

    /// <summary>前一条记录哈希（哈希链审计，保证追溯链不可篡改）</summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>本条记录哈希</summary>
    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
