using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;

namespace MesAdmin.Api.Features.SapWebhooks.Status;

public class SapSyncStatusEndpoint : MesEndpointWithoutRequest<SapSyncStatusResponse>
{
    private readonly ISapRejectionRepository _rejectionRepo;
    private readonly ISapInventorySyncRecordRepository _inventorySyncRepo;
    private readonly ISapOrderSyncRecordRepository _orderSyncRepo;

    public SapSyncStatusEndpoint(
        ISapRejectionRepository rejectionRepo,
        ISapInventorySyncRecordRepository inventorySyncRepo,
        ISapOrderSyncRecordRepository orderSyncRepo)
    {
        _rejectionRepo = rejectionRepo;
        _inventorySyncRepo = inventorySyncRepo;
        _orderSyncRepo = orderSyncRepo;
    }

    public override void Configure()
    {
        Get("/sync-status");
        Group<SapWebhookGroup>();
        Summary(s =>
        {
            s.Summary = "SAP 同步状态概览";
            s.Description = "返回所有待同步/已同步的 SAP 记录计数。";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var pendingRejections = await _rejectionRepo.GetPendingWritebackAsync(ct);
        var pendingInventory = await _inventorySyncRepo.GetPendingSyncAsync(ct);
        var pendingOrders = await _orderSyncRepo.GetPendingSyncAsync(ct);

        Response = new SapSyncStatusResponse
        {
            PendingRejectionCount = pendingRejections.Count,
            PendingInventorySyncCount = pendingInventory.Count,
            PendingOrderSyncCount = pendingOrders.Count,
            TotalPending = pendingRejections.Count + pendingInventory.Count + pendingOrders.Count,
        };
    }
}

public class SapSyncStatusResponse
{
    public int PendingRejectionCount { get; set; }
    public int PendingInventorySyncCount { get; set; }
    public int PendingOrderSyncCount { get; set; }
    public int TotalPending { get; set; }
}
