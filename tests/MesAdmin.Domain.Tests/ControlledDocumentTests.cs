using MesAdmin.Domain.Models;

namespace MesAdmin.Domain.Tests;

/// <summary>
/// 受控文档版本状态机测试（S03 · 文件控制）。
/// 覆盖四态流转与非法转移抛异常，与 Routing ECO 模式对齐。
/// </summary>
public class ControlledDocumentTests
{
    private static DocumentVersion CreateDraft(string versionNo = "v1.0")
        => DocumentVersion.CreateDraft(
            Ulid.NewUlid(), Ulid.NewUlid(), versionNo,
            "202608/xxx_test.pdf", "SOP.pdf", 1234, "application/pdf");

    [Fact]
    public void CreateDraft_ShouldInitializeAsDraft()
    {
        var doc = ControlledDocument.Create(Ulid.NewUlid(), "DOC-SOP-001", DocumentType.Sop, "ESP SOP");
        var ver = CreateDraft();

        Assert.Equal(DocumentStatus.Draft, ver.Status);
        Assert.Equal("v1.0", ver.VersionNo);
        Assert.Null(ver.SubmittedBy);
        Assert.Null(ver.ApprovedBy);
        Assert.Null(doc.CurrentVersionId);
    }

    [Fact]
    public void Create_ShouldTrimAndUpperDocNumber()
    {
        var doc = ControlledDocument.Create(Ulid.NewUlid(), " doc-sop-001 ", DocumentType.Drawing, " 标题 ");
        Assert.Equal("DOC-SOP-001", doc.DocNumber);
        Assert.Equal("标题", doc.Title);
    }

    [Fact]
    public void SubmitForApproval_ShouldTransitionDraftToPending()
    {
        var ver = CreateDraft();
        ver.SubmitForApproval("QE-001");

        Assert.Equal(DocumentStatus.PendingApproval, ver.Status);
        Assert.Equal("QE-001", ver.SubmittedBy);
        Assert.NotNull(ver.SubmittedAt);
    }

    [Fact]
    public void SubmitForApproval_ShouldThrow_WhenNotDraft()
    {
        var ver = CreateDraft();
        ver.SubmitForApproval("QE-001");
        Assert.Throws<InvalidOperationException>(() => ver.SubmitForApproval("QE-002"));
    }

    [Fact]
    public void Approve_ShouldTransitionPendingToReleased()
    {
        var ver = CreateDraft();
        ver.SubmitForApproval("QE-001");
        ver.Approve("MGR-001");

        Assert.Equal(DocumentStatus.Released, ver.Status);
        Assert.Equal("MGR-001", ver.ApprovedBy);
        Assert.NotNull(ver.ApprovedAt);
        Assert.NotNull(ver.EffectiveAt);
    }

    [Fact]
    public void Approve_ShouldThrow_WhenNotPending()
    {
        var ver = CreateDraft();
        Assert.Throws<InvalidOperationException>(() => ver.Approve("MGR-001"));
        ver.SubmitForApproval("QE-001");
        ver.Approve("MGR-001");
        Assert.Throws<InvalidOperationException>(() => ver.Approve("MGR-002"));
    }

    [Fact]
    public void Supersede_ShouldTransitionReleasedToSuperseded()
    {
        var ver = CreateDraft();
        ver.SubmitForApproval("QE-001");
        ver.Approve("MGR-001");
        ver.Supersede();

        Assert.Equal(DocumentStatus.Superseded, ver.Status);
        Assert.NotNull(ver.SupersededAt);
        Assert.False(ver.IsReleased);
        Assert.True(ver.IsSuperseded);
    }

    [Fact]
    public void Supersede_ShouldThrow_WhenNotReleased()
    {
        var ver = CreateDraft();
        Assert.Throws<InvalidOperationException>(() => ver.Supersede());
        ver.SubmitForApproval("QE-001");
        Assert.Throws<InvalidOperationException>(() => ver.Supersede());
    }

    [Fact]
    public void FullLifecycle_Draft_Pending_Released_Superseded()
    {
        var doc = ControlledDocument.Create(Ulid.NewUlid(), "DOC-SOP-002", DocumentType.Sop, "Test");
        var v1 = DocumentVersion.CreateDraft(Ulid.NewUlid(), doc.Id, "v1.0", "p1/x.pdf", "x.pdf", 100, "application/pdf");
        var v2 = DocumentVersion.CreateDraft(Ulid.NewUlid(), doc.Id, "v1.1", "p2/x.pdf", "x2.pdf", 100, "application/pdf");

        v1.SubmitForApproval("QE-1");
        v1.Approve("MGR-1");
        doc.SetCurrentVersion(v1.Id);
        Assert.Equal(DocumentStatus.Released, v1.Status);
        Assert.Equal(v1.Id, doc.CurrentVersionId);

        // 发布 v1.1：旧版 supersede，新版 released
        v2.SubmitForApproval("QE-1");
        v2.Approve("MGR-1");
        v1.Supersede();
        doc.SetCurrentVersion(v2.Id);

        Assert.Equal(DocumentStatus.Superseded, v1.Status);
        Assert.Equal(DocumentStatus.Released, v2.Status);
        Assert.Equal(v2.Id, doc.CurrentVersionId);
    }

    [Fact]
    public void CreateDraft_ShouldThrow_WhenVersionNoEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            DocumentVersion.CreateDraft(Ulid.NewUlid(), Ulid.NewUlid(), " ", "path", "f.pdf", 100, "application/pdf"));
    }
}
