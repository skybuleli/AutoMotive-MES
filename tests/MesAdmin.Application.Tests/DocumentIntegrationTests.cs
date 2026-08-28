using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 受控文档中心集成测试（S03）。
/// 覆盖 文件落盘 → 详情加载、提交/审批状态机、分级 Supersede 事务。
/// </summary>
[Collection("DatabaseIntegration")]
public class DocumentIntegrationTests
{
    private readonly DatabaseFixture _fixture;

    public DocumentIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateDocument_Then_GetDetail_ShouldPersistFileAndMetadata()
    {
        using var scope = _fixture.Services.CreateScope();
        var docRepo = scope.ServiceProvider.GetRequiredService<IControlledDocumentRepository>();
        var versionRepo = scope.ServiceProvider.GetRequiredService<IDocumentVersionRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var docNumber = $"DOC-S03-{Ulid.NewUlid().ToString()[..6].ToUpperInvariant()}";
        var doc = ControlledDocument.Create(Ulid.NewUlid(), docNumber, DocumentType.Sop, "E2E 测试 SOP");
        await docRepo.AddAsync(doc);

        var bytes = System.Text.Encoding.UTF8.GetBytes("SOP v1 content - IATF");
        await using var ms = new MemoryStream(bytes);
        var path = await storage.SaveAsync(ms, "sop-v1.pdf", "application/pdf");

        var version = DocumentVersion.CreateDraft(
            Ulid.NewUlid(), doc.Id, "v1.0", path, "sop-v1.pdf", bytes.Length, "application/pdf");
        await versionRepo.AddAsync(version);

        var detail = await docRepo.GetByIdAsync(doc.Id);
        var versions = await versionRepo.GetByDocumentIdAsync(doc.Id);
        Assert.NotNull(detail);
        Assert.Single(versions);
        Assert.Equal("v1.0", versions[0].VersionNo);
        Assert.Equal(DocumentStatus.Draft, versions[0].Status);

        // 下载校验
        var (stream, ct, fileName, size) = await storage.LoadAsync(versions[0].FileStoragePath);
        await using var _ = stream;
        Assert.Equal("application/pdf", ct);
        Assert.Equal(bytes.Length, size);
        Assert.Equal("sop-v1.pdf", fileName);
        using var r = new StreamReader(stream);
        var content = await r.ReadToEndAsync();
        Assert.Equal("SOP v1 content - IATF", content);
    }

    [Fact]
    public async Task FullLifecycle_Submit_Approve_Supersede_ShouldMaintainSingleReleased()
    {
        using var scope = _fixture.Services.CreateScope();
        var docRepo = scope.ServiceProvider.GetRequiredService<IControlledDocumentRepository>();
        var versionRepo = scope.ServiceProvider.GetRequiredService<IDocumentVersionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<MesAdmin.Infrastructure.Data.MesDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var docNumber = $"DOC-S03-LIFECYCLE-{Ulid.NewUlid().ToString()[..6].ToUpperInvariant()}";
        var doc = ControlledDocument.Create(Ulid.NewUlid(), docNumber, DocumentType.Sop, "Lifecycle SOP");
        await docRepo.AddAsync(doc);

        // v1.0 Draft -> Submit -> Approve (Released)
        var v1 = DocumentVersion.CreateDraft(
            Ulid.NewUlid(), doc.Id, "v1.0",
            await SaveTempFileAsync(storage, "v1.pdf", "v1"), "v1.pdf", 2, "application/pdf");
        await versionRepo.AddAsync(v1);
        v1.SubmitForApproval("QE-001");
        await versionRepo.UpdateAsync(v1);
        v1.Approve("MGR-001");
        await versionRepo.UpdateAsync(v1);

        // 事务性发布：设置 CurrentVersion + (若需) supersede 旧版 — 此处 v1 首发无旧版
        doc.SetCurrentVersion(v1.Id);
        await docRepo.UpdateAsync(doc);

        var released = await versionRepo.GetReleasedAsync(doc.Id);
        Assert.NotNull(released);
        Assert.Equal(v1.Id, released!.Id);
        Assert.Equal(DocumentStatus.Released, released.Status);

        // v1.1 Draft -> Submit -> Approve => v1 superseded, v1.1 released
        var v2 = DocumentVersion.CreateDraft(
            Ulid.NewUlid(), doc.Id, "v1.1",
            await SaveTempFileAsync(storage, "v2.pdf", "v2-updated"), "v2.pdf", 10, "application/pdf");
        await versionRepo.AddAsync(v2);
        v2.SubmitForApproval("QE-002");
        await versionRepo.UpdateAsync(v2);

        // 模拟 Approve 端点的事务：旧版 supersede + 新版 release + doc 指向新版
        // 复用 db transaction 逻辑（简化：直接手工 supersede + update）
        var v1Tracked = await db.DocumentVersions.FirstAsync(x => x.Id == v1.Id);
        v1Tracked.Supersede();
        db.DocumentVersions.Update(v1Tracked);

        v2.Approve("MGR-002");
        db.DocumentVersions.Update(v2);
        doc.SetCurrentVersion(v2.Id);
        db.ControlledDocuments.Update(doc);
        await db.SaveChangesAsync();

        var all = await versionRepo.GetByDocumentIdAsync(doc.Id);
        Assert.Equal(2, all.Count);
        var v1After = all.First(v => v.Id == v1.Id);
        var v2After = all.First(v => v.Id == v2.Id);
        Assert.Equal(DocumentStatus.Superseded, v1After.Status);
        Assert.Equal(DocumentStatus.Released, v2After.Status);

        var docAfter = await docRepo.GetByIdAsync(doc.Id);
        Assert.Equal(v2.Id, docAfter!.CurrentVersionId);
    }

    [Fact]
    public async Task Approve_ShouldThrow_WhenNotPending()
    {
        using var scope = _fixture.Services.CreateScope();
        var versionRepo = scope.ServiceProvider.GetRequiredService<IDocumentVersionRepository>();
        var docRepo = scope.ServiceProvider.GetRequiredService<IControlledDocumentRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var doc = ControlledDocument.Create(Ulid.NewUlid(), $"DOC-ERR-{Ulid.NewUlid().ToString()[..5]}", DocumentType.Form, "Err");
        await docRepo.AddAsync(doc);
        var ver = DocumentVersion.CreateDraft(
            Ulid.NewUlid(), doc.Id, "v1.0",
            await SaveTempFileAsync(storage, "e.pdf", "x"), "e.pdf", 1, "application/pdf");
        await versionRepo.AddAsync(ver);

        // 未提交直接批准应抛
        Assert.Throws<InvalidOperationException>(() => ver.Approve("MGR"));
    }

    private static async Task<string> SaveTempFileAsync(IFileStorage storage, string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        await using var ms = new MemoryStream(bytes);
        return await storage.SaveAsync(ms, fileName, "application/pdf");
    }
}
