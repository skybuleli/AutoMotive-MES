using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>受控文档版本配置（S03）。</summary>
public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.DocumentId).IsRequired();
        builder.HasIndex(v => v.DocumentId).HasDatabaseName("idx_docver_document");
        // 同一文档下版本号唯一，避免 v1.0 重复
        builder.HasIndex(v => new { v.DocumentId, v.VersionNo }).IsUnique();
        builder.HasIndex(v => v.Status).HasDatabaseName("idx_docver_status");
        builder.Property(v => v.VersionNo).HasMaxLength(16).IsRequired();
        builder.Property(v => v.FileStoragePath).HasMaxLength(512).IsRequired();
        builder.Property(v => v.FileName).HasMaxLength(256).IsRequired();
        builder.Property(v => v.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(v => v.Status).HasConversion<int>();
        builder.Property(v => v.SubmittedBy).HasMaxLength(32);
        builder.Property(v => v.ApprovedBy).HasMaxLength(32);
        builder.Property(v => v.Remarks).HasMaxLength(512);
        builder.Property(v => v.CreatedAt).HasColumnType("timestamptz");
        builder.Property(v => v.UpdatedAt).HasColumnType("timestamptz");
        builder.Property(v => v.SubmittedAt).HasColumnType("timestamptz");
        builder.Property(v => v.ApprovedAt).HasColumnType("timestamptz");
        builder.Property(v => v.EffectiveAt).HasColumnType("timestamptz");
        builder.Property(v => v.SupersededAt).HasColumnType("timestamptz");
    }
}
