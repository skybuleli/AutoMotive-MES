using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.System;

/// <summary>受控文档主表配置（S03）。</summary>
public sealed class ControlledDocumentConfiguration : IEntityTypeConfiguration<ControlledDocument>
{
    public void Configure(EntityTypeBuilder<ControlledDocument> builder)
    {
        builder.ToTable("controlled_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(d => d.DocNumber).IsUnique();
        builder.Property(d => d.Title).HasMaxLength(128).IsRequired();
        builder.Property(d => d.Type).HasConversion<int>();
        builder.Property(d => d.StationScope).HasMaxLength(32);
        // CurrentVersionId nullable FK-like, no constraint to allow draft-only docs
        builder.Property(d => d.CreatedAt).HasColumnType("timestamptz");
        builder.Property(d => d.UpdatedAt).HasColumnType("timestamptz");
        builder.HasIndex(d => d.Type).HasDatabaseName("idx_docs_type");
    }
}
