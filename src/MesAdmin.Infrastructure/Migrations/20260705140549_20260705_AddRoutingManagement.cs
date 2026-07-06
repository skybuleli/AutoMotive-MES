using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddRoutingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "routings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EcoNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EcoStatus = table.Column<int>(type: "integer", nullable: false),
                    OperationCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ChangeDescription = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    EffectiveDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Operations = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_routing_active",
                table: "routings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "idx_routing_eco_status",
                table: "routings",
                column: "EcoStatus");

            migrationBuilder.CreateIndex(
                name: "idx_routing_product",
                table: "routings",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "ux_routing_product_version",
                table: "routings",
                columns: new[] { "ProductCode", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "routings");
        }
    }
}
