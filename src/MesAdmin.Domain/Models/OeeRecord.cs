using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// OEE（设备综合效率）记录（T2.14）。
/// OEE = 可用率 × 性能率 × 良品率
/// 目标 85%~92%（PRD M05），低于 70% 报警。
/// </summary>
[MemoryPackable]
public partial class OeeRecord
{
    /// <summary>设备编码</summary>
    public string EquipmentCode { get; set; } = string.Empty;

    /// <summary>计算时间戳（UTC）</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>可用率（0~1）：实际运行时间 / 计划运行时间</summary>
    public double Availability { get; set; }

    /// <summary>性能率（0~1）：实际产量 / 理论产量（理想节拍 × 运行时间）</summary>
    public double Performance { get; set; }

    /// <summary>良品率（0~1）：合格件数 / 总件数</summary>
    public double Quality { get; set; }

    /// <summary>OEE 综合值（0~1）：Availability × Performance × Quality</summary>
    public double Oee { get; set; }

    /// <summary>OEE 等级（S&gt;85% / A 70-85% / B&lt;70%）</summary>
    public OeeGrade Grade { get; set; }

    public static OeeRecord Compute(
        string equipmentCode,
        DateTimeOffset timestamp,
        double availability,
        double performance,
        double quality)
    {
        // 钳制到 [0,1] 范围，防止异常 PLC 数据导致 &gt;100%
        availability = Math.Clamp(availability, 0, 1);
        performance = Math.Clamp(performance, 0, 1);
        quality = Math.Clamp(quality, 0, 1);

        var oee = availability * performance * quality;
        var grade = oee switch
        {
            >= 0.85 => OeeGrade.S,
            >= 0.70 => OeeGrade.A,
            _ => OeeGrade.B,
        };

        return new OeeRecord
        {
            EquipmentCode = equipmentCode,
            Timestamp = timestamp,
            Availability = availability,
            Performance = performance,
            Quality = quality,
            Oee = oee,
            Grade = grade,
        };
    }
}

/// <summary>OEE 等级</summary>
public enum OeeGrade
{
    /// <summary>优秀（≥85%）</summary>
    S = 0,

    /// <summary>达标（70%~85%）</summary>
    A = 1,

    /// <summary>待改进（&lt;70%）</summary>
    B = 2,
}
