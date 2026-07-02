using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace MesAdmin.Web.Services;

/// <summary>
/// 自定义 AuthenticationStateProvider，从 ProtectedLocalStorage 读取 JWT。
/// SSR 预渲染阶段 ProtectedLocalStorage 不可用，捕获异常返回匿名状态。
/// </summary>
public class MesAuthenticationStateProvider(ProtectedLocalStorage localStorage) : AuthenticationStateProvider
{
    private const string TokenKey = "mes_auth_token";
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var tokenResult = await localStorage.GetAsync<string>(TokenKey);
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Value))
                return new AuthenticationState(Anonymous);

            var identity = ParseToken(tokenResult.Value);
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (InvalidOperationException)
        {
            // SSR 预渲染阶段 ProtectedLocalStorage 不可用
            return new AuthenticationState(Anonymous);
        }
    }

    public void NotifyAuthenticationStateChanged()
        => base.NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static ClaimsIdentity ParseToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return new ClaimsIdentity();

            var payload = Base64UrlDecode(parts[1]);
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            var claims = new List<Claim>();

            if (doc.RootElement.TryGetProperty("sub", out var sub))
                claims.Add(new Claim(ClaimTypes.NameIdentifier, sub.GetString()!));
            if (doc.RootElement.TryGetProperty("unique_name", out var name))
                claims.Add(new Claim(ClaimTypes.Name, name.GetString()!));
            if (doc.RootElement.TryGetProperty("role", out var role))
                claims.Add(new Claim(ClaimTypes.Role, role.GetString()!));
            if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var r in roles.EnumerateArray())
                    claims.Add(new Claim(ClaimTypes.Role, r.GetString()!));
            }

            return new ClaimsIdentity(claims, "jwt");
        }
        catch
        {
            return new ClaimsIdentity();
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.PadRight(input.Length + (4 - input.Length % 4) % 4, '=');
        var base64 = padded.Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(base64);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
