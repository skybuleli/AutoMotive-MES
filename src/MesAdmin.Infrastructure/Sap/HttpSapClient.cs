using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MesAdmin.Application.Interfaces;
using MesAdmin.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Sap;

/// <summary>
/// HTTP OData/REST SAP 客户端（生产模式）。
/// 通过 HttpClient 调用 SAP S/4HANA OData API 或自定义 REST 端点。
/// 配置在 appsettings.json 的 Sap:BaseUrl / Sap:Username / Sap:Password。
/// </summary>
public sealed class HttpSapClient : ISapClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpSapClient> _logger;
    private readonly string _baseUrl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly int _timeoutSeconds;

    public HttpSapClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HttpSapClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["Sap:BaseUrl"] ?? "").TrimEnd('/');
        _username = configuration["Sap:Username"];
        _password = configuration["Sap:Password"];
        _timeoutSeconds = configuration.GetValue<int>("Sap:TimeoutSeconds", 30);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("SapClient");
        client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

        if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
        {
            var authBytes = System.Text.Encoding.UTF8.GetBytes($"{_username}:{_password}");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(authBytes));
        }

        return client;
    }

    public async Task<SapWritebackResult> WritebackRejectionAsync(
        SapRejectionRecord rejection, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var payload = new SapRejectionWritebackPayload
            {
                ExternalOrderNumber = rejection.ExternalOrderNumber,
                ProductCode = rejection.ProductCode,
                BomVersion = rejection.BomVersion,
                PlannedQuantity = rejection.PlannedQuantity,
                RejectionReason = rejection.RejectionReason,
                RejectedAt = rejection.RejectedAt.ToString("O"),
            };

            var url = $"{_baseUrl}/api/sap/order/rejection";
            var response = await client.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SapDocumentResponse>(cancellationToken: ct);
            return new SapWritebackResult(true, result?.DocumentNumber);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"SAP 拒单回写失败：外部工单 {rejection.ExternalOrderNumber}");
            return new SapWritebackResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<SapWritebackResult> SendOrderStatusAsync(
        string externalOrderNumber, OrderStatus status,
        int qualifiedQuantity, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var payload = new SapOrderStatusPayload
            {
                ExternalOrderNumber = externalOrderNumber,
                Status = status.ToString(),
                QualifiedQuantity = qualifiedQuantity,
            };

            var url = $"{_baseUrl}/api/sap/order/status";
            var response = await client.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SapDocumentResponse>(cancellationToken: ct);
            return new SapWritebackResult(true, result?.DocumentNumber);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"SAP 工单状态推送失败：外部工单 {externalOrderNumber}");
            return new SapWritebackResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<SapWritebackResult> SendInventorySyncAsync(
        SapInventorySyncRecord record, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var payload = new SapInventoryPayload
            {
                OrderNumber = record.OrderNumber,
                MaterialCode = record.MaterialCode,
                Quantity = record.Quantity,
                MovementType = record.MovementType,
                Unit = record.Unit,
            };

            var url = $"{_baseUrl}/api/sap/inventory/sync";
            var response = await client.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SapDocumentResponse>(cancellationToken: ct);
            return new SapWritebackResult(true, result?.DocumentNumber);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"SAP 库存同步失败：物料 {record.MaterialCode}");
            return new SapWritebackResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<SapWritebackResult> SendMaterialMovementAsync(
        string orderNumber, string materialCode, double quantity,
        string movementType, string unit, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var payload = new SapMaterialMovementPayload
            {
                OrderNumber = orderNumber,
                MaterialCode = materialCode,
                Quantity = quantity,
                MovementType = movementType,
                Unit = unit,
            };

            var url = $"{_baseUrl}/api/sap/material/movement";
            var response = await client.PostAsJsonAsync(url, payload, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SapDocumentResponse>(cancellationToken: ct);
            return new SapWritebackResult(true, result?.DocumentNumber);
        }
        catch (Exception ex)
        {
            _logger.ZLogError(ex, $"SAP 物料移动失败：物料 {materialCode}");
            return new SapWritebackResult(false, ErrorMessage: ex.Message);
        }
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync($"{_baseUrl}/api/sap/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.ZLogWarning(ex, $"SAP 健康检查失败");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  请求 / 响应 DTO
    // ═══════════════════════════════════════════════════════

    private sealed class SapRejectionWritebackPayload
    {
        [JsonPropertyName("external_order_number")]
        public string? ExternalOrderNumber { get; set; }
        [JsonPropertyName("product_code")] public string ProductCode { get; set; } = string.Empty;
        [JsonPropertyName("bom_version")] public string BomVersion { get; set; } = string.Empty;
        [JsonPropertyName("planned_quantity")] public int PlannedQuantity { get; set; }
        [JsonPropertyName("rejection_reason")] public string RejectionReason { get; set; } = string.Empty;
        [JsonPropertyName("rejected_at")] public string RejectedAt { get; set; } = string.Empty;
    }

    private sealed class SapOrderStatusPayload
    {
        [JsonPropertyName("external_order_number")] public string ExternalOrderNumber { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("qualified_quantity")] public int QualifiedQuantity { get; set; }
    }

    private sealed class SapInventoryPayload
    {
        [JsonPropertyName("order_number")] public string OrderNumber { get; set; } = string.Empty;
        [JsonPropertyName("material_code")] public string MaterialCode { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public double Quantity { get; set; }
        [JsonPropertyName("movement_type")] public string MovementType { get; set; } = string.Empty;
        [JsonPropertyName("unit")] public string Unit { get; set; } = string.Empty;
    }

    private sealed class SapMaterialMovementPayload
    {
        [JsonPropertyName("order_number")] public string OrderNumber { get; set; } = string.Empty;
        [JsonPropertyName("material_code")] public string MaterialCode { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public double Quantity { get; set; }
        [JsonPropertyName("movement_type")] public string MovementType { get; set; } = string.Empty;
        [JsonPropertyName("unit")] public string Unit { get; set; } = string.Empty;
    }

    private sealed class SapDocumentResponse
    {
        [JsonPropertyName("document_number")] public string? DocumentNumber { get; set; }
    }
}
