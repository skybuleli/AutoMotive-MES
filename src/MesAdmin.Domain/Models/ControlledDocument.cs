using MemoryPack;

namespace MesAdmin.Domain.Models;

/// <summary>受控文档类型（S03 · 文件管控中心）。</summary>
public enum DocumentType
{
    /// <summary>SOP 标准作业指导书</summary>
    Sop = 0,
    /// <summary>WI 作业指导书（细分）</summary>
    Wi = 1,
    /// <summary>图纸 / 工艺图</summary>
    Drawing = 2,
    /// <summary>表单 / 记录表</summary>
    Form = 3,
}

/// <summary>受控文档版本状态（S03 · 四态机：Draft→Pending→Released→Superseded）。</summary>
public enum DocumentStatus
{
    /// <summary>草稿（可编辑/上传，可提交）</summary>
    Draft = 0,
    /// <summary>待审批</summary>
    PendingApproval = 1,
    /// <summary>已发布（当前生效，或历史生效但已被新版取代前的状态）</summary>
    Released = 2,
    /// <summary>已被取代（旧版失效）</summary>
    Superseded = 3
}

/// <summary>
/// 受控文档主表（S03 · IATF 16949 文件控制）。
/// 按 DocNumber 唯一管理，当前生效版本通过 CurrentVersionId 指向。
/// 版本内容在 DocumentVersion 表中分行保存。
/// </summary>
[MemoryPackable]
public partial class ControlledDocument
{
    public Ulid Id { get; set; }

    /// <summary>文档编号（唯一，如 DOC-SOP-ESP-001）</summary>
    public string DocNumber { get; set; } = string.Empty;

    /// <summary>标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>类型</summary>
    public DocumentType Type { get; set; }

    /// <summary>关联工站/工序范围（null = 全站通用，例 "ST3" 或 "OP-10"）</summary>
    public string? StationScope { get; set; }

    /// <summary>当前生效版本 Id（null = 尚无发布版）</summary>
    public Ulid? CurrentVersionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static ControlledDocument Create(
        Ulid id,
        string docNumber,
        DocumentType type,
        string title,
        string? stationScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var now = DateTimeOffset.UtcNow;
        return new ControlledDocument
        {
            Id = id,
            DocNumber = docNumber.Trim().ToUpperInvariant(),
            Type = type,
            Title = title.Trim(),
            StationScope = stationScope?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetCurrentVersion(Ulid versionId)
    {
        CurrentVersionId = versionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearCurrentVersion()
    {
        CurrentVersionId = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// 受控文档版本（S03 · 每个文档编号下按 VersionNo 递增，如 v1.0, v1.1, v2.0）。
/// 承载文件路径、状态机与审批轨迹；状态流转：Draft → PendingApproval → Released → Superseded。
/// </summary>
[MemoryPackable]
public partial class DocumentVersion
{
    public Ulid Id { get; set; }

    /// <summary>所属文档 Id</summary>
    public Ulid DocumentId { get; set; }

    /// <summary>版本号（如 "v1.0"）</summary>
    public string VersionNo { get; set; } = string.Empty;

    /// <summary>文件在存储中的相对路径（由 IFileStorage 返回）</summary>
    public string FileStoragePath { get; set; } = string.Empty;

    /// <summary>原文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>文件字节长度</summary>
    public long FileSize { get; set; }

    /// <summary>MIME 类型</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>状态</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    /// <summary>提交人（员工号）</summary>
    public string? SubmittedBy { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>审批人（员工号）</summary>
    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>生效时间（发布时设为 UtcNow）</summary>
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>被取代时间</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static DocumentVersion CreateDraft(
        Ulid id,
        Ulid documentId,
        string versionNo,
        string fileStoragePath,
        string fileName,
        long fileSize,
        string contentType,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileStoragePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var now = DateTimeOffset.UtcNow;
        return new DocumentVersion
        {
            Id = id,
            DocumentId = documentId,
            VersionNo = versionNo.Trim(),
            FileStoragePath = fileStoragePath.Trim(),
            FileName = fileName.Trim(),
            FileSize = fileSize,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            Status = DocumentStatus.Draft,
            Remarks = remarks?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>提交审批：Draft → PendingApproval</summary>
    public void SubmitForApproval(string submittedBy)
    {
        if (Status != DocumentStatus.Draft)
            throw new InvalidOperationException($"文档版本状态为 {Status}，仅草稿可提交审批");
        ArgumentException.ThrowIfNullOrWhiteSpace(submittedBy);
        SubmittedBy = submittedBy.Trim();
        SubmittedAt = DateTimeOffset.UtcNow;
        Status = DocumentStatus.PendingApproval;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>审批发布：PendingApproval → Released</summary>
    public void Approve(string approvedBy)
    {
        if (Status != DocumentStatus.PendingApproval)
            throw new InvalidOperationException($"文档版本状态为 {Status}，仅待审批可批准发布");
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = DateTimeOffset.UtcNow;
        EffectiveAt = DateTimeOffset.UtcNow;
        Status = DocumentStatus.Released;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>被新版取代：Released → Superseded</summary>
    public void Supersede()
    {
        if (Status != DocumentStatus.Released)
            throw new InvalidOperationException($"文档版本状态为 {Status}，仅已发布版可被取代");
        Status = DocumentStatus.Superseded;
        SupersededAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsReleased => Status == DocumentStatus.Released;
    public bool IsSuperseded => Status == DocumentStatus.Superseded;
}
