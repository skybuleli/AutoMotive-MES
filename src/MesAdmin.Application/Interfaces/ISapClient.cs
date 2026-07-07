using MesAdmin.Domain.Models;

namespace MesAdmin.Application.Interfaces;

/// <summary>
/// SAP 通信客户端接口（T3.14-T3.17）。
/// 封装 MES → SAP 的 OData/REST API 调用。
/// 开发环境使用 MockSapClient，生产环境使用 HttpSapClient。
/// 若后续对接 SAP NCo / ERPConnect / SapNwRfc，仅需新增实现类。
/// </summary>
public interface ISapClient
{
    /// <summary>T3.16: 将拒单原因回写 SAP</summary>
    Task<SapWritebackResult> WritebackRejectionAsync(
        SapRejectionRecord rejection, CancellationToken ct = default);

    /// <summary>T3.14: 向 SAP 推送工单状态变更</summary>
    Task<SapWritebackResult> SendOrderStatusAsync(
        string externalOrderNumber, OrderStatus status,
        int qualifiedQuantity, CancellationToken ct = default);

    /// <summary>T3.15: 向 SAP 同步库存变动</summary>
    Task<SapWritebackResult> SendInventorySyncAsync(
        SapInventorySyncRecord record, CancellationToken ct = default);

    /// <summary>T3.17: 向 SAP 推送物料移动凭证</summary>
    Task<SapWritebackResult> SendMaterialMovementAsync(
        string orderNumber, string materialCode, double quantity,
        string movementType, string unit, CancellationToken ct = default);

    /// <summary>SAP 连接健康检查</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

/// <summary>SAP 调用结果</summary>
public record SapWritebackResult(
    bool Success,
    string? DocumentNumber = null,
    string? ErrorMessage = null);
