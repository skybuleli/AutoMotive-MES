using FastEndpoints;

namespace MesAdmin.Api.Features.SupplierQuality;

/// <summary>供应商 SQE 端点组（api/v1/suppliers）</summary>
public class SupplierGroup : Group
{
    public SupplierGroup() => Configure("api/v1/suppliers", ep => { });
}
