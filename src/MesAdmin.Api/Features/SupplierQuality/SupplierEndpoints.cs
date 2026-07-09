using FastEndpoints;
using FluentValidation;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;
using MesAdmin.Application.Security;
using MesAdmin.Domain.Models;

namespace MesAdmin.Api.Features.SupplierQuality;

// ═══════════════════════════════════════════
//  GET /api/v1/suppliers — 供应商列表
// ═══════════════════════════════════════════

public class ListSuppliersEndpoint : MesEndpointWithoutRequest<List<SupplierResponse>>
{
    public override void Configure()
    {
        Get("/suppliers");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager, MesRoles.EquipmentEngineer);
        Summary(s => s.Summary = "查询供应商列表，支持按分类和关键供应商过滤");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var category = Query<string?>("category", isRequired: false);
        var criticalOnly = Query<bool?>("critical", isRequired: false);
        var repo = Resolve<ISupplierRepository>();

        List<Supplier> suppliers;
        if (criticalOnly == true)
        {
            suppliers = await repo.GetCriticalAsync(ct);
        }
        else if (category is not null)
        {
            suppliers = await repo.GetByMaterialCategoryAsync(category, ct);
        }
        else
        {
            suppliers = await repo.GetAllAsync(ct);
        }

        Response = suppliers.Select(SupplierMapper.ToResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/suppliers/{id} — 供应商详情
// ═══════════════════════════════════════════

public class GetSupplierEndpoint : MesEndpointWithoutRequest<SupplierResponse>
{
    public override void Configure()
    {
        Get("/suppliers/{id}");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询供应商详情");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISupplierRepository>();
        var supplier = await repo.GetByIdAsync(supplierId, ct);
        if (supplier is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Response = SupplierMapper.ToResponse(supplier);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers — 创建供应商
// ═══════════════════════════════════════════

public class CreateSupplierEndpoint : MesEndpoint<CreateSupplierRequest, SupplierResponse>
{
    public override void Configure()
    {
        Post("/suppliers");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "创建供应商主数据");
    }

    public override async Task HandleAsync(CreateSupplierRequest req, CancellationToken ct)
    {
        var supplier = Supplier.Create(
            Ulid.NewUlid(),
            req.SupplierCode,
            req.SupplierName,
            req.MaterialCategory,
            req.MaterialCodes,
            req.IsCritical,
            req.ContactPerson,
            req.ContactPhone,
            req.ContactEmail);

        if (!string.IsNullOrWhiteSpace(req.ShortName))
            supplier.ShortName = req.ShortName.Trim();
        if (!string.IsNullOrWhiteSpace(req.IsoCertification))
            supplier.IsoCertification = req.IsoCertification.Trim();
        if (!string.IsNullOrWhiteSpace(req.Remarks))
            supplier.Remarks = req.Remarks.Trim();

        var repo = Resolve<ISupplierRepository>();
        await repo.AddAsync(supplier, ct);
        await repo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToResponse(supplier);
        await SendCreatedDualAsync<CreateSupplierEndpoint>(new { id = supplier.Id.ToString() }, ct);
    }
}

public class CreateSupplierValidator : Validator<CreateSupplierRequest>
{
    public CreateSupplierValidator()
    {
        RuleFor(x => x.SupplierCode).NotEmpty().WithMessage("供应商编码不能为空");
        RuleFor(x => x.SupplierName).NotEmpty().WithMessage("供应商名称不能为空");
        RuleFor(x => x.MaterialCategory).NotEmpty().WithMessage("物料类别不能为空");
    }
}

// ═══════════════════════════════════════════
//  PUT /api/v1/suppliers/{id} — 更新供应商
// ═══════════════════════════════════════════

public class UpdateSupplierEndpoint : MesEndpoint<UpdateSupplierRequest, SupplierResponse>
{
    public override void Configure()
    {
        Put("/suppliers/{id}");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "更新供应商信息");
    }

    public override async Task HandleAsync(UpdateSupplierRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISupplierRepository>();
        var supplier = await repo.GetByIdAsync(supplierId, ct);
        if (supplier is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (req.ShortName is not null) supplier.ShortName = req.ShortName.Trim();
        if (req.ContactPerson is not null) supplier.ContactPerson = req.ContactPerson.Trim();
        if (req.ContactPhone is not null) supplier.ContactPhone = req.ContactPhone.Trim();
        if (req.ContactEmail is not null) supplier.ContactEmail = req.ContactEmail.Trim();
        if (req.MaterialCategory is not null) supplier.MaterialCategory = req.MaterialCategory.Trim();
        if (req.MaterialCodes is not null) supplier.MaterialCodes = req.MaterialCodes.Trim();
        if (req.IsCritical.HasValue) supplier.IsCritical = req.IsCritical.Value;
        if (req.IsoCertification is not null) supplier.IsoCertification = req.IsoCertification.Trim();
        if (req.IsoExpiryDate.HasValue) supplier.IsoExpiryDate = req.IsoExpiryDate.Value;
        if (req.Remarks is not null) supplier.Remarks = req.Remarks.Trim();

        supplier.UpdatedAt = DateTimeOffset.UtcNow;
        repo.Update(supplier);
        await repo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToResponse(supplier);
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{id}/score — 创建评分卡
// ═══════════════════════════════════════════

public class CreateScoreCardEndpoint : MesEndpoint<CreateScoreCardRequest, SupplierScoreCardResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{id}/score");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建供应商评分卡（五大 KPI 加权评分）");
    }

    public override async Task HandleAsync(CreateScoreCardRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var supplierRepo = Resolve<ISupplierRepository>();
        var supplier = await supplierRepo.GetByIdAsync(supplierId, ct);
        if (supplier is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var card = SupplierScoreCard.Create(
            Ulid.NewUlid(),
            supplierId,
            supplier.SupplierCode,
            req.Period,
            req.IncomingQualityScore,
            req.IncomingQualityData,
            req.OnTimeDeliveryScore,
            req.OnTimeDeliveryData,
            req.EightDResponseScore,
            req.EightDResponseData,
            req.PpapPassRateScore,
            req.PpapPassRateData,
            req.PriceCompetitivenessScore,
            req.PriceCompetitivenessData,
            req.EvaluatedBy);

        if (!string.IsNullOrWhiteSpace(req.Remarks))
            card.Remarks = req.Remarks.Trim();

        var scoreRepo = Resolve<ISupplierScoreCardRepository>();
        await scoreRepo.AddAsync(card, ct);
        await scoreRepo.SaveChangesAsync(ct);

        // 更新供应商综合评分
        supplier.UpdateScore(card.WeightedTotal);
        supplierRepo.Update(supplier);
        await supplierRepo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToScoreCardResponse(card);
        await SendCreatedDualAsync<CreateScoreCardEndpoint>(new { id = card.Id.ToString() }, ct);
    }
}

public class CreateScoreCardValidator : Validator<CreateScoreCardRequest>
{
    public CreateScoreCardValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("供应商 Id 不能为空");
        RuleFor(x => x.Period).NotEmpty().WithMessage("评分期间不能为空");
        RuleFor(x => x.EvaluatedBy).NotEmpty().WithMessage("评分人不能为空");
        RuleFor(x => x.IncomingQualityScore).InclusiveBetween(0, 100).WithMessage("来料合格率得分需在 0-100 之间");
        RuleFor(x => x.OnTimeDeliveryScore).InclusiveBetween(0, 100).WithMessage("交货准时率得分需在 0-100 之间");
        RuleFor(x => x.EightDResponseScore).InclusiveBetween(0, 100).WithMessage("8D 响应速度得分需在 0-100 之间");
        RuleFor(x => x.PpapPassRateScore).InclusiveBetween(0, 100).WithMessage("PPAP 通过率得分需在 0-100 之间");
        RuleFor(x => x.PriceCompetitivenessScore).InclusiveBetween(0, 100).WithMessage("价格竞争力得分需在 0-100 之间");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/suppliers/{id}/scores — 评分卡历史
// ═══════════════════════════════════════════

public class ListScoreCardsEndpoint : MesEndpointWithoutRequest<List<SupplierScoreCardResponse>>
{
    public override void Configure()
    {
        Get("/suppliers/{id}/scores");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询供应商评分历史");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISupplierScoreCardRepository>();
        var cards = await repo.GetBySupplierAsync(supplierId, ct);
        Response = cards.Select(SupplierMapper.ToScoreCardResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/suppliers/{id}/ppap — PPAP 文档列表
// ═══════════════════════════════════════════

public class ListPpapDocumentsEndpoint : MesEndpointWithoutRequest<List<PpapDocumentResponse>>
{
    public override void Configure()
    {
        Get("/suppliers/{id}/ppap");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询供应商 PPAP 文档列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPpapDocumentRepository>();
        var docs = await repo.GetBySupplierAsync(supplierId, ct);
        Response = docs.Select(SupplierMapper.ToPpapResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{id}/ppap — 创建 PPAP 文档
// ═══════════════════════════════════════════

public class CreatePpapDocumentEndpoint : MesEndpoint<CreatePpapDocumentRequest, PpapDocumentResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{id}/ppap");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "创建 PPAP 文档记录");
    }

    public override async Task HandleAsync(CreatePpapDocumentRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        var supplierRepo = Resolve<ISupplierRepository>();
        var supplier = await supplierRepo.GetByIdAsync(supplierId, ct);
        if (supplier is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var doc = PpapDocument.Create(
            Ulid.NewUlid(),
            supplierId,
            supplier.SupplierCode,
            req.MaterialCode,
            req.MaterialName,
            req.CreatedBy,
            req.PpapLevel,
            req.ExpiryDate);

        if (!string.IsNullOrWhiteSpace(req.Remarks))
            doc.Remarks = req.Remarks.Trim();

        var ppapRepo = Resolve<IPpapDocumentRepository>();
        await ppapRepo.AddAsync(doc, ct);
        await ppapRepo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToPpapResponse(doc);
        await SendCreatedDualAsync<CreatePpapDocumentEndpoint>(new { id = doc.Id.ToString() }, ct);
    }
}

public class CreatePpapDocumentValidator : Validator<CreatePpapDocumentRequest>
{
    public CreatePpapDocumentValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("供应商 Id 不能为空");
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.MaterialName).NotEmpty().WithMessage("物料名称不能为空");
        RuleFor(x => x.CreatedBy).NotEmpty().WithMessage("创建人不能为空");
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5).WithMessage("PPAP 等级需在 1-5 之间");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{supplierId}/ppap/{id}/submit — 提交 PPAP
// ═══════════════════════════════════════════

public class SubmitPpapEndpoint : MesEndpointWithoutRequest<PpapDocumentResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{supplierId}/ppap/{id}/submit");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "提交 PPAP 文档审批");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var docId))
        {
            AddError("id", "无效的 PPAP 文档 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPpapDocumentRepository>();
        var doc = await repo.GetByIdAsync(docId, ct);
        if (doc is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            doc.Submit();
            repo.Update(doc);
            await repo.SaveChangesAsync(ct);
            Response = SupplierMapper.ToPpapResponse(doc);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{supplierId}/ppap/{id}/approve — 批准 PPAP
// ═══════════════════════════════════════════

public class ApprovePpapEndpoint : MesEndpoint<ApprovePpapRequest, PpapDocumentResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{supplierId}/ppap/{id}/approve");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "批准 PPAP 文档");
    }

    public override async Task HandleAsync(ApprovePpapRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var docId))
        {
            AddError("id", "无效的 PPAP 文档 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPpapDocumentRepository>();
        var doc = await repo.GetByIdAsync(docId, ct);
        if (doc is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            doc.Approve(req.ApprovedBy);
            repo.Update(doc);
            await repo.SaveChangesAsync(ct);
            Response = SupplierMapper.ToPpapResponse(doc);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class ApprovePpapValidator : Validator<ApprovePpapRequest>
{
    public ApprovePpapValidator()
    {
        RuleFor(x => x.ApprovedBy).NotEmpty().WithMessage("批准人不能为空");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{supplierId}/ppap/{id}/reject — 拒绝 PPAP
// ═══════════════════════════════════════════

public class RejectPpapEndpoint : MesEndpoint<RejectPpapRequest, PpapDocumentResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{supplierId}/ppap/{id}/reject");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "拒绝 PPAP 文档");
    }

    public override async Task HandleAsync(RejectPpapRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var docId))
        {
            AddError("id", "无效的 PPAP 文档 Id");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<IPpapDocumentRepository>();
        var doc = await repo.GetByIdAsync(docId, ct);
        if (doc is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            doc.Reject(req.Reason);
            repo.Update(doc);
            await repo.SaveChangesAsync(ct);
            Response = SupplierMapper.ToPpapResponse(doc);
            await SendDualAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AddError(ex.Message);
            ThrowIfAnyErrors();
        }
    }
}

public class RejectPpapValidator : Validator<RejectPpapRequest>
{
    public RejectPpapValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("拒绝原因不能为空");
    }
}

// ═══════════════════════════════════════════
//  GET /api/v1/suppliers/critical-settings — 关键供应商管控设置
// ═══════════════════════════════════════════

public class ListCriticalSettingsEndpoint : MesEndpointWithoutRequest<List<CriticalSupplierSettingResponse>>
{
    public override void Configure()
    {
        Get("/suppliers/critical-settings");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "查询关键供应商管控设置列表");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = Resolve<ICriticalSupplierSettingRepository>();
        var settings = await repo.GetAllAsync(ct);
        Response = settings.Select(SupplierMapper.ToCriticalResponse).ToList();
        await SendDualAsync(ct);
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/critical-settings — 创建管控设置
// ═══════════════════════════════════════════

public class CreateCriticalSettingEndpoint : MesEndpoint<CreateCriticalSettingRequest, CriticalSupplierSettingResponse>
{
    public override void Configure()
    {
        Post("/suppliers/critical-settings");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer, MesRoles.ProductionManager);
        Summary(s => s.Summary = "创建关键供应商管控设置");
    }

    public override async Task HandleAsync(CreateCriticalSettingRequest req, CancellationToken ct)
    {
        var setting = CriticalSupplierSetting.Create(
            Ulid.NewUlid(),
            req.MaterialCode,
            req.MaterialName,
            req.ControlLevel);

        setting.RequiresFullInspection = req.RequiresFullInspection;
        setting.RequiresOnSiteAudit = req.RequiresOnSiteAudit;
        setting.AuditIntervalMonths = req.AuditIntervalMonths;
        setting.RequiresSpcDataSubmission = req.RequiresSpcDataSubmission;
        setting.RequiresComplianceReport = req.RequiresComplianceReport;

        var repo = Resolve<ICriticalSupplierSettingRepository>();
        await repo.AddAsync(setting, ct);
        await repo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToCriticalResponse(setting);
        await SendCreatedDualAsync<CreateCriticalSettingEndpoint>(new { id = setting.Id.ToString() }, ct);
    }
}

public class CreateCriticalSettingValidator : Validator<CreateCriticalSettingRequest>
{
    public CreateCriticalSettingValidator()
    {
        RuleFor(x => x.MaterialCode).NotEmpty().WithMessage("物料编码不能为空");
        RuleFor(x => x.MaterialName).NotEmpty().WithMessage("物料名称不能为空");
        RuleFor(x => x.ControlLevel).InclusiveBetween(1, 3).WithMessage("管控等级需在 1-3 之间");
        RuleFor(x => x.AuditIntervalMonths).GreaterThan(0).WithMessage("审核间隔必须大于 0");
    }
}

// ═══════════════════════════════════════════
//  POST /api/v1/suppliers/{id}/update-tier — 手动更新供应商等级
// ═══════════════════════════════════════════

public class UpdateSupplierTierEndpoint : MesEndpoint<UpdateTierRequest, SupplierResponse>
{
    public override void Configure()
    {
        Post("/suppliers/{id}/update-tier");
        Group<SupplierGroup>();
        Roles(MesRoles.QualityEngineer);
        Summary(s => s.Summary = "手动更新供应商等级");
    }

    public override async Task HandleAsync(UpdateTierRequest req, CancellationToken ct)
    {
        var idStr = Route<string>("id")!;
        if (!Ulid.TryParse(idStr, out var supplierId))
        {
            AddError("id", "无效的供应商 Id");
            ThrowIfAnyErrors();
        }

        if (!Enum.TryParse<SupplierTier>(req.Tier, true, out var tier))
        {
            AddError("tier", "无效的供应商等级，支持 Preferred/Qualified/Conditional/Disqualified");
            ThrowIfAnyErrors();
        }

        var repo = Resolve<ISupplierRepository>();
        var supplier = await repo.GetByIdAsync(supplierId, ct);
        if (supplier is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        supplier.UpdateTier(tier);
        repo.Update(supplier);
        await repo.SaveChangesAsync(ct);

        Response = SupplierMapper.ToResponse(supplier);
        await SendDualAsync(ct);
    }
}

public class UpdateTierRequest
{
    public string Tier { get; set; } = string.Empty;
}

public class UpdateTierValidator : Validator<UpdateTierRequest>
{
    public UpdateTierValidator()
    {
        RuleFor(x => x.Tier).NotEmpty().WithMessage("供应商等级不能为空");
    }
}
