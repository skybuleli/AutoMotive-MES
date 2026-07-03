using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// PLC 数据快照（T2.12/T2.13）。
/// 每台设备每 500ms（或 100Hz）采集一次，经 Channel 传入 R3 管道。
/// 零分配：帧解析阶段用 ref struct，最终封装为此 MemoryPackable 对象进入 Channel。
/// </summary>
[MemoryPackable]
public partial class PlcSnapshot
{
    /// <summary>设备编码（如 EQ-TQ-01）</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>采集时间戳（UTC）</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>设备状态</summary>
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Idle;

    /// <summary>累计循环次数（加工件数）</summary>
    public long CycleCount { get; set; }

    /// <summary>合格件数</summary>
    public long GoodCount { get; set; }

    /// <summary>不良件数</summary>
    public long DefectCount { get; set; }

    /// <summary>运行时长（毫秒，自本次开机起）</summary>
    public long RunTimeMs { get; set; }

    /// <summary>当前过程值（扭矩/压力/CAN 延迟等，视设备而定）</summary>
    public double ProcessValue { get; set; }

    /// <summary>过程值标签（如 "Torque-M6-FL" / "HydraulicPressure"）</summary>
    public string ProcessTag { get; set; } = string.Empty;

    public static PlcSnapshot Create(
        string equipmentCode,
        DateTimeOffset timestamp,
        EquipmentStatus status,
        long cycleCount,
        long goodCount,
        long defectCount,
        long runTimeMs,
        double processValue,
        string processTag)
        => new()
        {
            EquipmentCode = equipmentCode,
            Timestamp = timestamp,
            Status = status,
            CycleCount = cycleCount,
            GoodCount = goodCount,
            DefectCount = defectCount,
            RunTimeMs = runTimeMs,
            ProcessValue = processValue,
            ProcessTag = processTag,
        };
}
