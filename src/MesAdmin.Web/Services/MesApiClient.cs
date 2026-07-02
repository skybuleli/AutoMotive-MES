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
