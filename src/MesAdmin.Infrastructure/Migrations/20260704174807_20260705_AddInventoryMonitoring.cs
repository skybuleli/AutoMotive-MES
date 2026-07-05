using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddInventoryMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CurrentQuantity = table.Column<double>(type: "double precision", nullable: false),
                    SafetyStock = table.Column<double>(type: "double precision", nullable: false),
                    MinimumStock = table.Column<double>(type: "double precision", nullable: false),
                    AlertLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    JitPullSignalId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "material_inventory_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SafetyStock = table.Column<double>(type: "double precision", nullable: false),
                    MinimumStock = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_inventory_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_inv_alert_level",
                table: "inventory_alerts",
                column: "AlertLevel");

            migrationBuilder.CreateIndex(
                name: "idx_inv_alert_material",
                table: "inventory_alerts",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_inv_setting_material",
                table: "material_inventory_settings",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_inv_setting_station",
                table: "material_inventory_settings",
                column: "StationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_alerts");

            migrationBuilder.DropTable(
                name: "material_inventory_settings");
        }
    }
}
