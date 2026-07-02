using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 首件检验状态。
/// </summary>
public enum InspectionStatus
{
    Pending = 0,
    InProgress = 1,
    Passed = 2,
    Failed = 3
}

/// <summary>
/// 首件检验记录。
/// 每班次/换型后强制执行，控制计划逐项检验，全部合格方可批量生产。
/// </summary>
[MemoryPackable]
public partial class FirstArticleInspection
{
    public Ulid Id { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid OrderId { get; set; }

    /// <summary>工单号</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>产品编码</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>检验类型（班次首件 / 换型首件 / 设备维修后）</summary>
    public string InspectionType { get; set; } = string.Empty;

    /// <summary>检验状态</summary>
    public InspectionStatus Status { get; set; } = InspectionStatus.Pending;

    /// <summary>操作员工号</summary>
    public string OperatorId { get; set; } = string.Empty;

    /// <summary>质量工程师工号（审核人）</summary>
    public string? InspectorId { get; set; }

    /// <summary>检验项列表</summary>
    public List<InspectionItem> Items { get; set; } = [];

    /// <summary>总体结论</summary>
    public string? Conclusion { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>完成时间</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public static FirstArticleInspection Create(
        Ulid orderId,
        string orderNumber,
        string productCode,
        string inspectionType,
        string operatorId,
        List<InspectionItem> items)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("工单号不能为空", nameof(orderNumber));
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("产品编码不能为空", nameof(productCode));
        if (string.IsNullOrWhiteSpace(inspectionType))
            throw new ArgumentException("检验类型不能为空", nameof(inspectionType));
        if (string.IsNullOrWhiteSpace(operatorId))
            throw new ArgumentException("操作员工号不能为空", nameof(operatorId));
        if (items is null || items.Count == 0)
            throw new ArgumentException("检验项不能为空", nameof(items));

        return new FirstArticleInspection
        {
            Id = Ulid.NewUlid(),
            OrderId = orderId,
            OrderNumber = orderNumber.Trim(),
            ProductCode = productCode.Trim().ToUpperInvariant(),
            InspectionType = inspectionType.Trim(),
            OperatorId = operatorId.Trim(),
            Items = items,
            Status = InspectionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Start()
    {
        if (Status != InspectionStatus.Pending)
            throw new InvalidOperationException($"首件检验状态为 {Status}，无法开始");
        Status = InspectionStatus.InProgress;
    }

    public void Complete(string inspectorId, bool allPassed, string? conclusion = null)
    {
        if (Status != InspectionStatus.InProgress)
            throw new InvalidOperationException($"首件检验状态为 {Status}，无法完成");

        InspectorId = inspectorId;
        Status = allPassed ? InspectionStatus.Passed : InspectionStatus.Failed;
        Conclusion = conclusion ?? (allPassed ? "全部检验项合格" : "存在不合格项，请处理");
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public bool IsAllItemsPassed => Items.Count > 0 && Items.All(i => i.IsPass);
}

/// <summary>
/// 检验项（控制计划中的单个检验特性）。
/// </summary>
[MemoryPackable]
public partial class InspectionItem
{
    /// <summary>检验特性编码（如 DIM-01、TOR-02）</summary>
    public string CharacteristicCode { get; set; } = string.Empty;

    /// <summary>检验特性名称（如 "阀体安装孔直径"、"M6 扭矩"）</summary>
    public string CharacteristicName { get; set; } = string.Empty;

    /// <summary>标准值</summary>
    public double StandardValue { get; set; }

    /// <summary>上限</summary>
    public double? UpperLimit { get; set; }

    /// <summary>下限</summary>
    public double? LowerLimit { get; set; }

    /// <summary>单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>实测值</summary>
    public double? ActualValue { get; set; }

    /// <summary>是否合格</summary>
    public bool IsPass { get; set; }

    /// <summary>检验设备/工具</summary>
    public string? InspectionTool { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    public static InspectionItem Create(
        string characteristicCode,
        string characteristicName,
        double standardValue,
        string unit,
        double? upperLimit = null,
        double? lowerLimit = null)
    {
        if (string.IsNullOrWhiteSpace(characteristicCode))
            throw new ArgumentException("检验特性编码不能为空", nameof(characteristicCode));
        if (string.IsNullOrWhiteSpace(characteristicName))
            throw new ArgumentException("检验特性名称不能为空", nameof(characteristicName));

        return new InspectionItem
        {
            CharacteristicCode = characteristicCode.Trim(),
            CharacteristicName = characteristicName.Trim(),
            StandardValue = standardValue,
            Unit = unit.Trim(),
            UpperLimit = upperLimit,
            LowerLimit = lowerLimit,
        };
    }

    public void RecordValue(double actualValue)
    {
        ActualValue = actualValue;

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
