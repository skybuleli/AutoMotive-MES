using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_spc_char_collected",
                table: "spc_samples",
                columns: new[] { "CharacteristicCode", "CollectedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_spc_alert_char_created",
                table: "spc_rule_alerts",
                columns: new[] { "CharacteristicCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_quality_stage_created",
                table: "quality_records",
                columns: new[] { "Stage", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_ncr_product_created",
                table: "non_conformance_reports",
                columns: new[] { "ProductCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_mt_order_created_at",
                table: "maintenance_work_orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_eightd_product_created",
                table: "eight_d_reports",
                columns: new[] { "ProductCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_andon_created_at",
                table: "andon_events",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_spc_char_collected",
                table: "spc_samples");

            migrationBuilder.DropIndex(
                name: "idx_spc_alert_char_created",
                table: "spc_rule_alerts");

            migrationBuilder.DropIndex(
                name: "idx_quality_stage_created",
                table: "quality_records");

            migrationBuilder.DropIndex(
                name: "idx_ncr_product_created",
                table: "non_conformance_reports");

            migrationBuilder.DropIndex(
                name: "idx_mt_order_created_at",
                table: "maintenance_work_orders");

            migrationBuilder.DropIndex(
                name: "idx_eightd_product_created",
                table: "eight_d_reports");

            migrationBuilder.DropIndex(
                name: "idx_andon_created_at",
                table: "andon_events");
        }
    }
}
