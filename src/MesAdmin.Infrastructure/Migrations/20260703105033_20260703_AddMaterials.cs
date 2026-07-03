using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260703_AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "material_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceivedQuantity = table.Column<double>(type: "double precision", nullable: false),
                    RemainingQuantity = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProductionDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "material_bindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductSerial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    PokaYokePassed = table.Column<bool>(type: "boolean", nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BoundAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_bindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_material_code",
                table: "material_batches",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_material_status",
                table: "material_batches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_material_batches_BatchNumber",
                table: "material_batches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_binding_batch",
                table: "material_bindings",
                column: "MaterialBatchId");

            migrationBuilder.CreateIndex(
                name: "idx_binding_order",
                table: "material_bindings",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_binding_serial",
                table: "material_bindings",
                column: "ProductSerial");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "material_batches");

            migrationBuilder.DropTable(
                name: "material_bindings");
        }
    }
}
