using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// 模拟 SAP 客户端（开发模式）。
/// 所有调用仅记录日志，返回模拟成功结果。
/// </summary>
public sealed class MockSapClient(ILogger<MockSapClient> logger) : ISapClient
{
    private static int _mockDocSeq;

    public Task<SapWritebackResult> WritebackRejectionAsync(
        SapRejectionRecord rejection, CancellationToken ct = default)
    {
        var docNumber = $"MOCK-REJ-{Interlocked.Increment(ref _mockDocSeq):D8}";
        logger.ZLogInformation($"[MOCK SAP] 拒单回写：外部工单 {rejection.ExternalOrderNumber}，原因「{rejection.RejectionReason}」，凭证号 {docNumber}");
        return Task.FromResult(new SapWritebackResult(true, docNumber));
    }

    public Task<SapWritebackResult> SendOrderStatusAsync(
        string externalOrderNumber, OrderStatus status,
        int qualifiedQuantity, CancellationToken ct = default)
    {
        var docNumber = $"MOCK-ORD-{Interlocked.Increment(ref _mockDocSeq):D8}";
        logger.ZLogInformation($"[MOCK SAP] 工单状态推送：外部工单 {externalOrderNumber}，状态 {status}，合格数 {qualifiedQuantity}，凭证号 {docNumber}");
        return Task.FromResult(new SapWritebackResult(true, docNumber));
    }

    public Task<SapWritebackResult> SendInventorySyncAsync(
        SapInventorySyncRecord record, CancellationToken ct = default)
    {
        var docNumber = $"MOCK-INV-{Interlocked.Increment(ref _mockDocSeq):D8}";
        logger.ZLogInformation($"[MOCK SAP] 库存同步：物料 {record.MaterialCode}，数量 {record.Quantity}，移动类型 {record.MovementType}，凭证号 {docNumber}");
        return Task.FromResult(new SapWritebackResult(true, docNumber));
    }

    public Task<SapWritebackResult> SendMaterialMovementAsync(
        string orderNumber, string materialCode, double quantity,
        string movementType, string unit, CancellationToken ct = default)
    {
        var docNumber = $"MOCK-MAT-{Interlocked.Increment(ref _mockDocSeq):D8}";
        logger.ZLogInformation($"[MOCK SAP] 物料移动：工单 {orderNumber}，物料 {materialCode}，数量 {quantity:F2} {unit}，移动类型 {movementType}，凭证号 {docNumber}");
        return Task.FromResult(new SapWritebackResult(true, docNumber));
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        logger.ZLogInformation($"[MOCK SAP] 健康检查：OK");
        return Task.FromResult(true);
    }
}
