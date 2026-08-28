using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Infrastructure.Data.Repositories;

/// <summary>受控文档仓储（S03）。单类实现双接口，避免重复 DbContext。</summary>
public sealed class DocumentRepository(MesDbContext db)
    : IControlledDocumentRepository, IDocumentVersionRepository
{
    // ── ControlledDocument ──

    public Task<ControlledDocument?> GetByIdAsync(Ulid id, CancellationToken ct = default)
        => db.ControlledDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<ControlledDocument?> GetByDocNumberAsync(string docNumber, CancellationToken ct = default)
        => db.ControlledDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocNumber == docNumber.Trim().ToUpperInvariant(), ct);

    public Task<List<ControlledDocument>> GetAllAsync(CancellationToken ct = default)
        => db.ControlledDocuments.AsNoTracking().OrderByDescending(d => d.CreatedAt).ToListAsync(ct);

    async Task IControlledDocumentRepository.AddAsync(ControlledDocument doc, CancellationToken ct)
    {
        await db.ControlledDocuments.AddAsync(doc, ct);
        await db.SaveChangesAsync(ct);
    }

    async Task IControlledDocumentRepository.UpdateAsync(ControlledDocument doc, CancellationToken ct)
    {
        db.ControlledDocuments.Update(doc);
        await db.SaveChangesAsync(ct);
    }

    // ── DocumentVersion ──

    public Task<DocumentVersion?> GetVersionByIdAsync(Ulid id, CancellationToken ct = default)
        => db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<List<DocumentVersion>> GetByDocumentIdAsync(Ulid documentId, CancellationToken ct = default)
        => db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    public Task<DocumentVersion?> GetReleasedAsync(Ulid documentId, CancellationToken ct = default)
        => db.DocumentVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Status == DocumentStatus.Released, ct);

    async Task IDocumentVersionRepository.AddAsync(DocumentVersion version, CancellationToken ct)
    {
        await db.DocumentVersions.AddAsync(version, ct);
        await db.SaveChangesAsync(ct);
    }

    async Task IDocumentVersionRepository.UpdateAsync(DocumentVersion version, CancellationToken ct)
    {
        db.DocumentVersions.Update(version);
        await db.SaveChangesAsync(ct);
    }
}
