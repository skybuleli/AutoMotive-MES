using FastEndpoints;
using MemoryPack;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.Documents;

/// <summary>受控文档端点组（api/v1/documents，S03 · 文件控制）。</summary>
public class DocumentGroup : Group
{
    public DocumentGroup() => Configure("api/v1/documents", ep => { });
}

// ═══════════════════════════════════════════
//  响应 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial record ControlledDocumentResponse(
    string Id,
    string DocNumber,
    string Title,
    string Type,
    string? StationScope,
    string? CurrentVersionId,
    string? CurrentVersionNo,
    string? CurrentVersionStatus,
    int VersionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

[MemoryPackable]
public partial record DocumentVersionResponse(
    string Id,
    string DocumentId,
    string VersionNo,
    string FileName,
    long FileSize,
    string ContentType,
    string Status,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAt,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? EffectiveAt,
    DateTimeOffset? SupersededAt,
    string? Remarks,
    DateTimeOffset CreatedAt);

[MemoryPackable]
public partial record DocumentDetailResponse(
    ControlledDocumentResponse Document,
    List<DocumentVersionResponse> Versions);

// ═══════════════════════════════════════════
//  请求 DTO
// ═══════════════════════════════════════════

[MemoryPackable]
public partial class CreateDocumentRequest
{
    /// <summary>文档编号（唯一，如 DOC-SOP-ESP-001；大小写不敏感，存大写）</summary>
    public string DocNumber { get; set; } = string.Empty;

    /// <summary>标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>类型：Sop / Wi / Drawing / Form（大小写不敏感）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>版本号（如 v1.0，必须唯一于同文档下）</summary>
    public string VersionNo { get; set; } = string.Empty;

    /// <summary>原文件名（含扩展名）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME 类型（可选，不传按扩展名推断）</summary>
    public string? ContentType { get; set; }

    /// <summary>文件 Base64 内容（JSON 传输，服务端解码后落盘）</summary>
    public string FileBase64 { get; set; } = string.Empty;

    /// <summary>工站范围（可选，null = 通用）</summary>
    public string? StationScope { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }
}

// ═══════════════════════════════════════════
//  Mappers
// ═══════════════════════════════════════════

public static class DocumentMapper
{
    public static ControlledDocumentResponse ToDocumentResponse(ControlledDocument d, DocumentVersion? current, int versionCount) => new(
        d.Id.ToString(),
        d.DocNumber,
        d.Title,
        d.Type.ToString(),
        d.StationScope,
        d.CurrentVersionId?.ToString(),
        current?.VersionNo,
        current?.Status.ToString(),
        versionCount,
        d.CreatedAt,
        d.UpdatedAt);

    public static DocumentVersionResponse ToVersionResponse(DocumentVersion v) => new(
        v.Id.ToString(),
        v.DocumentId.ToString(),
        v.VersionNo,
        v.FileName,
        v.FileSize,
        v.ContentType,
        v.Status.ToString(),
        v.SubmittedBy,
        v.SubmittedAt,
        v.ApprovedBy,
        v.ApprovedAt,
        v.EffectiveAt,
        v.SupersededAt,
        v.Remarks,
        v.CreatedAt);
}
