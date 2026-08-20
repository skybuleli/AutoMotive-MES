using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace MesAdmin.Web.Services;

/// <summary>
/// Web 端认证服务：封装 API 登录调用，Token 存入 ProtectedLocalStorage。
/// </summary>
public class AuthService(
    IHttpClientFactory httpFactory,
    ProtectedLocalStorage localStorage,
    AuthenticationStateProvider authStateProvider,
    IJSRuntime js)
{
    private const string TokenKey = "mes_auth_token";
    private const string CookieName = "mes_auth_cookie";

    public async Task<bool> LoginAsync(string username, string password)
    {
        var client = httpFactory.CreateClient("MesApi");
        var response = await client.PostAsJsonAsync("api/auth/login", new { Username = username, Password = password });

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<LoginResult>();
        if (result?.Token is null)
            return false;

        await localStorage.SetAsync(TokenKey, result.Token);
        ((MesAuthenticationStateProvider)authStateProvider).MarkAuthenticated(result.Token);

        // 种下服务端可读 cookie：登录后 NavigateTo 触发整页加载时，服务端 [Authorize]
        // 校验依赖它（浏览器侧 JWT 在 ProtectedLocalStorage，服务端读不到，需在 OnMessageReceived 兜底取 cookie）。
        // JWT 本就存于 localStorage，cookie 非 httpOnly 不额外扩大 XSS 面。
        await js.InvokeVoidAsync("mesAuth.setCookie", CookieName, result.Token);
        return true;
    }

    public async Task LogoutAsync()
    {
        await localStorage.DeleteAsync(TokenKey);
        ((MesAuthenticationStateProvider)authStateProvider).MarkLoggedOut();
        await js.InvokeVoidAsync("mesAuth.clearCookie", CookieName);
    }

    public async Task<string?> GetTokenAsync()
    {
        var result = await localStorage.GetAsync<string>(TokenKey);
        return result.Success ? result.Value : null;
    }

    private sealed record LoginResult(string Token, string User, string[] Roles);
}
