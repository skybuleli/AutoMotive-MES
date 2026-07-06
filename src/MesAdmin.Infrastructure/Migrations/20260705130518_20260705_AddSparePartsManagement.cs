using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddSparePartsManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SparePartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "spare_part_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SparePartId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaintenanceWorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    UnitPrice = table.Column<double>(type: "double precision", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spare_part_usages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "spare_parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Specification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentQuantity = table.Column<double>(type: "double precision", nullable: false),
                    SafetyStock = table.Column<double>(type: "double precision", nullable: false),
                    MinimumStock = table.Column<double>(type: "double precision", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spare_parts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_purchase_request_part",
                table: "purchase_requests",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "idx_purchase_request_status",
                table: "purchase_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_requests_RequestNumber",
                table: "purchase_requests",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_spare_usage_composite",
                table: "spare_part_usages",
                columns: new[] { "SparePartId", "MaintenanceWorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "idx_spare_usage_order",
                table: "spare_part_usages",
                column: "MaintenanceWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "idx_spare_usage_part",
                table: "spare_part_usages",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "idx_spare_part_equipment",
                table: "spare_parts",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "IX_spare_parts_MaterialCode",
                table: "spare_parts",
                column: "MaterialCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_requests");

            migrationBuilder.DropTable(
                name: "spare_part_usages");

            migrationBuilder.DropTable(
                name: "spare_parts");
        }
    }
}
