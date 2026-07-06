using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 检验阶段类型。
/// </summary>
public enum InspectionStage
{
    /// <summary>IQC 来料检验</summary>
    Iq = 0,
    /// <summary>IPQC 过程巡检</summary>
    Ipqc = 1,
    /// <summary>OQC 出货检验</summary>
    Oqc = 2,
    /// <summary>首件检验（合并 T1.5 FirstArticleInspection）</summary>
    FirstArticle = 3,
    /// <summary>在线 100% 功能测试</summary>
    OnlineTest = 4,
}

/// <summary>
/// 检验结果判定。
/// </summary>
public enum InspectionVerdict
{
    Pending = 0,
    Passed = 1,
    Failed = 2,
    ConditionalPass = 3, // 让步接收
}

/// <summary>
/// 质量检验记录（T2.1 基础模型）。
/// 覆盖 IQC 来料检验（T2.2）、IPQC 过程巡检（T2.4）、OQC 出货检验等全阶段。
/// 每份检验记录包含一个检验计划引用和多个实测值。
/// 使用 [MemoryPackable] 对齐 AGENTS.md 4.4 序列化铁律。
/// </summary>
[MemoryPackable]
public partial class QualityRecord
{
    public Ulid Id { get; set; }

    /// <summary>检验阶段</summary>
    public InspectionStage Stage { get; set; }

    /// <summary>关联工单 Id（IPQC/OQC 时必填）</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>工单号</summary>
    public string? OrderNumber { get; set; }

    /// <summary>产品/物料编码（IQC=物料, IPQC=产品）</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>产品/物料名称</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>批次号（IQC 来料批次 / IPQC 生产批次）</summary>
    public string? BatchNumber { get; set; }

    /// <summary>供应商编码（IQC 时必填）</summary>
    public string? SupplierCode { get; set; }

    /// <summary>供应商名称（IQC 时必填）</summary>
    public string? SupplierName { get; set; }

    /// <summary>检验计划 Id（引用 InspectionPlan）</summary>
    public Ulid InspectionPlanId { get; set; }

    /// <summary>检验计划名称</summary>
    public string InspectionPlanName { get; set; } = string.Empty;

    /// <summary>AQL 抽样方案（如 "AQL=0.65, Level II"）</summary>
    public string? AqlScheme { get; set; }

    /// <summary>抽样数量</summary>
    public int SampleSize { get; set; }

    /// <summary>合格判定数（Ac）</summary>
    public int AcceptNumber { get; set; }

    /// <summary>不合格判定数（Re）</summary>
    public int RejectNumber { get; set; }

    /// <summary>检验员工号</summary>
    public string InspectorId { get; set; } = string.Empty;

    /// <summary>检验结果判定</summary>
    public InspectionVerdict Verdict { get; set; } = InspectionVerdict.Pending;

    /// <summary>检验特性实测值列表</summary>
    public List<MeasuredCharacteristic> Characteristics { get; set; } = [];

    /// <summary>不合格项数量</summary>
    public int DefectCount { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public static QualityRecord CreateIqc(
        Ulid inspectionPlanId,
        string inspectionPlanName,
        string materialCode,
        string materialName,
        string batchNumber,
        string supplierCode,
        string supplierName,
        string inspectorId,
        int sampleSize,
        int acceptNumber,
        int rejectNumber,
        string? aqlScheme = null)
    {
        ValidateRequired(materialCode, nameof(materialCode));
        ValidateRequired(batchNumber, nameof(batchNumber));
        ValidateRequired(inspectorId, nameof(inspectorId));

        return new QualityRecord
        {
            Id = Ulid.NewUlid(),
            Stage = InspectionStage.Iq,
            ProductCode = materialCode.Trim(),
            ProductName = materialName.Trim(),
            BatchNumber = batchNumber.Trim(),
            SupplierCode = supplierCode.Trim(),
            SupplierName = supplierName.Trim(),
            InspectionPlanId = inspectionPlanId,
            InspectionPlanName = inspectionPlanName.Trim(),
            AqlScheme = aqlScheme?.Trim(),
            SampleSize = sampleSize,
            AcceptNumber = acceptNumber,
            RejectNumber = rejectNumber,
            InspectorId = inspectorId.Trim(),
            Verdict = InspectionVerdict.Pending,
            Characteristics = [],
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static QualityRecord CreateIpqc(
        Ulid orderId,
        string orderNumber,
        string productCode,
        string productName,
        Ulid inspectionPlanId,
        string inspectionPlanName,
        string inspectorId,
        List<MeasuredCharacteristic> characteristics,
        int acceptNumber = 0,
        int rejectNumber = 1)
    {
        ValidateRequired(productCode, nameof(productCode));
        ValidateRequired(inspectorId, nameof(inspectorId));

        return new QualityRecord
        {
            Id = Ulid.NewUlid(),
            Stage = InspectionStage.Ipqc,
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            ProductCode = productCode.Trim(),
            ProductName = productName.Trim(),
            InspectionPlanId = inspectionPlanId,
            InspectionPlanName = inspectionPlanName.Trim(),
            InspectorId = inspectorId.Trim(),
            SampleSize = characteristics.Count,
            AcceptNumber = acceptNumber,
            RejectNumber = rejectNumber,
            Characteristics = characteristics,
            Verdict = InspectionVerdict.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>记录实测值</summary>
    public void RecordCharacteristic(string characteristicCode, double actualValue)
    {
        var c = Characteristics.FirstOrDefault(x => x.CharacteristicCode == characteristicCode);
        if (c is null)
            throw new KeyNotFoundException($"检验特性 {characteristicCode} 不在计划中");

        c.Record(actualValue);
    }

    /// <summary>完成检验并自动判定</summary>
    public void Complete()
    {
        if (Verdict != InspectionVerdict.Pending)
            throw new InvalidOperationException($"检验记录状态为 {Verdict}，无法完成");

        DefectCount = Characteristics.Count(c => c.IsFailed);
        Verdict = DefectCount >= RejectNumber
            ? InspectionVerdict.Failed
            : DefectCount > 0
                ? InspectionVerdict.ConditionalPass
                : InspectionVerdict.Passed;

        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>人工指定判定（覆盖自动判定）</summary>
    public void OverrideVerdict(InspectionVerdict verdict, string? remarks = null)
    {
        if (verdict == InspectionVerdict.Pending)
            throw new ArgumentException("不能设为待判定状态", nameof(verdict));

        Verdict = verdict;
        if (remarks is not null)
            Remarks = remarks.Trim();
        CompletedAt ??= DateTimeOffset.UtcNow;
    }

    private static void ValidateRequired(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} 不能为空", paramName);
    }
}

/// <summary>
/// 检验特性实测值（单个测量结果）。
/// </summary>
[MemoryPackable]
public partial class MeasuredCharacteristic
{
    /// <summary>特性编码（如 TOR-M6、DIM-01）</summary>
    public string CharacteristicCode { get; set; } = string.Empty;

    /// <summary>特性名称（如 "M6 扭矩"、"阀体孔径"）</summary>
    public string CharacteristicName { get; set; } = string.Empty;

    /// <summary>标准值</summary>
    public double StandardValue { get; set; }

    /// <summary>规格上限 (USL)</summary>
    public double? UpperSpecLimit { get; set; }

    /// <summary>规格下限 (LSL)</summary>
    public double? LowerSpecLimit { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>实测值</summary>
    public double? ActualValue { get; set; }

    /// <summary>是否不合格</summary>
    public bool IsFailed { get; set; }

    /// <summary>测量设备/工具</summary>
    public string? MeasurementTool { get; set; }

    public static MeasuredCharacteristic Create(
        string code, string name, double std, string unit,
        double? usl = null, double? lsl = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("特性编码不能为空", nameof(code));

        return new MeasuredCharacteristic
        {
            CharacteristicCode = code.Trim(),
            CharacteristicName = name.Trim(),
            StandardValue = std,
            Unit = unit.Trim(),
            UpperSpecLimit = usl,
            LowerSpecLimit = lsl,
        };
    }

    public void Record(double actualValue)
    {
        ActualValue = actualValue;

        if (UpperSpecLimit.HasValue && actualValue > UpperSpecLimit.Value)
        {
            IsFailed = true;
            return;
        }
        if (LowerSpecLimit.HasValue && actualValue < LowerSpecLimit.Value)
        {
            IsFailed = true;
            return;
        }
        IsFailed = false;
    }
}
