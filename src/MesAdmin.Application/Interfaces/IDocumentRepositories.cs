using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// 受控文档主表仓储（S03）。
/// </summary>
public interface IControlledDocumentRepository
{
    Task<ControlledDocument?> GetByIdAsync(Ulid id, CancellationToken ct = default);
    Task<ControlledDocument?> GetByDocNumberAsync(string docNumber, CancellationToken ct = default);
    Task<List<ControlledDocument>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(ControlledDocument doc, CancellationToken ct = default);
    Task UpdateAsync(ControlledDocument doc, CancellationToken ct = default);
}

/// <summary>
/// 受控文档版本仓储（S03）。
/// </summary>
public interface IDocumentVersionRepository
{
    Task<DocumentVersion?> GetVersionByIdAsync(Ulid id, CancellationToken ct = default);
    Task<List<DocumentVersion>> GetByDocumentIdAsync(Ulid documentId, CancellationToken ct = default);
    Task<DocumentVersion?> GetReleasedAsync(Ulid documentId, CancellationToken ct = default);
    Task AddAsync(DocumentVersion version, CancellationToken ct = default);
    Task UpdateAsync(DocumentVersion version, CancellationToken ct = default);
}

/// <summary>
/// 文件存储抽象（S03 · IFileStorage）。
/// Infrastructure 提供 LocalFileStorage 实现，落盘到配置 BasePath。
/// </summary>
public interface IFileStorage
{
    /// <summary>保存文件并返回存储相对路径。</summary>
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    /// <summary>按存储路径加载文件。</summary>
    Task<(Stream Content, string ContentType, string FileName, long FileSize)> LoadAsync(string storagePath, CancellationToken ct = default);
}
