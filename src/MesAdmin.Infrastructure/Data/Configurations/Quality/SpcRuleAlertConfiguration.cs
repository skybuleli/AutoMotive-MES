using MesAdmin.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MesAdmin.Infrastructure.Data.Configurations.Quality;

/// <summary>SPC 规则告警实体配置。</summary>
public sealed class SpcRuleAlertConfiguration : IEntityTypeConfiguration<SpcRuleAlert>
{
    public void Configure(EntityTypeBuilder<SpcRuleAlert> builder)
    {
        builder.ToTable("spc_rule_alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CharacteristicCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(a => a.CharacteristicCode).HasDatabaseName("idx_spc_alert_char");
        builder.Property(a => a.RuleType).HasConversion<int>();
        builder.Property(a => a.AlertLevel).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.EquipmentCode).HasMaxLength(32);
        builder.Property(a => a.Description).HasMaxLength(256).IsRequired();
        builder.Property(a => a.AcknowledgedBy).HasMaxLength(32);
        builder.Property(a => a.ActionTaken).HasMaxLength(512);
        builder.Property(a => a.AcknowledgedAt).HasColumnType("timestamptz");
        builder.Property(a => a.CreatedAt).HasColumnType("timestamptz");
        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_spc_alert_created");
        builder.HasIndex(a => new { a.CharacteristicCode, a.CreatedAt }).HasDatabaseName("idx_spc_alert_char_created");
    }
}
