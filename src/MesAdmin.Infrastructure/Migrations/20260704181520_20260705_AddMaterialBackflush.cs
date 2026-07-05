using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddMaterialBackflush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumption_variance_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StandardQuantity = table.Column<double>(type: "double precision", nullable: false),
                    ConsumedQuantity = table.Column<double>(type: "double precision", nullable: false),
                    VarianceQuantity = table.Column<double>(type: "double precision", nullable: false),
                    VariancePercent = table.Column<double>(type: "double precision", nullable: false),
                    Direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumption_variance_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "material_consumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StandardQuantity = table.Column<double>(type: "double precision", nullable: false),
                    ActualBoundQuantity = table.Column<double>(type: "double precision", nullable: false),
                    ConsumedQuantity = table.Column<double>(type: "double precision", nullable: false),
                    VarianceQuantity = table.Column<double>(type: "double precision", nullable: false),
                    VariancePercent = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_consumptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sap_inventory_sync_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MovementType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SapSynced = table.Column<bool>(type: "boolean", nullable: false),
                    SapDocumentNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SyncError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sap_inventory_sync_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_variance_order",
                table: "consumption_variance_reports",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_consumption_material",
                table: "material_consumptions",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_consumption_order",
                table: "material_consumptions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_sap_inv_sync_material",
                table: "sap_inventory_sync_records",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_sap_inv_sync_order",
                table: "sap_inventory_sync_records",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_sap_inv_sync_status",
                table: "sap_inventory_sync_records",
                column: "SapSynced");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumption_variance_reports");

            migrationBuilder.DropTable(
                name: "material_consumptions");

            migrationBuilder.DropTable(
                name: "sap_inventory_sync_records");
        }
    }
}
