using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MesAdmin.Web.Services;

/// <summary>
/// Web 端认证服务：封装 API 登录调用，Token 存入 ProtectedLocalStorage。
/// </summary>
public class AuthService(
    IHttpClientFactory httpFactory,
    ProtectedLocalStorage localStorage,
    AuthenticationStateProvider authStateProvider)
{
    private const string TokenKey = "mes_auth_token";

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
        ((MesAuthenticationStateProvider)authStateProvider).NotifyAuthenticationStateChanged();
        return true;
    }

    public async Task LogoutAsync()
    {
        await localStorage.DeleteAsync(TokenKey);
        ((MesAuthenticationStateProvider)authStateProvider).NotifyAuthenticationStateChanged();
    }

    public async Task<string?> GetTokenAsync()
    {
        var result = await localStorage.GetAsync<string>(TokenKey);
        return result.Success ? result.Value : null;
    }

    private sealed record LoginResult(string Token, string User, string[] Roles);
}
