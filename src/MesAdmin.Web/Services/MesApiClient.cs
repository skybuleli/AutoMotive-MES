using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MesAdmin.Web.Services;

/// <summary>
/// API 客户端：通过 HttpClient 调用后端 API，自动附加 JWT Bearer token。
/// 所有 Web 页面通过此客户端访问 API，不再直接注入 Application 层服务。
/// </summary>
public class MesApiClient
{
    private readonly HttpClient _http;
    private readonly ProtectedLocalStorage _localStorage;
    private const string TokenKey = "mes_auth_token";

    public MesApiClient(IHttpClientFactory factory, ProtectedLocalStorage localStorage)
    {
        _http = factory.CreateClient("MesApi");
        _localStorage = localStorage;
    }

    /// <summary>发送带 JWT 的 GET 请求</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        await AttachTokenAsync(req);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    /// <summary>发送带 JWT 的 GET 请求，并读取列表总数响应头</summary>
    public async Task<(T? Data, int? Total)> GetWithTotalAsync<T>(string path, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        await AttachTokenAsync(req);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Headers.TryGetValues("X-Total-Count", out var values)
            && int.TryParse(values.FirstOrDefault(), out var parsed)
                ? parsed
                : (int?)null;
        var data = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return (data, total);
    }

    /// <summary>发送带 JWT 的 POST 请求</summary>
    public async Task<(bool Ok, T? Data, int Status)> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        await AttachTokenAsync(req);
        req.Content = JsonContent.Create(body);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, default, (int)resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return (true, data, (int)resp.StatusCode);
    }

    /// <summary>发送带 JWT 的 PATCH 请求</summary>
    public async Task<(bool Ok, T? Data, int Status)> PatchAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, path);
        await AttachTokenAsync(req);
        req.Content = JsonContent.Create(body);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, default, (int)resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return (true, data, (int)resp.StatusCode);
    }

    /// <summary>发送带 JWT 的 POST 请求（无响应体）</summary>
    public async Task<(bool Ok, int Status)> PostNoBodyAsync(string path, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        await AttachTokenAsync(req);
        var resp = await _http.SendAsync(req, ct);
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode);
    }

    /// <summary>发送带 JWT 的 PUT 请求</summary>
    public async Task<(bool Ok, T? Data, int Status)> PutAsync<T>(string path, object body, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, path);
        await AttachTokenAsync(req);
        req.Content = JsonContent.Create(body);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, default, (int)resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        return (true, data, (int)resp.StatusCode);
    }

    public Task<List<TraceabilityLinkDto>?> GetForwardTraceAsync(string vinOrSerial, CancellationToken ct = default)
        => GetAsync<List<TraceabilityLinkDto>>($"api/v1/traceability/forward/{Uri.EscapeDataString(vinOrSerial)}", ct);

    public Task<List<TraceabilityLinkDto>?> GetReverseTraceAsync(string batchType, string batch, CancellationToken ct = default)
        => GetAsync<List<TraceabilityLinkDto>>($"api/v1/traceability/reverse/{Uri.EscapeDataString(batchType)}/{Uri.EscapeDataString(batch)}", ct);

    /// <summary>T1.4 齐套检查：对指定工单执行 BOM 展开→库存检查→缺料 JIT 拉动。</summary>
    public Task<(bool Ok, KitCheckResponse? Data, int Status)> KitCheckAsync(string orderId, CancellationToken ct = default)
        => PostAsync<KitCheckResponse>($"api/v1/orders/{orderId}/kit-check", new { }, ct);

    // ═══════════════════════════════════════════
    // T2.1-T2.10 SPC 质量管理 API
    // ═══════════════════════════════════════════

    /// <summary>查询 SPC 样本（按特性编码）</summary>
    public Task<List<SpcSampleDto>?> GetSpcSamplesAsync(string charCode, int limit = 25, CancellationToken ct = default)
        => GetAsync<List<SpcSampleDto>>($"api/v1/quality/spc/samples?charCode={Uri.EscapeDataString(charCode)}&limit={limit}", ct);

    /// <summary>查询未确认 SPC 告警</summary>
    public Task<List<SpcAlertDto>?> GetSpcAlertsAsync(string? charCode = null, CancellationToken ct = default)
    {
        var url = "api/v1/quality/spc/alerts";
        if (charCode is not null)
            url += $"?charCode={Uri.EscapeDataString(charCode)}";
        return GetAsync<List<SpcAlertDto>>(url, ct);
    }

    /// <summary>查询检验记录列表</summary>
    public Task<List<QualityRecordDto>?> GetQualityRecordsAsync(string stage, CancellationToken ct = default)
        => GetAsync<List<QualityRecordDto>>($"api/v1/quality/records?stage={Uri.EscapeDataString(stage)}", ct);

    /// <summary>查询 NCR 列表</summary>
    public Task<List<NcrDto>?> GetNcrListAsync(string? status = null, CancellationToken ct = default)
    {
        var url = "api/v1/quality/ncr";
        if (status is not null)
            url += $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<List<NcrDto>>(url, ct);
    }

    /// <summary>提交 NCR 评审</summary>
    public Task<(bool Ok, NcrDto? Data, int Status)> SubmitNcrReviewAsync(string ncrId, string reviewerId, CancellationToken ct = default)
        => PostAsync<NcrDto>($"api/v1/quality/ncr/{ncrId}/review", new { ReviewerId = reviewerId }, ct);

    /// <summary>NCR 处置决定</summary>
    public Task<(bool Ok, NcrDto? Data, int Status)> DispositionNcrAsync(string ncrId, string disposition, string comments, CancellationToken ct = default)
        => PostAsync<NcrDto>($"api/v1/quality/ncr/{ncrId}/disposition", new { Disposition = disposition, Comments = comments }, ct);

    /// <summary>关闭 NCR</summary>
    public Task<(bool Ok, NcrDto? Data, int Status)> CloseNcrAsync(string ncrId, string remarks, CancellationToken ct = default)
        => PostAsync<NcrDto>($"api/v1/quality/ncr/{ncrId}/close", new { Remarks = remarks }, ct);

    /// <summary>创建 8D 报告</summary>
    public Task<(bool Ok, EightDDto? Data, int Status)> CreateEightDAsync(string title, string productCode, string productName, string? ncrId = null, CancellationToken ct = default)
        => PostAsync<EightDDto>("api/v1/quality/8d", new { NcrId = ncrId, Title = title, ProductCode = productCode, ProductName = productName }, ct);

    /// <summary>查询 8D 报告列表</summary>
    public Task<List<EightDDto>?> GetEightDListAsync(string? status = null, CancellationToken ct = default)
    {
        var url = "api/v1/quality/8d";
        if (status is not null)
            url += $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<List<EightDDto>>(url, ct);
    }

    /// <summary>关闭 8D 报告</summary>
    public Task<(bool Ok, EightDDto? Data, int Status)> CloseEightDAsync(string reportId, string summary, CancellationToken ct = default)
        => PostAsync<EightDDto>($"api/v1/quality/8d/{reportId}/close", new { Summary = summary }, ct);

    // ═══════════════════════════════════════════
    // T2.20-T2.23 Andon 报警 API
    // ═══════════════════════════════════════════

    /// <summary>查询 Andon 报警列表</summary>
    public Task<List<AndonEventDto>?> GetAndonListAsync(string? status = null, string? equipmentCode = null, string? severity = null, CancellationToken ct = default)
    {
        var url = "api/v1/andon";
        var query = new List<string>();
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        if (equipmentCode is not null) query.Add($"equipmentCode={Uri.EscapeDataString(equipmentCode)}");
        if (severity is not null) query.Add($"severity={Uri.EscapeDataString(severity)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return GetAsync<List<AndonEventDto>>(url, ct);
    }

    /// <summary>查询 Andon 统计</summary>
    public Task<AndonStatsDto?> GetAndonStatsAsync(CancellationToken ct = default)
        => GetAsync<AndonStatsDto>("api/v1/andon/stats", ct);

    /// <summary>确认 Andon 报警</summary>
    public Task<(bool Ok, AndonEventDto? Data, int Status)> AcknowledgeAndonAsync(string eventId, string acknowledgedBy, CancellationToken ct = default)
        => PostAsync<AndonEventDto>($"api/v1/andon/{eventId}/acknowledge", new { AcknowledgedBy = acknowledgedBy }, ct);

    /// <summary>解决 Andon 报警</summary>
    public Task<(bool Ok, AndonEventDto? Data, int Status)> ResolveAndonAsync(string eventId, string resolvedBy, string resolution, CancellationToken ct = default)
        => PostAsync<AndonEventDto>($"api/v1/andon/{eventId}/resolve", new { ResolvedBy = resolvedBy, Resolution = resolution }, ct);

    /// <summary>关闭 Andon 报警</summary>
    public Task<(bool Ok, AndonEventDto? Data, int Status)> CloseAndonAsync(string eventId, string closeRemarks, CancellationToken ct = default)
        => PostAsync<AndonEventDto>($"api/v1/andon/{eventId}/close", new { CloseRemarks = closeRemarks }, ct);

    // ═══════════════════════════════════════════
    // T2.18 备件管理 API
    // ═══════════════════════════════════════════

    /// <summary>查询备件列表（支持 low-stock / needs-restock 过滤）</summary>
    public Task<List<SparePartDto>?> GetSparePartsAsync(string? filter = null, CancellationToken ct = default)
    {
        var url = "api/v1/maintenance/spare-parts";
        if (filter is not null)
            url += $"?filter={Uri.EscapeDataString(filter)}";
        return GetAsync<List<SparePartDto>>(url, ct);
    }

    /// <summary>查询备件详情</summary>
    public Task<SparePartDto?> GetSparePartAsync(string id, CancellationToken ct = default)
        => GetAsync<SparePartDto>($"api/v1/maintenance/spare-parts/{id}", ct);

    /// <summary>创建备件</summary>
    public Task<(bool Ok, SparePartDto? Data, int Status)> CreateSparePartAsync(CreateSparePartBody body, CancellationToken ct = default)
        => PostAsync<SparePartDto>("api/v1/maintenance/spare-parts", body, ct);

    /// <summary>更新库存（盘点）</summary>
    public Task<(bool Ok, SparePartDto? Data, int Status)> UpdateSparePartStockAsync(string id, double newQuantity, CancellationToken ct = default)
        => PutAsync<SparePartDto>($"api/v1/maintenance/spare-parts/{id}/stock", new UpdateStockBody(newQuantity), ct);

    /// <summary>补货入库</summary>
    public Task<(bool Ok, SparePartDto? Data, int Status)> RestockSparePartAsync(string id, double quantity, CancellationToken ct = default)
        => PostAsync<SparePartDto>($"api/v1/maintenance/spare-parts/{id}/restock", new RestockBody(quantity), ct);

    /// <summary>检查库存（不足时自动生成采购申请）</summary>
    public async Task<(bool Ok, StockCheckDto? Data, int Status)> CheckStockAsync(string id, CancellationToken ct = default)
        => await PostAsync<StockCheckDto>($"api/v1/maintenance/spare-parts/{id}/check-stock", new { }, ct);

    /// <summary>查询维护工单的备件使用记录</summary>
    public Task<List<SparePartUsageDto>?> GetWorkOrderSparePartsAsync(string orderId, CancellationToken ct = default)
        => GetAsync<List<SparePartUsageDto>>($"api/v1/maintenance/orders/{orderId}/spare-parts", ct);

    /// <summary>消耗备件</summary>
    public Task<(bool Ok, SparePartUsageDto? Data, int Status)> ConsumeSparePartAsync(string orderId, ConsumeSparePartBody body, CancellationToken ct = default)
        => PostAsync<SparePartUsageDto>($"api/v1/maintenance/orders/{orderId}/spare-parts", body, ct);

    /// <summary>查询备件使用历史</summary>
    public Task<List<SparePartUsageDto>?> GetSparePartUsagesAsync(string sparePartId, CancellationToken ct = default)
        => GetAsync<List<SparePartUsageDto>>($"api/v1/maintenance/spare-parts/{sparePartId}/usages", ct);

    /// <summary>查询采购申请列表</summary>
    public Task<List<PurchaseRequestDto>?> GetPurchaseRequestsAsync(string? status = null, CancellationToken ct = default)
    {
        var url = "api/v1/maintenance/purchase-requests";
        if (status is not null)
            url += $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<List<PurchaseRequestDto>>(url, ct);
    }

    /// <summary>手动创建采购申请</summary>
    public Task<(bool Ok, PurchaseRequestDto? Data, int Status)> CreatePurchaseRequestAsync(CreatePurchaseRequestBody body, CancellationToken ct = default)
        => PostAsync<PurchaseRequestDto>("api/v1/maintenance/purchase-requests", body, ct);

    /// <summary>审批采购申请</summary>
    public Task<(bool Ok, PurchaseRequestDto? Data, int Status)> ApprovePurchaseRequestAsync(string id, string approvedBy, CancellationToken ct = default)
        => PostAsync<PurchaseRequestDto>($"api/v1/maintenance/purchase-requests/{id}/approve", new ApprovePurchaseRequestBody(approvedBy), ct);

    /// <summary>取消采购申请</summary>
    public Task<(bool Ok, PurchaseRequestDto? Data, int Status)> CancelPurchaseRequestAsync(string id, string reason, CancellationToken ct = default)
        => PostAsync<PurchaseRequestDto>($"api/v1/maintenance/purchase-requests/{id}/cancel", new { Reason = reason }, ct);

    // ═══════════════════════════════════════════
    // T3.1-T3.5 M07 工艺管理 API
    // ═══════════════════════════════════════════

    /// <summary>查询工艺路线列表</summary>
    public Task<List<RoutingResponseDto>?> GetRoutingsAsync(string? productCode = null, string? ecoStatus = null, CancellationToken ct = default)
    {
        var url = "api/v1/routing";
        var query = new List<string>();
        if (productCode is not null) query.Add($"productCode={Uri.EscapeDataString(productCode)}");
        if (ecoStatus is not null) query.Add($"ecoStatus={Uri.EscapeDataString(ecoStatus)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return GetAsync<List<RoutingResponseDto>>(url, ct);
    }

    /// <summary>查询工艺路线详情</summary>
    public Task<RoutingResponseDto?> GetRoutingByIdAsync(string id, CancellationToken ct = default)
        => GetAsync<RoutingResponseDto>($"api/v1/routing/{id}", ct);

    /// <summary>查询当前生效工艺路线</summary>
    public Task<RoutingResponseDto?> GetActiveRoutingAsync(string productCode, CancellationToken ct = default)
        => GetAsync<RoutingResponseDto>($"api/v1/routing/active?productCode={Uri.EscapeDataString(productCode)}", ct);

    /// <summary>创建工艺路线</summary>
    public Task<(bool Ok, RoutingResponseDto? Data, int Status)> CreateRoutingAsync(CreateRoutingBody body, CancellationToken ct = default)
        => PostAsync<RoutingResponseDto>("api/v1/routing", body, ct);

    /// <summary>提交 ECO 审批</summary>
    public Task<(bool Ok, RoutingResponseDto? Data, int Status)> SubmitRoutingAsync(string id, CancellationToken ct = default)
        => PostAsync<RoutingResponseDto>($"api/v1/routing/{id}/submit", new { }, ct);

    /// <summary>审批通过工艺路线</summary>
    public Task<(bool Ok, RoutingResponseDto? Data, int Status)> ApproveRoutingAsync(string id, string approvedBy, CancellationToken ct = default)
        => PostAsync<RoutingResponseDto>($"api/v1/routing/{id}/approve", new ApproveRoutingBody(approvedBy), ct);

    /// <summary>发布生效</summary>
    public Task<(bool Ok, RoutingResponseDto? Data, int Status)> ReleaseRoutingAsync(string id, CancellationToken ct = default)
        => PostAsync<RoutingResponseDto>($"api/v1/routing/{id}/release", new { }, ct);

    private async Task AttachTokenAsync(HttpRequestMessage req)
    {
        try
        {
            var result = await _localStorage.GetAsync<string>(TokenKey);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Value))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.Value);
        }
        catch (InvalidOperationException)
        {
            // SSR 预渲染阶段 ProtectedLocalStorage 不可用，跳过
        }
    }
}
