using System.Security.Claims;

namespace MesAdmin.Application.Security;

/// <summary>
/// 认证令牌服务接口。
/// Infrastructure 层提供 JWT 实现。
/// </summary>
public interface ITokenService
{
    /// <summary>为指定用户和角色生成 JWT 令牌</summary>
    /// <param name="userId">用户标识</param>
    /// <param name="userName">用户名</param>
    /// <param name="roles">角色列表</param>
    /// <returns>JWT 令牌字符串</returns>
    string GenerateToken(string userId, string userName, IEnumerable<string> roles);

    /// <summary>验证令牌并返回 ClaimsPrincipal</summary>
    ClaimsPrincipal? ValidateToken(string token);
}
