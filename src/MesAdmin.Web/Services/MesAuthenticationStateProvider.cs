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
    private const string JwtRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private ClaimsPrincipal? _currentUser;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_currentUser is not null)
            return new AuthenticationState(_currentUser);

        try
        {
            var tokenResult = await localStorage.GetAsync<string>(TokenKey);
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Value))
                return new AuthenticationState(Anonymous);

            _currentUser = new ClaimsPrincipal(ParseToken(tokenResult.Value));
            return new AuthenticationState(_currentUser);
        }
        catch (InvalidOperationException)
        {
            // SSR 预渲染阶段 ProtectedLocalStorage 不可用
            return new AuthenticationState(Anonymous);
        }
    }

    public void MarkAuthenticated(string token)
    {
        _currentUser = new ClaimsPrincipal(ParseToken(token));
        base.NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
    }

    public void MarkLoggedOut()
    {
        _currentUser = Anonymous;
        base.NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));
    }

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
            if (doc.RootElement.TryGetProperty(JwtRoleClaim, out var mappedRole))
                claims.Add(new Claim(ClaimTypes.Role, mappedRole.GetString()!));
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
