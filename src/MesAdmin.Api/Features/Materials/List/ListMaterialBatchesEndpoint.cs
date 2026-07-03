using FastEndpoints;
using Microsoft.AspNetCore.Http;
using MemoryPack;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Features.Materials;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Features.Materials.List;

/// <summary>物料批次分页查询端点</summary>
public class ListMaterialBatchesEndpoint : MesEndpointWithoutRequest<List<MaterialBatchResponse>>
{
    public override void Configure()
    {
        Get("/batches");
        Group<MaterialGroup>();
        Roles(MesRoles.WarehouseClerk, MesRoles.ShiftLeader, MesRoles.ProductionManager, MesRoles.QualityEngineer);
        Summary(s => s.Summary = "物料批次分页查询");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var materialCode = Query<string?>("materialCode", isRequired: false);
        var page = Query<int?>("page", isRequired: false) ?? 1;
        var size = Query<int?>("size", isRequired: false) ?? 20;

        var (items, total) = await new ListMaterialBatchesQuery(materialCode, page, size).ExecuteAsync(ct);

        Response = items.Select(MaterialMapper.ToResponse).ToList();
        HttpContext.Response.Headers["X-Total-Count"] = total.ToString();
        await SendDualAsync(ct);
    }
}
