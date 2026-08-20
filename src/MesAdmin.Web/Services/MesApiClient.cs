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

    /// <summary>确认 SPC 判异告警</summary>
    public Task<(bool Ok, SpcAlertDto? Data, int Status)> AcknowledgeSpcAlertAsync(
        string alertId, string acknowledgedBy, string? actionTaken, CancellationToken ct = default)
        => PostAsync<SpcAlertDto>($"api/v1/quality/spc/alerts/{alertId}/ack",
            new AcknowledgeSpcAlertBody(acknowledgedBy, actionTaken), ct);

    /// <summary>查询检验记录列表（按阶段：Iq/Ipqc/Oqc/FirstArticle/OnlineTest）</summary>
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

    /// <summary>更新 8D 报告步骤内容（D1-D7）</summary>
    public Task<(bool Ok, EightDDto? Data, int Status)> UpdateEightDAsync(string reportId, UpdateEightDBody body, CancellationToken ct = default)
        => PutAsync<EightDDto>($"api/v1/quality/8d/{reportId}", body, ct);

    /// <summary>创建 IQC 来料检验记录</summary>
    public Task<(bool Ok, QualityRecordDto? Data, int Status)> CreateIqcAsync(CreateIqcBody body, CancellationToken ct = default)
        => PostAsync<QualityRecordDto>("api/v1/quality/iqc", body, ct);

    /// <summary>记录 IQC 检验特性实测值</summary>
    public Task<(bool Ok, QualityRecordDto? Data, int Status)> RecordIqcMeasurementAsync(
        string id, string characteristicCode, double actualValue, CancellationToken ct = default)
        => PostAsync<QualityRecordDto>($"api/v1/quality/iqc/{id}/measure",
            new RecordIqcMeasurementBody(characteristicCode, actualValue), ct);

    /// <summary>完成 IQC 检验并自动判定</summary>
    public Task<(bool Ok, QualityRecordDto? Data, int Status)> CompleteIqcAsync(string id, CancellationToken ct = default)
        => PostAsync<QualityRecordDto>($"api/v1/quality/iqc/{id}/complete", new { }, ct);

    /// <summary>创建 IPQC 过程巡检记录</summary>
    public Task<(bool Ok, QualityRecordDto? Data, int Status)> CreateIpqcAsync(CreateIpqcBody body, CancellationToken ct = default)
        => PostAsync<QualityRecordDto>("api/v1/quality/ipqc", body, ct);

    /// <summary>查询检验计划列表（按阶段/产品筛选）</summary>
    public Task<List<InspectionPlanDto>?> GetInspectionPlansAsync(string? stage = null, string? productCode = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (stage is not null) query.Add($"stage={Uri.EscapeDataString(stage)}");
        if (productCode is not null) query.Add($"productCode={Uri.EscapeDataString(productCode)}");
        var url = "api/v1/quality/plans" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return GetAsync<List<InspectionPlanDto>>(url, ct);
    }

    /// <summary>创建检验计划</summary>
    public Task<(bool Ok, InspectionPlanDto? Data, int Status)> CreateInspectionPlanAsync(CreateInspectionPlanBody body, CancellationToken ct = default)
        => PostAsync<InspectionPlanDto>("api/v1/quality/plans", body, ct);

    /// <summary>启用/停用检验计划</summary>
    public Task<(bool Ok, InspectionPlanDto? Data, int Status)> ToggleInspectionPlanAsync(string id, CancellationToken ct = default)
        => PostAsync<InspectionPlanDto>($"api/v1/quality/plans/{id}/toggle", new { }, ct);

    /// <summary>完成 IPQC 检验并自动判定</summary>
    public Task<(bool Ok, QualityRecordDto? Data, int Status)> CompleteIpqcAsync(string id, CancellationToken ct = default)
        => PostAsync<QualityRecordDto>($"api/v1/quality/ipqc/{id}/complete", new { }, ct);

    // ═══════════════════════════════════════════
    // 液压测试台 API
    // ═══════════════════════════════════════════

    /// <summary>查询液压测试台最新测试结果</summary>
    public Task<HydraulicTestDto?> GetHydraulicTestLatestAsync(CancellationToken ct = default)
        => GetAsync<HydraulicTestDto>("api/v1/hydraulic-test/latest", ct);

    /// <summary>查询液压测试历史记录</summary>
    public Task<List<HydraulicTestDto>?> GetHydraulicTestHistoryAsync(int limit = 50, CancellationToken ct = default)
        => GetAsync<List<HydraulicTestDto>>($"api/v1/hydraulic-test/history?limit={limit}", ct);

    /// <summary>按 Id 查询液压测试结果</summary>
    public Task<HydraulicTestDto?> GetHydraulicTestAsync(string id, CancellationToken ct = default)
        => GetAsync<HydraulicTestDto>($"api/v1/hydraulic-test/{id}", ct);

    /// <summary>质量工程师解锁液压测试设备</summary>
    public Task<(bool Ok, object? Data, int Status)> UnlockHydraulicEquipmentAsync(string id, string unlockedBy, CancellationToken ct = default)
        => PostAsync<object>($"api/v1/hydraulic-test/{id}/unlock", new UnlockHydraulicBody(unlockedBy), ct);
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
    // T2.17 预防性维护 API（维护计划 + 维护工单）
    // ═══════════════════════════════════════════

    /// <summary>查询维护计划列表</summary>
    public Task<List<MaintenancePlanDto>?> GetMaintenancePlansAsync(CancellationToken ct = default)
        => GetAsync<List<MaintenancePlanDto>>("api/v1/maintenance/plans", ct);

    /// <summary>创建维护计划</summary>
    public Task<(bool Ok, MaintenancePlanDto? Data, int Status)> CreateMaintenancePlanAsync(CreateMaintenancePlanBody body, CancellationToken ct = default)
        => PostAsync<MaintenancePlanDto>("api/v1/maintenance/plans", body, ct);

    /// <summary>查询维护工单列表（支持设备/状态/条数过滤）</summary>
    public Task<List<MaintenanceOrderDto>?> GetMaintenanceOrdersAsync(string? equipmentCode = null, string? status = null, int? limit = null, CancellationToken ct = default)
    {
        var url = "api/v1/maintenance/orders";
        var query = new List<string>();
        if (equipmentCode is not null) query.Add($"equipmentCode={Uri.EscapeDataString(equipmentCode)}");
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        if (limit is not null) query.Add($"limit={limit}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return GetAsync<List<MaintenanceOrderDto>>(url, ct);
    }

    /// <summary>开始执行维护工单</summary>
    public Task<(bool Ok, MaintenanceOrderDto? Data, int Status)> StartMaintenanceOrderAsync(string id, string assignedTo, CancellationToken ct = default)
        => PostAsync<MaintenanceOrderDto>($"api/v1/maintenance/orders/{id}/start", new StartMaintenanceOrderBody(assignedTo), ct);

    /// <summary>完成维护工单</summary>
    public Task<(bool Ok, MaintenanceOrderDto? Data, int Status)> CompleteMaintenanceOrderAsync(string id, string completedBy, string remarks, CancellationToken ct = default)
        => PostAsync<MaintenanceOrderDto>($"api/v1/maintenance/orders/{id}/complete", new CompleteMaintenanceOrderBody(completedBy, remarks), ct);

    /// <summary>取消维护工单</summary>
    public Task<(bool Ok, MaintenanceOrderDto? Data, int Status)> CancelMaintenanceOrderAsync(string id, string reason, CancellationToken ct = default)
        => PostAsync<MaintenanceOrderDto>($"api/v1/maintenance/orders/{id}/cancel", new CancelMaintenanceOrderBody(reason), ct);

    // ═══════════════════════════════════════════
    // T1.5 首件检验 API
    // ═══════════════════════════════════════════

    /// <summary>查询工单首件检验列表（按创建时间倒序）</summary>
    public Task<List<InspectionDto>?> GetOrderInspectionsAsync(string orderId, CancellationToken ct = default)
        => GetAsync<List<InspectionDto>>($"api/v1/orders/{orderId}/inspections", ct);

    /// <summary>查询首件检验详情</summary>
    public Task<InspectionDto?> GetInspectionAsync(string orderId, string inspectionId, CancellationToken ct = default)
        => GetAsync<InspectionDto>($"api/v1/orders/{orderId}/inspections/{inspectionId}", ct);

    /// <summary>创建首件检验任务（班组长/质量工程师）</summary>
    public Task<(bool Ok, InspectionDto? Data, int Status)> CreateInspectionAsync(string orderId, string inspectionType, string operatorId, CancellationToken ct = default)
        => PostAsync<InspectionDto>($"api/v1/orders/{orderId}/inspections", new CreateInspectionBody(inspectionType, operatorId), ct);

    /// <summary>记录检验项实测值（质量工程师）</summary>
    public Task<(bool Ok, InspectionDto? Data, int Status)> RecordInspectionValueAsync(string orderId, string inspectionId, string characteristicCode, double actualValue, CancellationToken ct = default)
        => PatchAsync<InspectionDto>($"api/v1/orders/{orderId}/inspections/{inspectionId}/items/{characteristicCode}", new RecordInspectionValueBody(actualValue), ct);

    /// <summary>完成首件检验（质量工程师审核放行）</summary>
    public Task<(bool Ok, InspectionDto? Data, int Status)> CompleteInspectionAsync(string orderId, string inspectionId, string inspectorId, CancellationToken ct = default)
        => PostAsync<InspectionDto>($"api/v1/orders/{orderId}/inspections/{inspectionId}/complete", new CompleteInspectionBody(inspectorId), ct);

    // ═══════════════════════════════════════════
    // T2.9 质量报表（QuestPDF 日报/周报/月报/自定义）
    // ═══════════════════════════════════════════

    /// <summary>获取质量报表 PDF 字节（daily / weekly / monthly）</summary>
    public async Task<(bool Ok, byte[]? Pdf, int Status)> GetReportPdfAsync(string reportType, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/quality/reports/{reportType}");
        await AttachTokenAsync(req);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, null, (int)resp.StatusCode);
        var pdf = await resp.Content.ReadAsByteArrayAsync(ct);
        return (true, pdf, (int)resp.StatusCode);
    }

    /// <summary>自定义时间范围质量报表 PDF（仅质量工程师）</summary>
    public async Task<(bool Ok, byte[]? Pdf, int Status)> GetCustomReportPdfAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/quality/reports/custom");
        await AttachTokenAsync(req);
        // DateTime.Today 为 Kind=Local，序列化会带本地偏移（+08:00），Npgsql 写 timestamptz 会报错，
        // 因此统一用显式 UTC 偏移发送。
        req.Content = JsonContent.Create(new
        {
            StartDate = new DateTimeOffset(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(endDate.Year, endDate.Month, endDate.Day, 0, 0, 0, TimeSpan.Zero)
        });
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, null, (int)resp.StatusCode);
        var pdf = await resp.Content.ReadAsByteArrayAsync(ct);
        return (true, pdf, (int)resp.StatusCode);
    }

    // ═══════════════════════════════════════════
    // T4.4-T4.5 SAP / 离线同步监控 API
    // ═══════════════════════════════════════════

    /// <summary>查询离线同步状态概览（统计 + 通道健康）</summary>
    public Task<SyncStatusDto?> GetSyncStatusAsync(CancellationToken ct = default)
        => GetAsync<SyncStatusDto>("api/v1/sync/status", ct);

    /// <summary>查询待同步/冲突记录（status 可选 Pending/Conflict/Failed）</summary>
    public Task<List<SyncPendingItemDto>?> GetSyncPendingAsync(string? status = null, string? terminalId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        if (terminalId is not null) query.Add($"terminalId={Uri.EscapeDataString(terminalId)}");
        var url = "api/v1/sync/pending" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return GetAsync<List<SyncPendingItemDto>>(url, ct);
    }

    /// <summary>解决离线同步冲突（use_local / use_server / manual）</summary>
    public Task<(bool Ok, SyncPendingItemDto? Data, int Status)> ResolveSyncConflictAsync(string id, string resolution, CancellationToken ct = default)
        => PostAsync<SyncPendingItemDto>($"api/v1/sync/resolve/{id}", new { Resolution = resolution }, ct);

    /// <summary>查询 SAP 同步状态概览（计数）</summary>
    public Task<SapSyncStatusDto?> GetSapSyncStatusAsync(CancellationToken ct = default)
        => GetAsync<SapSyncStatusDto>("api/webhooks/sap/sync-status", ct);

    /// <summary>查询 SAP 待同步记录明细</summary>
    public Task<List<SapPendingItemDto>?> GetSapPendingAsync(CancellationToken ct = default)
        => GetAsync<List<SapPendingItemDto>>("api/v1/sync/sap-pending", ct);

    /// <summary>手动重试 SAP 拒单回写</summary>
    public Task<(bool Ok, SapWritebackResultDto? Data, int Status)> RetrySapWritebackAsync(string rejectionId, CancellationToken ct = default)
        => PostAsync<SapWritebackResultDto>($"api/webhooks/sap/rejections/{rejectionId}/writeback", new { }, ct);

    // ═══════════════════════════════════════════
    // T4.1 报表模板引擎 API
    // ═══════════════════════════════════════════

    /// <summary>查询可用报表模板列表</summary>
    public Task<List<ReportTemplateItemDto>?> GetReportTemplatesAsync(CancellationToken ct = default)
        => GetAsync<List<ReportTemplateItemDto>>("api/v1/reports/templates", ct);

    /// <summary>按模板 + 时间范围生成报表 PDF（质量工程师 / 生产经理）</summary>
    public async Task<(bool Ok, byte[]? Pdf, int Status)> GenerateTemplateReportAsync(
        string templateId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"api/v1/reports/templates/{templateId}/generate");
        await AttachTokenAsync(req);
        // 与自定义报表一致：显式 UTC 偏移，避免本地偏移（+08:00）触发 Npgsql 报错
        req.Content = JsonContent.Create(new
        {
            Id = templateId,
            StartDate = new DateTimeOffset(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(endDate.Year, endDate.Month, endDate.Day, 0, 0, 0, TimeSpan.Zero)
        });
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, null, (int)resp.StatusCode);
        var pdf = await resp.Content.ReadAsByteArrayAsync(ct);
        return (true, pdf, (int)resp.StatusCode);
    }

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

    // ═══════════════════════════════════════════
    // M08 SQE 供应商质量 API (T3.6-T3.8)
    // ═══════════════════════════════════════════

    /// <summary>查询供应商列表</summary>
    public Task<List<SupplierDto>?> GetSuppliersAsync(string? category = null, bool? critical = null, CancellationToken ct = default)
    {
        var url = "api/v1/suppliers/suppliers";
        var query = new List<string>();
        if (category is not null) query.Add($"category={Uri.EscapeDataString(category)}");
        if (critical == true) query.Add("critical=true");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return GetAsync<List<SupplierDto>>(url, ct);
    }

    /// <summary>查询供应商详情</summary>
    public Task<SupplierDto?> GetSupplierAsync(string id, CancellationToken ct = default)
        => GetAsync<SupplierDto>($"api/v1/suppliers/suppliers/{id}", ct);

    /// <summary>创建供应商</summary>
    public Task<(bool Ok, SupplierDto? Data, int Status)> CreateSupplierAsync(CreateSupplierBody body, CancellationToken ct = default)
        => PostAsync<SupplierDto>("api/v1/suppliers/suppliers", body, ct);

    /// <summary>更新供应商</summary>
    public Task<(bool Ok, SupplierDto? Data, int Status)> UpdateSupplierAsync(string id, UpdateSupplierBody body, CancellationToken ct = default)
        => PutAsync<SupplierDto>($"api/v1/suppliers/suppliers/{id}", body, ct);

    /// <summary>更新供应商等级</summary>
    public Task<(bool Ok, SupplierDto? Data, int Status)> UpdateSupplierTierAsync(string id, string tier, CancellationToken ct = default)
        => PostAsync<SupplierDto>($"api/v1/suppliers/suppliers/{id}/update-tier", new UpdateTierBody(tier), ct);

    /// <summary>创建评分卡</summary>
    public Task<(bool Ok, SupplierScoreCardDto? Data, int Status)> CreateScoreCardAsync(string supplierId, CreateScoreCardBody body, CancellationToken ct = default)
        => PostAsync<SupplierScoreCardDto>($"api/v1/suppliers/suppliers/{supplierId}/score", body, ct);

    /// <summary>查询评分卡历史</summary>
    public Task<List<SupplierScoreCardDto>?> GetScoreCardsAsync(string supplierId, CancellationToken ct = default)
        => GetAsync<List<SupplierScoreCardDto>>($"api/v1/suppliers/suppliers/{supplierId}/scores", ct);

    /// <summary>查询 PPAP 文档列表</summary>
    public Task<List<PpapDocumentDto>?> GetPpapDocumentsAsync(string supplierId, CancellationToken ct = default)
        => GetAsync<List<PpapDocumentDto>>($"api/v1/suppliers/suppliers/{supplierId}/ppap", ct);

    /// <summary>创建 PPAP 文档</summary>
    public Task<(bool Ok, PpapDocumentDto? Data, int Status)> CreatePpapDocumentAsync(string supplierId, CreatePpapBody body, CancellationToken ct = default)
        => PostAsync<PpapDocumentDto>($"api/v1/suppliers/suppliers/{supplierId}/ppap", body, ct);

    /// <summary>提交 PPAP 审批</summary>
    public Task<(bool Ok, PpapDocumentDto? Data, int Status)> SubmitPpapAsync(string supplierId, string docId, CancellationToken ct = default)
        => PostAsync<PpapDocumentDto>($"api/v1/suppliers/suppliers/{supplierId}/ppap/{docId}/submit", new { }, ct);

    /// <summary>批准 PPAP</summary>
    public Task<(bool Ok, PpapDocumentDto? Data, int Status)> ApprovePpapAsync(string supplierId, string docId, string approvedBy, CancellationToken ct = default)
        => PostAsync<PpapDocumentDto>($"api/v1/suppliers/suppliers/{supplierId}/ppap/{docId}/approve", new ApprovePpapBody(approvedBy), ct);

    /// <summary>拒绝 PPAP</summary>
    public Task<(bool Ok, PpapDocumentDto? Data, int Status)> RejectPpapAsync(string supplierId, string docId, string reason, CancellationToken ct = default)
        => PostAsync<PpapDocumentDto>($"api/v1/suppliers/suppliers/{supplierId}/ppap/{docId}/reject", new RejectPpapBody(reason), ct);

    /// <summary>查询关键供应商管控设置</summary>
    public Task<List<CriticalSupplierSettingDto>?> GetCriticalSettingsAsync(CancellationToken ct = default)
        => GetAsync<List<CriticalSupplierSettingDto>>("api/v1/suppliers/suppliers/critical-settings", ct);

    /// <summary>创建关键供应商管控设置</summary>
    public Task<(bool Ok, CriticalSupplierSettingDto? Data, int Status)> CreateCriticalSettingAsync(CreateCriticalSettingBody body, CancellationToken ct = default)
        => PostAsync<CriticalSupplierSettingDto>("api/v1/suppliers/suppliers/critical-settings", body, ct);

    // ═══════════════════════════════════════════
    // M09 排程管理 API (T3.10-T3.13)
    // ═══════════════════════════════════════════

    /// <summary>查询排程列表</summary>
    public Task<List<ScheduleDto>?> GetSchedulesAsync(string? equipment = null, string? status = null, string? from = null, string? to = null, CancellationToken ct = default)
    {
        var url = "api/v1/scheduling/plans";
        var query = new List<string>();
        if (equipment is not null) query.Add($"equipment={Uri.EscapeDataString(equipment)}");
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        if (from is not null) query.Add($"from={Uri.EscapeDataString(from)}");
        if (to is not null) query.Add($"to={Uri.EscapeDataString(to)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return GetAsync<List<ScheduleDto>>(url, ct);
    }

    /// <summary>查询甘特图数据</summary>
    public Task<GanttDataDto?> GetGanttDataAsync(string from, string to, CancellationToken ct = default)
        => GetAsync<GanttDataDto>($"api/v1/scheduling/gantt-data?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}", ct);

    /// <summary>查询产能利用率</summary>
    public Task<List<CapacityUtilizationDto>?> GetCapacityAsync(string date, CancellationToken ct = default)
        => GetAsync<List<CapacityUtilizationDto>>($"api/v1/scheduling/capacity?date={Uri.EscapeDataString(date)}", ct);

    /// <summary>开始排程</summary>
    public Task<(bool Ok, ScheduleDto? Data, int Status)> StartScheduleAsync(string id, CancellationToken ct = default)
        => PostAsync<ScheduleDto>($"api/v1/scheduling/plans/{id}/start", new { }, ct);

    /// <summary>完成排程</summary>
    public Task<(bool Ok, ScheduleDto? Data, int Status)> CompleteScheduleAsync(string id, CancellationToken ct = default)
        => PostAsync<ScheduleDto>($"api/v1/scheduling/plans/{id}/complete", new { }, ct);

    /// <summary>取消排程</summary>
    public Task<(bool Ok, ScheduleDto? Data, int Status)> CancelScheduleAsync(string id, string reason, CancellationToken ct = default)
        => PostAsync<ScheduleDto>($"api/v1/scheduling/plans/{id}/cancel", new { Reason = reason }, ct);

    /// <summary>紧急插单</summary>
    public Task<(bool Ok, RushOrderResultDto? Data, int Status)> InsertRushOrderAsync(
        string orderId, string equipmentCode, string rushType,
        double standardMinutes, double changeoverMinutes, string? rushReason,
        CancellationToken ct = default)
        => PostAsync<RushOrderResultDto>("api/v1/scheduling/rush-order",
            new InsertRushOrderBody(orderId, equipmentCode, rushType, standardMinutes, changeoverMinutes, rushReason), ct);

    /// <summary>重新排程（调整时间/设备/换型时间）</summary>
    public Task<(bool Ok, ScheduleDto? Data, int Status)> RescheduleScheduleAsync(
        string id, DateTimeOffset newStartAt, string? newEquipmentCode, double? newChangeoverMinutes,
        CancellationToken ct = default)
        => PostAsync<ScheduleDto>($"api/v1/scheduling/plans/{id}/reschedule",
            new RescheduleBody(newStartAt, newEquipmentCode, newChangeoverMinutes), ct);

    /// <summary>查询产能日历配置</summary>
    public Task<List<CapacityCalendarDto>?> GetCalendarsAsync(CancellationToken ct = default)
        => GetAsync<List<CapacityCalendarDto>>("api/v1/scheduling/calendars", ct);

    /// <summary>创建设备产能日历</summary>
    public Task<(bool Ok, CapacityCalendarDto? Data, int Status)> CreateCalendarAsync(CreateCalendarBody body, CancellationToken ct = default)
        => PostAsync<CapacityCalendarDto>("api/v1/scheduling/calendars", body, ct);

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
