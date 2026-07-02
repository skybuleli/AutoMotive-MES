using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 过程参数记录（每工序可含多个参数，如扭矩、压力、角度等）。
/// </summary>
[MemoryPackable]
public partial class ProcessParameter
{
    /// <summary>参数编码</summary>
    public string ParameterCode { get; set; } = string.Empty;

    /// <summary>参数名称</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>标准值（工艺设定值）</summary>
    public double StandardValue { get; set; }

    /// <summary>上限（公差上限）</summary>
    public double? UpperLimit { get; set; }

    /// <summary>下限（公差下限）</summary>
    public double? LowerLimit { get; set; }

    /// <summary>实际值（设备采集）</summary>
    public double? ActualValue { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否合格（Null=未判定）</summary>
    public bool? IsPass { get; set; }

    /// <summary>采集时间</summary>
    public DateTimeOffset? RecordedAt { get; set; }

    public static ProcessParameter Create(
        string parameterCode,
        string parameterName,
        double standardValue,
        string unit,
        double? upperLimit = null,
        double? lowerLimit = null)
    {
        if (string.IsNullOrWhiteSpace(parameterCode))
            throw new ArgumentException("参数编码不能为空", nameof(parameterCode));

        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentException("参数名称不能为空", nameof(parameterName));

        return new ProcessParameter
        {
            ParameterCode = parameterCode.Trim(),
            ParameterName = parameterName.Trim(),
            StandardValue = standardValue,
            Unit = unit.Trim(),
            UpperLimit = upperLimit,
            LowerLimit = lowerLimit,
        };
    }

    public void Record(double actualValue, DateTimeOffset at)
    {
        ActualValue = actualValue;
        RecordedAt = at;

        if (UpperLimit.HasValue && actualValue > UpperLimit.Value)
        {
            IsPass = false;
            return;
        }

        if (LowerLimit.HasValue && actualValue < LowerLimit.Value)
        {
            IsPass = false;
            return;
        }

        IsPass = true;
    }
}
