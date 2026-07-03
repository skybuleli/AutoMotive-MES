using FastEndpoints;
using FluentValidation;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Materials;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Materials.Receive;

/// <summary>来料扫码入库端点（T1.12）</summary>
public class ReceiveMaterialEndpoint : MesEndpoint<ReceiveMaterialRequest, MaterialBatchResponse>
{
    public override void Configure()
    {
        Post("/batches");
        Group<MaterialGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.ShiftLeader);
        Summary(s => s.Summary = "来料扫码入库（GS1-128 解析 + 供应商校验）");
    }

    public override async Task HandleAsync(ReceiveMaterialRequest req, CancellationToken ct)
    {
        var batch = await new ReceiveMaterialCommand(
            req.Barcode,
            req.SupplierCode,
            req.SupplierName,
            req.MaterialName,
            req.IsCritical).ExecuteAsync(ct);

        Response = MaterialMapper.ToResponse(batch);
        await SendCreatedDualAsync<ReceiveMaterialEndpoint>(new { id = batch.Id.ToString() }, ct);
    }
}

[MemoryPackable]
public partial class ReceiveMaterialRequest
{
    /// <summary>GS1-128 条码（含物料编码+批次+数量+生产日期）</summary>
    public string Barcode { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
}

public class ReceiveMaterialValidator : Validator<ReceiveMaterialRequest>
{
    public ReceiveMaterialValidator()
    {
        RuleFor(x => x.Barcode).NotEmpty().WithMessage("条码不能为空");
        RuleFor(x => x.SupplierCode).NotEmpty().WithMessage("供应商编码不能为空");
        RuleFor(x => x.MaterialName).NotEmpty().WithMessage("物料名称不能为空");
    }
}
