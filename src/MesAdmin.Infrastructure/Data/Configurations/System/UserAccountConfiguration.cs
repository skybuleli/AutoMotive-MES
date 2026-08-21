using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserAccountEntity = MesAdmin.Domain.Models.UserAccount;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>用户账号实体配置。</summary>
public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccountEntity>
{
    public void Configure(EntityTypeBuilder<UserAccountEntity> builder)
    {
        builder.ToTable("user_accounts");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(64).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(64).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        // 角色数组 → jsonb；EF Core 10 原生支持 string[] 到 PG jsonb 的映射
        builder.Property(u => u.Roles).HasColumnType("jsonb").IsRequired();
        builder.Property(u => u.LockoutUntil).HasColumnType("timestamptz");
        builder.Property(u => u.LastLoginAt).HasColumnType("timestamptz");
        builder.Property(u => u.CreatedAt).HasColumnType("timestamptz");
        builder.Property(u => u.UpdatedAt).HasColumnType("timestamptz");
    }
}
