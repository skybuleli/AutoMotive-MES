using System.Text;
using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MesAdmin.Api.Features.Documents;

// ═══════════════════════════════════════════
//  POST /api/v1/documents — 创建文档/版本（Draft）
// ═══════════════════════════════════════════

public class CreateDocumentEndpoint : MesEndpoint<CreateDocumentRequest, DocumentDetailResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "新建受控文档或新增版本（Draft，文件以 Base64 上传）");
    }

    public override async Task HandleAsync(CreateDocumentRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<DocumentType>(req.Type, true, out var docType))
        {
            AddError("Type", "无效的文档类型，支持 Sop / Wi / Drawing / Form");
            ThrowIfAnyErrors();
        }

        if (string.IsNullOrWhiteSpace(req.FileBase64))
        {
            AddError("FileBase64", "文件内容不能为空");
            ThrowIfAnyErrors();
        }

        byte[] fileBytes;
        try { fileBytes = Convert.FromBase64String(req.FileBase64); }
        catch (FormatException)
        {
            AddError("FileBase64", "文件 Base64 非法");
            ThrowIfAnyErrors();
            return;
        }

        const long maxSize = 20 * 1024 * 1024;
        if (fileBytes.Length > maxSize)
        {
            AddError("FileBase64", $"文件超过限制（{maxSize / 1024 / 1024} MB）");
            ThrowIfAnyErrors();
        }

        var docRepo = Resolve<IControlledDocumentRepository>();
        var versionRepo = Resolve<IDocumentVersionRepository>();
        var storage = Resolve<IFileStorage>();

        var doc = await docRepo.GetByDocNumberAsync(req.DocNumber, ct);
        var isNewDocument = doc is null;

        if (isNewDocument)
        {
            doc = ControlledDocument.Create(Ulid.NewUlid(), req.DocNumber, docType, req.Title, req.StationScope);
            await docRepo.AddAsync(doc, ct);
        }
        else
        {
            // 已存在文档：校验 VersionNo 在该文档下唯一
            var existingVersions = await versionRepo.GetByDocumentIdAsync(doc!.Id, ct);
            if (existingVersions.Any(v => string.Equals(v.VersionNo, req.VersionNo.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                AddError("VersionNo", $"版本 {req.VersionNo} 在文档 {doc.DocNumber} 下已存在");
                ThrowIfAnyErrors();
            }
        }

        // 落盘
        var contentType = string.IsNullOrWhiteSpace(req.ContentType) ? InferContentType(req.FileName) : req.ContentType!;
        await using var ms = new MemoryStream(fileBytes);
        var storagePath = await storage.SaveAsync(ms, req.FileName.Trim(), contentType, ct);

        var version = DocumentVersion.CreateDraft(
            Ulid.NewUlid(), doc!.Id, req.VersionNo.Trim(), storagePath,
            req.FileName.Trim(), fileBytes.Length, contentType, req.Remarks);

        await versionRepo.AddAsync(version, ct);

        var allVersions = await versionRepo.GetByDocumentIdAsync(doc.Id, ct);
        DocumentVersion? current = null;
        if (doc.CurrentVersionId.HasValue)
            current = allVersions.FirstOrDefault(v => v.Id == doc.CurrentVersionId.Value);

        Response = new DocumentDetailResponse(
            DocumentMapper.ToDocumentResponse(doc, current, allVersions.Count),
            allVersions.Select(DocumentMapper.ToVersionResponse).ToList());
        await SendCreatedDualAsync<CreateDocumentEndpoint>(new { id = doc.Id.ToString() }, ct);
    }

    private static string InferContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }
}

public class CreateDocumentValidator : Validator<CreateDocumentRequest>
{
    public CreateDocumentValidator()
    {
        RuleFor(x => x.DocNumber).NotEmpty().WithMessage("文档编号不能为空").MaximumLength(32);
        RuleFor(x => x.Title).NotEmpty().WithMessage("标题不能为空").MaximumLength(128);
        RuleFor(x => x.VersionNo).NotEmpty().WithMessage("版本号不能为空").MaximumLength(16);
        RuleFor(x => x.FileName).NotEmpty().WithMessage("文件名不能为空");
        RuleFor(x => x.FileBase64).NotEmpty().WithMessage("文件内容不能为空");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/documents — 列表
// ═══════════════════════════════════════════

public class ListDocumentsEndpoint : MesEndpointWithoutRequest<List<ControlledDocumentResponse>>
{
    public override void Configure()
    {
        Get("/");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer,
              MesRoles.Inspector, MesRoles.Technician, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询受控文档列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var docRepo = Resolve<IControlledDocumentRepository>();
        var versionRepo = Resolve<IDocumentVersionRepository>();

        var docs = await docRepo.GetAllAsync(ct);
        var list = new List<ControlledDocumentResponse>(docs.Count);
        foreach (var d in docs)
        {
            var versions = await versionRepo.GetByDocumentIdAsync(d.Id, ct);
            DocumentVersion? current = null;
            if (d.CurrentVersionId.HasValue)
                current = versions.FirstOrDefault(v => v.Id == d.CurrentVersionId.Value);
            list.Add(DocumentMapper.ToDocumentResponse(d, current, versions.Count));
        }

        Response = list;
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/documents/{id} — 详情（含版本列表）
// ═══════════════════════════════════════════

public class GetDocumentEndpoint : MesEndpointWithoutRequest<DocumentDetailResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer,
              MesRoles.Inspector, MesRoles.Technician, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "查询受控文档详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!Ulid.TryParse(Route<string>("id")!, out var id))
        {
            AddError("id", "无效的文档 Id");
            ThrowIfAnyErrors();
        }

        var docRepo = Resolve<IControlledDocumentRepository>();
        var versionRepo = Resolve<IDocumentVersionRepository>();

        var doc = await docRepo.GetByIdAsync(id, ct);
        if (doc is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var versions = await versionRepo.GetByDocumentIdAsync(doc.Id, ct);
        DocumentVersion? current = null;
        if (doc.CurrentVersionId.HasValue)
            current = versions.FirstOrDefault(v => v.Id == doc.CurrentVersionId.Value);

        Response = new DocumentDetailResponse(
            DocumentMapper.ToDocumentResponse(doc, current, versions.Count),
            versions.Select(DocumentMapper.ToVersionResponse).ToList());
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/documents/{id}/versions/{versionId}/submit — 提交审批
// ═══════════════════════════════════════════

public class SubmitVersionEndpoint : MesEndpointWithoutRequest<DocumentVersionResponse>
{
    public override void Configure()
    {
        Post("/{id}/versions/{versionId}/submit");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "提交版本审批（Draft → PendingApproval）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Ulid docId = Ulid.Empty, versionId = Ulid.Empty;
        if (!Ulid.TryParse(Route<string>("id")!, out docId) ||
            !Ulid.TryParse(Route<string>("versionId")!, out versionId))
        {
            AddError("id", "无效的 Id");
            ThrowIfAnyErrors();
        }

        var versionRepo = Resolve<IDocumentVersionRepository>();

        var version = await versionRepo.GetVersionByIdAsync(versionId, ct);
        if (version is null || version.DocumentId != docId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var username = HttpContext.User.Identity?.Name
            ?? HttpContext.User.FindFirst("user_id")?.Value
            ?? "anonymous";

        try
        {
            version.SubmitForApproval(username);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }

        await versionRepo.UpdateAsync(version, ct);

        Response = DocumentMapper.ToVersionResponse(version);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/documents/{id}/versions/{versionId}/approve — 批准发布（Pending → Released，同事务 supersede 旧发布版）
// ═══════════════════════════════════════════

public class ApproveVersionEndpoint : MesEndpointWithoutRequest<DocumentVersionResponse>
{
    public override void Configure()
    {
        Post("/{id}/versions/{versionId}/approve");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "批准并发布版本（Pending → Released，旧发布版自动 Superseded）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Ulid docId = Ulid.Empty, versionId = Ulid.Empty;
        if (!Ulid.TryParse(Route<string>("id")!, out docId) ||
            !Ulid.TryParse(Route<string>("versionId")!, out versionId))
        {
            AddError("id", "无效的 Id");
            ThrowIfAnyErrors();
        }

        var db = Resolve<MesDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doc = await db.ControlledDocuments.FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var version = await db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.DocumentId == docId, ct);
        if (version is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var username = HttpContext.User.Identity?.Name
            ?? HttpContext.User.FindFirst("unique_name")?.Value
            ?? HttpContext.User.FindFirst("user_id")?.Value
            ?? "anonymous";

        try
        {
            version.Approve(username);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
            return;
        }

        // 旧发布版 supersede（若存在且非本次发布版）
        if (doc.CurrentVersionId.HasValue && doc.CurrentVersionId.Value != version.Id)
        {
            var oldTracked = await db.DocumentVersions.FirstOrDefaultAsync(
                v => v.Id == doc.CurrentVersionId.Value, ct);
            if (oldTracked is not null && oldTracked.Status == DocumentStatus.Released)
            {
                oldTracked.Supersede();
                db.DocumentVersions.Update(oldTracked);
            }
        }

        db.DocumentVersions.Update(version);
        doc.SetCurrentVersion(version.Id);
        db.ControlledDocuments.Update(doc);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        Response = DocumentMapper.ToVersionResponse(version);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/documents/{id}/versions/{versionId}/file — 下载文件
// ═══════════════════════════════════════════

public class DownloadDocumentFileEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/{id}/versions/{versionId}/file");
        Group<DocumentGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer,
              MesRoles.Inspector, MesRoles.Technician, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "下载文档版本文件");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Ulid docId = Ulid.Empty, versionId = Ulid.Empty;
        if (!Ulid.TryParse(Route<string>("id")!, out docId) ||
            !Ulid.TryParse(Route<string>("versionId")!, out versionId))
        {
            AddError("id", "无效的 Id");
            ThrowIfAnyErrors();
        }

        var versionRepo = Resolve<IDocumentVersionRepository>();
        var storage = Resolve<IFileStorage>();

        var version = await versionRepo.GetVersionByIdAsync(versionId, ct);
        if (version is null || version.DocumentId != docId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            var (stream, contentType, fileName, _) = await storage.LoadAsync(version.FileStoragePath, ct);
            HttpContext.Response.ContentType = string.IsNullOrWhiteSpace(version.ContentType) ? contentType : version.ContentType;
            HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(fileName)}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
            HttpContext.Response.ContentLength = version.FileSize;
            await stream.CopyToAsync(HttpContext.Response.Body, ct);
            await stream.DisposeAsync();
        }
        catch (FileNotFoundException)
        {
            await Send.NotFoundAsync(ct);
        }
    }
}
