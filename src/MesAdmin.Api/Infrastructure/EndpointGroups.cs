using FastEndpoints;

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

/// <summary>SAP Webhook 端点组（api/webhooks/sap，匿名访问）</summary>
public class SapWebhookGroup : Group
{
    public SapWebhookGroup() => Configure("api/webhooks/sap", ep => ep.AllowAnonymous());
}
