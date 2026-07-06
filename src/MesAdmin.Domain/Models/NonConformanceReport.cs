using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>
/// 不合格品严重等级。
/// </summary>
public enum NcrSeverity
{
    /// <summary>轻微 — 不影响功能，让步接收</summary>
    Minor = 0,
    /// <summary>一般 — 影响功能，需评审</summary>
    Major = 1,
    /// <summary>严重 — 安全/法规相关，必须报废</summary>
    Critical = 2,
}

/// <summary>
/// NCR 处置方式。
/// </summary>
public enum NcrDisposition
{
    /// <summary>待处置</summary>
    Pending = 0,
    /// <summary>让步接收（偏差许可）</summary>
    Concession = 1,
    /// <summary>返工</summary>
    Rework = 2,
    /// <summary>返修</summary>
    Repair = 3,
    /// <summary>报废</summary>
    Scrap = 4,
    /// <summary>退货给供应商</summary>
    ReturnToSupplier = 5,
}

/// <summary>
/// NCR 状态。
/// </summary>
public enum NcrStatus
{
    Open = 0,
    UnderReview = 1,
    Dispositioned = 2,
    Closed = 3,
}

/// <summary>
/// 不合格品报告（Non-Conformance Report，T2.7）。
/// 覆盖来料不合格（IQC）、过程不合格（IPQC）、出货不合格（OQC）、客户投诉。
/// 支持 MRB（Material Review Board）评审流程和 8D 关联。
/// </summary>
[MemoryPackable]
public partial class NonConformanceReport
{
    public Ulid Id { get; set; }

    /// <summary>NCR 编号（如 NCR-20260705-001）</summary>
    public string NcrNumber { get; set; } = string.Empty;

    /// <summary>关联检验记录 Id（来自 QualityRecord）</summary>
    public Ulid? QualityRecordId { get; set; }

    /// <summary>关联工单 Id</summary>
    public Ulid? OrderId { get; set; }

    /// <summary>关联工单号</summary>
    public string? OrderNumber { get; set; }

    /// <summary>产品/物料编码</summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>产品/物料名称</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>批次号</summary>
    public string? BatchNumber { get; set; }

    /// <summary>发现阶段（IQC/IPQC/OQC/Customer）</summary>
    public InspectionStage DiscoveredAt { get; set; }

    /// <summary>不合格描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>不合格数量</summary>
    public int DefectQuantity { get; set; }

    /// <summary>严重等级</summary>
    public NcrSeverity Severity { get; set; } = NcrSeverity.Minor;

    /// <summary>当前状态</summary>
    public NcrStatus Status { get; set; } = NcrStatus.Open;

    /// <summary>处置方式</summary>
    public NcrDisposition Disposition { get; set; } = NcrDisposition.Pending;

    /// <summary>责任部门</summary>
    public string? ResponsibleDept { get; set; }

    /// <summary>发现人（员工号）</summary>
    public string DiscoveredBy { get; set; } = string.Empty;

    /// <summary>评审人（MRB）</summary>
    public string? ReviewerId { get; set; }

    /// <summary>评审意见</summary>
    public string? ReviewComments { get; set; }

    /// <summary>处置截止日期</summary>
    public DateTimeOffset? DispositionDeadline { get; set; }

    /// <summary>关联 8D 报告 Id</summary>
    public Ulid? EightDReportId { get; set; }

    /// <summary>关闭备注</summary>
    public string? CloseRemarks { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>处置完成时间</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// 创建 NCR（从不合格检验记录自动生成）。
    /// </summary>
    public static NonConformanceReport CreateFromQualityRecord(
        QualityRecord record,
        string description,
        int defectQuantity,
        NcrSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (defectQuantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(defectQuantity), "不合格数量必须大于 0");

        return new NonConformanceReport
        {
            Id = Ulid.NewUlid(),
            NcrNumber = GenerateNcrNumber(),
            QualityRecordId = record.Id,
            OrderId = record.OrderId,
            OrderNumber = record.OrderNumber,
            ProductCode = record.ProductCode,
            ProductName = record.ProductName,
            BatchNumber = record.BatchNumber,
            DiscoveredAt = record.Stage,
            Description = description.Trim(),
            DefectQuantity = defectQuantity,
            Severity = severity,
            Status = NcrStatus.Open,
            DiscoveredBy = record.InspectorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// 提交 MRB 评审。
    /// </summary>
    public void SubmitForReview(string reviewerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerId);
        if (Status != NcrStatus.Open)
            throw new InvalidOperationException($"NCR 状态为 {Status}，不能提交评审");
        Status = NcrStatus.UnderReview;
        ReviewerId = reviewerId.Trim();
    }

    /// <summary>
    /// MRB 做出处置决定。
    /// </summary>
    public void SetDisposition(NcrDisposition disposition, string comments)
    {
        if (Status != NcrStatus.UnderReview)
            throw new InvalidOperationException($"NCR 状态为 {Status}，不能做出处置");
        ArgumentException.ThrowIfNullOrWhiteSpace(comments);

        Disposition = disposition;
        ReviewComments = comments.Trim();
        Status = NcrStatus.Dispositioned;
    }

    /// <summary>
    /// 关闭 NCR。
    /// </summary>
    public void Close(string remarks)
    {
        if (Status == NcrStatus.Closed)
            throw new InvalidOperationException("NCR 已经关闭");
        ArgumentException.ThrowIfNullOrWhiteSpace(remarks);

        CloseRemarks = remarks.Trim();
        Status = NcrStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 关联 8D 报告。
    /// </summary>
    public void LinkEightDReport(Ulid eightDReportId)
    {
        EightDReportId = eightDReportId;
    }

    private static int _ncrSequence;
    private static string GenerateNcrNumber()
    {
        var seq = Interlocked.Increment(ref _ncrSequence);
        return $"NCR-{DateTimeOffset.UtcNow:yyyyMMdd}-{seq:D4}";
    }
}
