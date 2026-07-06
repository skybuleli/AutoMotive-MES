using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 检验计划（T2.1/T2.4）。
/// 定义各阶段（IQC/IPQC/OQC）需要检验的特性、抽样频率、AQL、控制限等。
/// ESP-9.0/9.1 产线按产品编码 + 版本管理控制计划。
/// </summary>
[MemoryPackable]
public partial class InspectionPlan
{
    public Ulid Id { get; set; }

    /// <summary>计划名称（如 "ESP-9.0 IPQC 控制计划"）</summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>计划版本</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>适用产品编码（ESP-9.0 / ESP-9.1，空=通用）</summary>
    public string? ProductCode { get; set; }

    /// <summary>适用检验阶段</summary>
    public InspectionStage Stage { get; set; }

    /// <summary>适用工站（IPQC 时指定，空=全部）</summary>
    public int? Station { get; set; }

    /// <summary>抽样频率（如 "每50件抽5"、"每班次1次"）</summary>
    public string SamplingFrequency { get; set; } = string.Empty;

    /// <summary>抽样数量</summary>
    public int SampleSize { get; set; }

    /// <summary>AQL 值（如 0.65）</summary>
    public double? AqlValue { get; set; }

    /// <summary>检验水平（I / II / III）</summary>
    public string? InspectionLevel { get; set; }

    /// <summary>合格判定数 (Ac)</summary>
    public int AcceptNumber { get; set; }

    /// <summary>不合格判定数 (Re)</summary>
    public int RejectNumber { get; set; }

    /// <summary>是否启用 SPC 控制图</summary>
    public bool EnableSpcChart { get; set; }

    /// <summary>SPC 子组大小（通常 n=5）</summary>
    public int SpcSubgroupSize { get; set; } = 5;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>检验特性清单</summary>
    public List<PlanCharacteristic> Characteristics { get; set; } = [];

    /// <summary>生效日期</summary>
    public DateTimeOffset EffectiveDate { get; set; }

    /// <summary>失效日期</summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public static InspectionPlan Create(
        string planName,
        string version,
        InspectionStage stage,
        string samplingFrequency,
        int sampleSize,
        int acceptNumber,
        int rejectNumber,
        DateTimeOffset effectiveDate)
    {
        if (string.IsNullOrWhiteSpace(planName))
            throw new ArgumentException("计划名称不能为空", nameof(planName));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("版本不能为空", nameof(version));
        if (sampleSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleSize), "抽样数量必须大于 0");

        return new InspectionPlan
        {
            Id = Ulid.NewUlid(),
            PlanName = planName.Trim(),
            Version = version.Trim(),
            Stage = stage,
            SamplingFrequency = samplingFrequency.Trim(),
            SampleSize = sampleSize,
            AcceptNumber = acceptNumber,
            RejectNumber = rejectNumber,
            EffectiveDate = effectiveDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void AddCharacteristic(PlanCharacteristic c)
    {
        if (Characteristics.Any(x => x.CharacteristicCode == c.CharacteristicCode))
            throw new InvalidOperationException($"特性 {c.CharacteristicCode} 已存在");
        Characteristics.Add(c);
    }
}

/// <summary>
/// 控制计划中的单个检验特性定义。
/// 包含规格限、控制限、测量方法等。
/// </summary>
[MemoryPackable]
public partial class PlanCharacteristic
{
    /// <summary>特性编码（如 TOR-M6、DIM-01、LEAK-01）</summary>
    public string CharacteristicCode { get; set; } = string.Empty;

    /// <summary>特性名称</summary>
    public string CharacteristicName { get; set; } = string.Empty;

    /// <summary>特性类型（计量型/计数型）</summary>
    public CharacteristicType Type { get; set; } = CharacteristicType.Variable;

    /// <summary>标准值（目标值）</summary>
    public double StandardValue { get; set; }

    /// <summary>规格上限 (USL)</summary>
    public double? UpperSpecLimit { get; set; }

    /// <summary>规格下限 (LSL)</summary>
    public double? LowerSpecLimit { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>测量工具/设备</summary>
    public string? MeasurementTool { get; set; }

    /// <summary>是否关键特性（CC/SC）</summary>
    public bool IsCritical { get; set; }

    /// <summary>是否启用 SPC 控制图</summary>
    public bool EnableSpc { get; set; }

    /// <summary>控制上限 (UCL) — X̄ 图</summary>
    public double? UpperControlLimit { get; set; }

    /// <summary>控制下限 (LCL) — X̄ 图</summary>
    public double? LowerControlLimit { get; set; }

    /// <summary>中心线 (CL) — X̄ 图</summary>
    public double? CenterLine { get; set; }

    /// <summary>R 图控制上限 (UCL-R)</summary>
    public double? UpperRangeLimit { get; set; }

    /// <summary>R 图中心线 (CL-R)</summary>
    public double? CenterRange { get; set; }

    public static PlanCharacteristic CreateVariable(
        string code, string name, double std, string unit,
        double? usl = null, double? lsl = null,
        bool isCritical = false, bool enableSpc = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("特性编码不能为空", nameof(code));

        return new PlanCharacteristic
        {
            CharacteristicCode = code.Trim(),
            CharacteristicName = name.Trim(),
            Type = CharacteristicType.Variable,
            StandardValue = std,
            Unit = unit.Trim(),
            UpperSpecLimit = usl,
            LowerSpecLimit = lsl,
            IsCritical = isCritical,
            EnableSpc = enableSpc,
        };
    }

    public static PlanCharacteristic CreateAttribute(
        string code, string name, string unit)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("特性编码不能为空", nameof(code));

        return new PlanCharacteristic
        {
            CharacteristicCode = code.Trim(),
            CharacteristicName = name.Trim(),
            Type = CharacteristicType.Attribute,
            Unit = unit.Trim(),
        };
    }

    /// <summary>
    /// 设置 X̄-R 控制图参数。
    /// 使用 A2/D3/D4 常数表计算控制限：
    ///   UCL = X̄̄ + A2 × R̄
    ///   LCL = X̄̄ - A2 × R̄
    ///   UCL-R = D4 × R̄, LCL-R = D3 × R̄
    /// </summary>
    public void SetXbarRControlLimits(double grandMean, double meanRange, int subgroupSize)
    {
        var constants = XbarRConstants.Get(subgroupSize);
        CenterLine = grandMean;
        UpperControlLimit = grandMean + constants.A2 * meanRange;
        LowerControlLimit = grandMean - constants.A2 * meanRange;
        CenterRange = meanRange;
        UpperRangeLimit = constants.D4 * meanRange;
    }
}

/// <summary>检验特性类型</summary>
public enum CharacteristicType
{
    /// <summary>计量型（连续数值，如扭矩、压力）</summary>
    Variable = 0,
    /// <summary>计数型（合格/不合格，如外观、功能）</summary>
    Attribute = 1,
}

/// <summary>X̄-R 控制图常数表（ASTM STP 15D）</summary>
public static class XbarRConstants
{
    public record XbarRFactor(double A2, double D3, double D4);

    private static readonly Dictionary<int, XbarRFactor> Factors = new()
    {
        { 2, new(1.880, 0, 3.267) },
        { 3, new(1.023, 0, 2.575) },
        { 4, new(0.729, 0, 2.282) },
        { 5, new(0.577, 0, 2.115) },
        { 6, new(0.483, 0, 2.004) },
        { 7, new(0.419, 0.076, 1.924) },
        { 8, new(0.373, 0.136, 1.864) },
        { 9, new(0.337, 0.184, 1.816) },
        { 10, new(0.308, 0.223, 1.777) },
    };

    public static XbarRFactor Get(int subgroupSize)
        => Factors.TryGetValue(subgroupSize, out var f)
            ? f
            : throw new ArgumentOutOfRangeException(nameof(subgroupSize),
                $"不支持子组大小 {subgroupSize}，仅支持 2-10");

    /// <summary>是否为有效的子组大小</summary>
    public static bool IsValidSubgroupSize(int n) => n >= 2 && n <= 10;
}
