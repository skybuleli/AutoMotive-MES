using MesAdmin.Domain.Models;
using MesAdmin.Infrastructure.Security;

namespace MesAdmin.Application.Tests;

/// <summary>
/// 用户账号锁定 + PBKDF2 哈希器单元测试（审计日志+用户体系）。
/// </summary>
public class UserAccountLockoutTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();
    private static Ulid NewId() => Ulid.NewUlid();

    // ── Hasher ──

    [Fact]
    public void Hash_And_Verify_ShouldRoundTrip()
    {
        var hash = Hasher.Hash("Mes@2026");
        Assert.True(Hasher.Verify("Mes@2026", hash));
        Assert.False(Hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_ShouldProduceUniqueSalt_EachInvocation()
    {
        var h1 = Hasher.Hash("Mes@2026");
        var h2 = Hasher.Hash("Mes@2026");
        Assert.NotEqual(h1, h2);
        Assert.True(Hasher.Verify("Mes@2026", h1));
        Assert.True(Hasher.Verify("Mes@2026", h2));
    }

    [Fact]
    public void Verify_ShouldReject_MalformedHash()
    {
        Assert.False(Hasher.Verify("Mes@2026", ""));
        Assert.False(Hasher.Verify("Mes@2026", "not-a-hash"));
        Assert.False(Hasher.Verify("Mes@2026", "pbkdf2-sha256$0$abc$def"));
        Assert.False(Hasher.Verify("", Hasher.Hash("Mes@2026")));
    }

    [Fact]
    public void NeedsRehash_ShouldReturnTrue_ForOldIterations()
    {
        var oldHash = $"pbkdf2-sha256$1000${Convert.ToBase64String(new byte[16])}${Convert.ToBase64String(new byte[32])}";
        Assert.True(Hasher.NeedsRehash(oldHash));
    }

    // ── 锁定 ──

    [Fact]
    public void RegisterLoginFailure_ShouldTriggerLock_OnThreshold()
    {
        var user = UserAccount.Create(NewId(), "locktest", "锁测试", Hasher.Hash("Mes@2026"), ["QualityEngineer"]);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < UserAccount.MaxFailedLoginAttempts - 1; i++)
            Assert.False(user.RegisterLoginFailure(now));
        Assert.True(user.RegisterLoginFailure(now));
        Assert.True(user.IsLockedOut);
    }

    [Fact]
    public void RegisterLoginSuccess_ShouldClearLock()
    {
        var user = UserAccount.Create(NewId(), "oktest", "正常测试", Hasher.Hash("Mes@2026"), ["QualityEngineer"]);
        var now = DateTimeOffset.UtcNow;
        user.RegisterLoginFailure(now);
        user.RegisterLoginFailure(now);
        user.RegisterLoginSuccess(now.AddMinutes(1));
        Assert.False(user.IsLockedOut);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public void ResetPassword_ShouldClearFailureState()
    {
        var user = UserAccount.Create(NewId(), "pwdtest", "改密测试", Hasher.Hash("Mes@2026"), ["QualityEngineer"]);
        var now = DateTimeOffset.UtcNow;
        user.RegisterLoginFailure(now);
        user.RegisterLoginFailure(now);
        user.ResetPassword(Hasher.Hash("New@2027"), now);
        Assert.False(user.IsLockedOut);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.True(Hasher.Verify("New@2027", user.PasswordHash));
        Assert.False(Hasher.Verify("Mes@2026", user.PasswordHash));
    }
}
