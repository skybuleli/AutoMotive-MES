using System.Security.Cryptography;

namespace MesAdmin.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 密码哈希器。
/// 存储格式：pbkdf2-sha256$&lt;iterations&gt;$&lt;base64盐&gt;$&lt;base64哈希&gt;
/// 算法标识写入格式串——未来提升迭代次数时，旧哈希仍可验证并可在登录时透明重哈希。
/// </summary>
public sealed class Pbkdf2PasswordHasher
{
    /// <summary>当前迭代次数（OWASP 2023 建议 ≥60万 for SHA512 / ≥21万 for SHA256；取 21 万平衡演示环境性能）</summary>
    public const int Iterations = 210_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string FormatPrefix = "pbkdf2-sha256";

    /// <summary>生成密码哈希（每次调用产生随机盐）。</summary>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{FormatPrefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>验证密码；格式不识别的哈希一律返回 false（fail-closed）。</summary>
    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != FormatPrefix) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        if (!TryDecodeBase64(parts[2], out var salt)) return false;
        if (!TryDecodeBase64(parts[3], out var expected)) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>存储哈希的迭代次数低于当前配置时返回 true（提示透明重哈希）。</summary>
    public bool NeedsRehash(string storedHash)
    {
        var parts = storedHash.Split('$');
        return parts.Length == 4
            && parts[0] == FormatPrefix
            && int.TryParse(parts[1], out var iterations)
            && iterations < Iterations;
    }

    private static bool TryDecodeBase64(string text, out byte[] buffer)
    {
        try
        {
            buffer = Convert.FromBase64String(text);
            return buffer.Length > 0;
        }
        catch (FormatException)
        {
            buffer = [];
            return false;
        }
    }
}
