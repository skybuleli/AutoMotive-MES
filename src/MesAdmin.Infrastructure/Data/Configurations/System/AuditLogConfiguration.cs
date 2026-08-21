using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AuditLogEntity = MesAdmin.Domain.Models.AuditLog;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>操作审计日志实体配置。</summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntity>
{
    public void Configure(EntityTypeBuilder<AuditLogEntity> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Username).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Module).HasMaxLength(64).IsRequired();
        // 查询页默认时间倒序 + 按用户/模块筛选，全部建索引
        builder.HasIndex(a => a.Timestamp).HasDatabaseName("idx_audit_timestamp");
        builder.HasIndex(a => a.Username).HasDatabaseName("idx_audit_username");
        builder.HasIndex(a => new { a.Module, a.Action }).HasDatabaseName("idx_audit_module_action");
        builder.Property(a => a.Summary).HasMaxLength(512);
        builder.Property(a => a.RemoteIp).HasMaxLength(64);
    }
}
