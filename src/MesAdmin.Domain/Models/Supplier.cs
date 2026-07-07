using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 供应商等级。
/// </summary>
public enum SupplierTier
{
    /// <summary>优选 — 持续优秀表现</summary>
    Preferred = 0,
    /// <summary>合格 — 满足要求</summary>
    Qualified = 1,
    /// <summary>待改进 — 需纠正措施</summary>
    Conditional = 2,
    /// <summary>不合格 — 暂停供货</summary>
    Disqualified = 3,
}

/// <summary>
/// 供应商主数据（T3.6 M08 SQE）。
/// </summary>
[MemoryPackable]
public partial class Supplier
{
    public Ulid Id { get; set; }

    /// <summary>供应商编码</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>供应商名称</summary>
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>供应商简称</summary>
    public string? ShortName { get; set; }

    /// <summary>统一社会信用代码</summary>
    public string? CreditCode { get; set; }

    /// <summary>联系人</summary>
    public string? ContactPerson { get; set; }

    /// <summary>联系电话</summary>
    public string? ContactPhone { get; set; }

    /// <summary>联系邮箱</summary>
    public string? ContactEmail { get; set; }

    /// <summary>地址</summary>
    public string? Address { get; set; }

    /// <summary>供应物料类别（如 ECU芯片、电磁阀、PCB板材）</summary>
    public string MaterialCategory { get; set; } = string.Empty;

    /// <summary>供应物料编码（逗号分隔）</summary>
    public string MaterialCodes { get; set; } = string.Empty;

    /// <summary>当前等级</summary>
    public SupplierTier Tier { get; set; } = SupplierTier.Qualified;

    /// <summary>是否关键供应商（电磁阀/压力传感器/PCB 板材）</summary>
    public bool IsCritical { get; set; }

    /// <summary>最新综合评分（0-100）</summary>
    public double LatestScore { get; set; }

    /// <summary>评分日期</summary>
    public DateTimeOffset? LatestScoreAt { get; set; }

    /// <summary>ISO 认证状态</summary>
    public string? IsoCertification { get; set; }

    /// <summary>ISO 证书到期日期</summary>
    public DateTimeOffset? IsoExpiryDate { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static Supplier Create(
        Ulid id,
        string supplierCode,
        string supplierName,
        string materialCategory,
        string materialCodes,
        bool isCritical = false,
        string? contactPerson = null,
        string? contactPhone = null,
        string? contactEmail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierName);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialCategory);

        var now = DateTimeOffset.UtcNow;
        return new Supplier
        {
            Id = id,
            SupplierCode = supplierCode.Trim(),
            SupplierName = supplierName.Trim(),
            MaterialCategory = materialCategory.Trim(),
            MaterialCodes = materialCodes?.Trim() ?? string.Empty,
            IsCritical = isCritical,
            ContactPerson = contactPerson?.Trim(),
            ContactPhone = contactPhone?.Trim(),
            ContactEmail = contactEmail?.Trim(),
            Tier = SupplierTier.Qualified,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>更新供应商等级。</summary>
    public void UpdateTier(SupplierTier newTier)
    {
        Tier = newTier;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>更新综合评分。</summary>
    public void UpdateScore(double score)
    {
        LatestScore = Math.Clamp(score, 0, 100);
        LatestScoreAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;

        // 根据评分自动调整等级
        Tier = score switch
        {
            >= 90 => SupplierTier.Preferred,
            >= 70 => SupplierTier.Qualified,
            >= 50 => SupplierTier.Conditional,
            _ => SupplierTier.Disqualified,
        };
    }
}

/// <summary>
/// 供应商评分卡（T3.6）。
/// 按周期（月度/季度/年度）对 5 大 KPI 进行评分。
/// 权重：来料合格率 30% + 交货准时率 25% + 8D 响应速度 20% + PPAP 通过率 15% + 价格竞争力 10%
/// </summary>
[MemoryPackable]
public partial class SupplierScoreCard
{
    public Ulid Id { get; set; }

    /// <summary>关联供应商 Id</summary>
    public Ulid SupplierId { get; set; }

    /// <summary>供应商编码</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>评分期间（如 2026-Q3）</summary>
    public string Period { get; set; } = string.Empty;

    // ── 5 大 KPI（每项 0-100）──

    /// <summary>来料合格率得分（权重 30%）</summary>
    public double IncomingQualityScore { get; set; }

    /// <summary>来料合格率原始数据（合格批数/总批数）</summary>
    public string IncomingQualityData { get; set; } = string.Empty;

    /// <summary>交货准时率得分（权重 25%）</summary>
    public double OnTimeDeliveryScore { get; set; }

    /// <summary>交货准时率原始数据</summary>
    public string OnTimeDeliveryData { get; set; } = string.Empty;

    /// <summary>8D 响应速度得分（权重 20%）</summary>
    public double EightDResponseScore { get; set; }

    /// <summary>8D 响应速度原始数据</summary>
    public string EightDResponseData { get; set; } = string.Empty;

    /// <summary>PPAP 通过率得分（权重 15%）</summary>
    public double PpapPassRateScore { get; set; }

    /// <summary>PPAP 通过率原始数据</summary>
    public string PpapPassRateData { get; set; } = string.Empty;

    /// <summary>价格竞争力得分（权重 10%）</summary>
    public double PriceCompetitivenessScore { get; set; }

    /// <summary>价格竞争力原始数据</summary>
    public string PriceCompetitivenessData { get; set; } = string.Empty;

    /// <summary>综合得分（加权平均）</summary>
    public double WeightedTotal { get; set; }

    /// <summary>评分人</summary>
    public string EvaluatedBy { get; set; } = string.Empty;

    /// <summary>评分备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 计算加权综合得分。
    /// 权重：质量 30% + 交付 25% + 8D 20% + PPAP 15% + 价格 10%
    /// </summary>
    public static double CalculateWeightedTotal(
        double quality, double delivery, double eightD, double ppap, double price)
    {
        return Math.Round(
            quality * 0.30 + delivery * 0.25 + eightD * 0.20 + ppap * 0.15 + price * 0.10, 2);
    }

    public static SupplierScoreCard Create(
        Ulid id,
        Ulid supplierId,
        string supplierCode,
        string period,
        double incomingQualityScore,
        string incomingQualityData,
        double onTimeDeliveryScore,
        string onTimeDeliveryData,
        double eightDResponseScore,
        string eightDResponseData,
        double ppapPassRateScore,
        string ppapPassRateData,
        double priceCompetitivenessScore,
        string priceCompetitivenessData,
        string evaluatedBy)
    {
        var weighted = CalculateWeightedTotal(
            incomingQualityScore, onTimeDeliveryScore, eightDResponseScore,
            ppapPassRateScore, priceCompetitivenessScore);

        return new SupplierScoreCard
        {
            Id = id,
            SupplierId = supplierId,
            SupplierCode = supplierCode.Trim(),
            Period = period.Trim(),
            IncomingQualityScore = incomingQualityScore,
            IncomingQualityData = incomingQualityData?.Trim() ?? string.Empty,
            OnTimeDeliveryScore = onTimeDeliveryScore,
            OnTimeDeliveryData = onTimeDeliveryData?.Trim() ?? string.Empty,
            EightDResponseScore = eightDResponseScore,
            EightDResponseData = eightDResponseData?.Trim() ?? string.Empty,
            PpapPassRateScore = ppapPassRateScore,
            PpapPassRateData = ppapPassRateData?.Trim() ?? string.Empty,
            PriceCompetitivenessScore = priceCompetitivenessScore,
            PriceCompetitivenessData = priceCompetitivenessData?.Trim() ?? string.Empty,
            WeightedTotal = weighted,
            EvaluatedBy = evaluatedBy.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>
/// PPAP 提交状态。
/// </summary>
public enum PpapStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>已提交待审批</summary>
    Submitted = 1,
    /// <summary>已批准</summary>
    Approved = 2,
    /// <summary>已拒绝</summary>
    Rejected = 3,
    /// <summary>已过期</summary>
    Expired = 4,
}

/// <summary>
/// PPAP 文档（T3.7 M08 SQE）。
/// 覆盖 18 项 PPAP 文档清单的电子归档、到期提醒、逾期升级。
/// </summary>
[MemoryPackable]
public partial class PpapDocument
{
    public Ulid Id { get; set; }

    /// <summary>关联供应商 Id</summary>
    public Ulid SupplierId { get; set; }

    /// <summary>供应商编码</summary>
    public string SupplierCode { get; set; } = string.Empty;

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>PPAP 等级（1-5）</summary>
    public int PpapLevel { get; set; } = 3;

    /// <summary>当前状态</summary>
    public PpapStatus Status { get; set; } = PpapStatus.Draft;

    /// <summary>提交日期</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>批准日期</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>到期日期（证书/文档到期提醒）</summary>
    public DateTimeOffset? ExpiryDate { get; set; }

    /// <summary>是否已过期</summary>
    public bool IsExpired => ExpiryDate.HasValue && ExpiryDate.Value < DateTimeOffset.UtcNow;

    /// <summary>提交版本</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>批准人</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>拒绝原因</summary>
    public string? RejectionReason { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建人</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static PpapDocument Create(
        Ulid id,
        Ulid supplierId,
        string supplierCode,
        string materialCode,
        string materialName,
        string createdBy,
        int ppapLevel = 3,
        DateTimeOffset? expiryDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);

        var now = DateTimeOffset.UtcNow;
        return new PpapDocument
        {
            Id = id,
            SupplierId = supplierId,
            SupplierCode = supplierCode.Trim(),
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName?.Trim() ?? string.Empty,
            PpapLevel = Math.Clamp(ppapLevel, 1, 5),
            Status = PpapStatus.Draft,
            Version = "1.0",
            CreatedBy = createdBy.Trim(),
            ExpiryDate = expiryDate,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>提交审批。</summary>
    public void Submit()
    {
        if (Status != PpapStatus.Draft)
            throw new InvalidOperationException($"PPAP 状态为 {Status}，不能提交");
        Status = PpapStatus.Submitted;
        SubmittedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>批准。</summary>
    public void Approve(string approvedBy)
    {
        if (Status != PpapStatus.Submitted)
            throw new InvalidOperationException($"PPAP 状态为 {Status}，不能批准");
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ApprovedBy = approvedBy.Trim();
        Status = PpapStatus.Approved;
        ApprovedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>拒绝。</summary>
    public void Reject(string reason)
    {
        if (Status != PpapStatus.Submitted)
            throw new InvalidOperationException($"PPAP 状态为 {Status}，不能拒绝");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RejectionReason = reason.Trim();
        Status = PpapStatus.Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>标记过期。</summary>
    public void MarkExpired()
    {
        Status = PpapStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// PPAP 文档项（18 项清单，T3.7）。
/// 定义 PPAP 18 项文档标准清单。
/// </summary>
public static class PpapDocumentItems
{
    /// <summary>PPAP 18 项标准文档清单</summary>
    public static readonly IReadOnlyList<string> StandardItems = new[]
    {
        "1. 设计记录（Design Records）",
        "2. 工程变更文件（Engineering Change Documents）",
        "3. 过程流程图（Process Flow Diagram）",
        "4. 过程 FMEA（Process FMEA）",
        "5. 控制计划（Control Plan）",
        "6. 测量系统分析 MSA（Measurement System Analysis）",
        "7. 尺寸结果（Dimensional Results）",
        "8. 材料/性能试验结果（Material/Performance Test Results）",
        "9. 初始过程能力研究（Initial Process Capability Study）",
        "10. 实验室资质文件（Laboratory Qualifications）",
        "11. 外观批准报告（Appearance Approval Report）",
        "12. 散装材料要求检查表（Bulk Material Requirements Checklist）",
        "13. 样品产品（Sample Product）",
        "14. 标准样品（Master Sample）",
        "15. 检查辅助工具（Checking Aids）",
        "16. 客户特殊要求（Customer Specific Requirements）",
        "17. 零件提交保证书（PSW - Part Submission Warrant）",
        "18. 批准印章记录（Approval Stamp Record）",
    };
}

/// <summary>
/// 关键供应商管控设置（T3.8）。
/// 电磁阀/压力传感器/PCB 板材三类最高等级管控。
/// </summary>
[MemoryPackable]
public partial class CriticalSupplierSetting
{
    public Ulid Id { get; set; }

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = string.Empty;

    /// <summary>管控等级（1-3，3=最高）</summary>
    public int ControlLevel { get; set; } = 3;

    /// <summary>是否需要 100% 来料检验</summary>
    public bool RequiresFullInspection { get; set; } = true;

    /// <summary>是否需要供应商现场审核</summary>
    public bool RequiresOnSiteAudit { get; set; } = true;

    /// <summary>审核频率（月）</summary>
    public int AuditIntervalMonths { get; set; } = 6;

    /// <summary>是否需要 SPC 数据提交</summary>
    public bool RequiresSpcDataSubmission { get; set; } = true;

    /// <summary>是否需要 RoHS/REACH 报告</summary>
    public bool RequiresComplianceReport { get; set; } = true;

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public static CriticalSupplierSetting Create(
        Ulid id,
        string materialCode,
        string materialName,
        int controlLevel = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);

        var now = DateTimeOffset.UtcNow;
        return new CriticalSupplierSetting
        {
            Id = id,
            MaterialCode = materialCode.Trim(),
            MaterialName = materialName.Trim(),
            ControlLevel = Math.Clamp(controlLevel, 1, 3),
            RequiresFullInspection = true,
            RequiresOnSiteAudit = true,
            AuditIntervalMonths = 6,
            RequiresSpcDataSubmission = true,
            RequiresComplianceReport = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
