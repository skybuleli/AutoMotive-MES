using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MesAdmin.Application.Security;

namespace MesAdmin.Infrastructure.Security;

/// <summary>
/// JWT 令牌服务实现。
/// 生成包含用户 ID、用户名、角色 Claims 的 Bearer 令牌。
/// </summary>
public class JwtTokenService(IConfiguration configuration) : ITokenService
{
    private readonly SymmetricSecurityKey _key = new(
        Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret 未配置")));

    /// <summary>构建 TokenValidationParameters（复用同一份配置）</summary>
    public TokenValidationParameters BuildValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "AutoMES",
        ValidAudience = "AutoMES-Clients",
        IssuerSigningKey = _key,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    /// <inheritdoc />
    public string GenerateToken(string userId, string userName, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, userName),
            new(JwtRegisteredClaimNames.Jti, Ulid.NewUlid().ToString()),
            new("user_id", userId)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(8);

        var token = new JwtSecurityToken(
            issuer: "AutoMES",
            audience: "AutoMES-Clients",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, BuildValidationParameters(), out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// JWT 认证 DI 扩展。
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// 注册 JWT Bearer 认证 + ITokenService。
    /// </summary>
    public static IServiceCollection AddMesJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret 未配置");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "AutoMES",
                    ValidAudience = "AutoMES-Clients",
                    IssuerSigningKey = key,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        services.AddAuthorization();
        return services;
    }
}
