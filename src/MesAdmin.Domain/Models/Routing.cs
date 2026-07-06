using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// ECO 状态：Draft=草稿 / PendingApproval=待审批 / Approved=已批准 / Released=已发布 / Superseded=已取代
/// </summary>
public enum EcoStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Released = 3,
    Superseded = 4
}

/// <summary>
/// 工艺路线（T3.1 M07 工艺管理）。
/// 定义 ESP-9.0/9.1 的 31 工序 × 7 站工艺路线，含标准工时、工装夹具、参数模板，
/// 以及 ECO 版本控制（审批流 → 发布 → 旧版本归档）。
/// </summary>
[MemoryPackable]
public partial class Routing
{
    public Ulid Id { get; set; }

    /// <summary>产品编码 ESP-9.0 / ESP-9.1</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>工艺路线名称（如 "ESP-9.0 标准工艺路线 V1.0"）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号（如 "1.0", "2.0"）</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>ECO 变更单号（如 "ECO-20260701-001"）</summary>
    public string? EcoNumber { get; set; }

    /// <summary>ECO 状态</summary>
    public EcoStatus EcoStatus { get; set; } = EcoStatus.Draft;

    /// <summary>工序数量（固定 31）</summary>
    public int OperationCount { get; set; } = 31;

    /// <summary>是否当前有效版本（仅一个版本可设为 Active）</summary>
    public bool IsActive { get; set; }

    /// <summary>变更描述</summary>
    public string? ChangeDescription { get; set; }

    /// <summary>创建人（员工号）</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>审批人（员工号）</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>审批时间</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>工序定义（31 条，作为 JSONB 嵌入）</summary>
    public List<RoutingOperation> Operations { get; set; } = [];

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>生效时间</summary>
    public DateTimeOffset? EffectiveDate { get; set; }

    /// <summary>过期时间（被取代后记录）</summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    public static Routing Create(
        Ulid id,
        string productCode,
        string name,
        string version,
        string createdBy,
        List<RoutingOperation> operations,
        string? ecoNumber = null,
        string? changeDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        var now = DateTimeOffset.UtcNow;
        return new Routing
        {
            Id = id,
            ProductCode = productCode.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Version = version.Trim(),
            EcoNumber = ecoNumber?.Trim(),
            EcoStatus = EcoStatus.Draft,
            OperationCount = operations.Count,
            IsActive = false,
            ChangeDescription = changeDescription?.Trim(),
            CreatedBy = createdBy.Trim(),
            Operations = operations,
            CreatedAt = now,
            UpdatedAt = now,
            EffectiveDate = now,
        };
    }

    /// <summary>提交 ECO 审批</summary>
    public void SubmitForApproval()
    {
        if (EcoStatus != EcoStatus.Draft)
            throw new InvalidOperationException($"ECO 状态为 {EcoStatus}，不能提交审批");

        EcoStatus = EcoStatus.PendingApproval;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>审批通过</summary>
    public void Approve(string approvedBy)
    {
        if (EcoStatus != EcoStatus.PendingApproval)
            throw new InvalidOperationException($"ECO 状态为 {EcoStatus}，不能审批");

        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = DateTimeOffset.UtcNow;
        EcoStatus = EcoStatus.Approved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>发布（设为当前有效版本）</summary>
    public void Release()
    {
        if (EcoStatus != EcoStatus.Approved)
            throw new InvalidOperationException($"ECO 状态为 {EcoStatus}，不能发布");

        EcoStatus = EcoStatus.Released;
        IsActive = true;
        EffectiveDate = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>取代旧版本</summary>
    public void Supersede()
    {
        EcoStatus = EcoStatus.Superseded;
        IsActive = false;
        ExpirationDate = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>获取指定工站的工序列表</summary>
    public List<RoutingOperation> GetOperationsByStation(int station)
        => Operations.Where(o => o.Station == station).OrderBy(o => o.Sequence).ToList();

    /// <summary>获取指定工序</summary>
    public RoutingOperation? GetOperation(int sequence)
        => Operations.FirstOrDefault(o => o.Sequence == sequence);
}

/// <summary>
/// 工艺路线工序定义（嵌入 Routing 作为 JSONB）。
/// </summary>
[MemoryPackable]
public partial class RoutingOperation
{
    /// <summary>工序序号（1-31）</summary>
    public int Sequence { get; set; }

    /// <summary>工站编号（1-7）</summary>
    public int Station { get; set; }

    /// <summary>工序编码（如 TQ-01, HY-03）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>工序名称</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>标准工时（秒）</summary>
    public double? StandardTimeSeconds { get; set; }

    /// <summary>工装夹具编号</summary>
    public string? FixtureCode { get; set; }

    /// <summary>工装夹具名称</summary>
    public string? FixtureName { get; set; }

    /// <summary>参数模板列表</summary>
    public List<ParameterTemplate> ParameterTemplates { get; set; } = [];
}

/// <summary>
/// 工艺参数模板（嵌入 RoutingOperation 作为 JSONB）。
/// </summary>
[MemoryPackable]
public partial class ParameterTemplate
{
    /// <summary>参数编码（如 TOR-M6, HYD-01）</summary>
    public string ParameterCode { get; set; } = string.Empty;

    /// <summary>参数名称（如 "M6 螺栓扭矩", "建压时间"）</summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>标准值</summary>
    public double StandardValue { get; set; }

    /// <summary>上限（公差上限）</summary>
    public double? UpperSpecLimit { get; set; }

    /// <summary>下限（公差下限）</summary>
    public double? LowerSpecLimit { get; set; }

    /// <summary>计量单位</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是否启用 SPC 监控</summary>
    public bool EnableSpc { get; set; }

    /// <summary>SPC 子组大小（默认 5）</summary>
    public int SpcSubgroupSize { get; set; } = 5;
}
