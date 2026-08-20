using FastEndpoints;
using MesAdmin.Api.Infrastructure;
using MesAdmin.Application.Interfaces;

namespace MesAdmin.Api.Features.Sync;

// ═══════════════════════════════════════════
//  GET /api/v1/sync/sap-pending
//  查询 SAP 待同步记录明细（拒单回写 + 库存 + 工单状态）
// ═══════════════════════════════════════════

public sealed class GetSapPendingEndpoint : EndpointWithoutRequest<List<SapPendingItem>>
{
    private readonly ISapRejectionRepository _rejectionRepo;
    private readonly ISapInventorySyncRecordRepository _inventorySyncRepo;
    private readonly ISapOrderSyncRecordRepository _orderSyncRepo;

    public GetSapPendingEndpoint(
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
        Get("/sap-pending");
        Group<SyncGroup>();
        Summary(s => s.Summary = "查询 SAP 待同步记录明细（拒单回写/库存/工单状态）");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var items = new List<SapPendingItem>();

        var rejections = await _rejectionRepo.GetPendingWritebackAsync(ct);
        foreach (var r in rejections)
        {
            items.Add(new SapPendingItem(
                r.Id.ToString(),
                "rejection",
                "拒单回写",
                r.ExternalOrderNumber,
                $"{r.ProductCode} · {r.PlannedQuantity} 件",
                r.RejectionReason,
                r.WritebackError,
                r.RejectedAt));
        }

        var inventory = await _inventorySyncRepo.GetPendingSyncAsync(ct);
        foreach (var r in inventory)
        {
            items.Add(new SapPendingItem(
                r.Id.ToString(),
                "inventory",
                "库存过账",
                r.OrderNumber,
                $"{r.MaterialCode} · {r.Quantity:0.###} {r.Unit} · 移动类型 {r.MovementType}",
                null,
                r.SyncError,
                r.CreatedAt));
        }

        var orders = await _orderSyncRepo.GetPendingSyncAsync(ct);
        foreach (var r in orders)
        {
            items.Add(new SapPendingItem(
                r.Id.ToString(),
                "order",
                "工单状态",
                $"{r.OrderNumber} / {r.ExternalOrderNumber}",
                $"状态 {r.Status} · 合格 {r.QualifiedQuantity}",
                null,
                r.SyncError,
                r.CreatedAt));
        }

        await Send.OkAsync(items.OrderByDescending(i => i.CreatedAt).ToList(), ct);
    }
}

/// <summary>SAP 待同步记录明细项</summary>
public sealed record SapPendingItem(
    string Id,
    string Type,
    string TypeLabel,
    string? Reference,
    string Detail,
    string? Reason,
    string? Error,
    DateTimeOffset CreatedAt);
