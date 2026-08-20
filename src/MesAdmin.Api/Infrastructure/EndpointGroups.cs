using FastEndpoints;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Infrastructure;

/// <summary>生产工单端点组（api/v1/orders）</summary>
public class ProductionOrderGroup : Group
{
    public ProductionOrderGroup() => Configure("api/v1/orders", ep => { });
}

/// <summary>首件检验端点组（api/v1/orders/{orderId}/inspections）</summary>
public class InspectionGroup : Group
{
    public InspectionGroup() => Configure("api/v1/orders/{orderId}/inspections", ep => { });
}

/// <summary>SAP Webhook 端点组（api/webhooks/sap，匿名访问——仅供 SAP 回调）</summary>
public class SapWebhookGroup : Group
{
    public SapWebhookGroup() => Configure("api/webhooks/sap", ep => ep.AllowAnonymous());
}

/// <summary>SAP 拒单写回端点组（api/webhooks/sap，需角色认证——手动重试操作）</summary>
public class SapWritebackGroup : Group
{
    public SapWritebackGroup() => Configure("api/webhooks/sap", ep =>
        ep.Roles(MesRoles.ProductionManager, MesRoles.ShiftLeader,
                 MesRoles.QualityEngineer, MesRoles.WarehouseClerk));
}
