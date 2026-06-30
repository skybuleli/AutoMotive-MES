using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "production_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    RoutingId = table.Column<Guid>(type: "uuid", nullable: false),
                    BomVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    QualifiedQuantity = table.Column<int>(type: "integer", nullable: false),
                    DefectiveQuantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "traceability_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    VinOrSerial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentBatch = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaterialBatch = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousHash = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_traceability_links", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_orders_status",
                table: "production_orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_production_orders_OrderNumber",
                table: "production_orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_trace_component",
                table: "traceability_links",
                column: "ComponentBatch");

            migrationBuilder.CreateIndex(
                name: "idx_trace_material",
                table: "traceability_links",
                column: "MaterialBatch");

            migrationBuilder.CreateIndex(
                name: "idx_trace_vin",
                table: "traceability_links",
                column: "VinOrSerial");

            migrationBuilder.CreateIndex(
                name: "IX_traceability_links_VinOrSerial_Level",
                table: "traceability_links",
                columns: new[] { "VinOrSerial", "Level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "production_orders");

            migrationBuilder.DropTable(
                name: "traceability_links");
        }
    }
}
