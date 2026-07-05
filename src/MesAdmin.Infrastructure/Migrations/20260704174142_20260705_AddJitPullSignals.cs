using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddJitPullSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jit_pull_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ShortageQuantity = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    TargetStation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    DeliveredBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jit_pull_signals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_jit_pull_material",
                table: "jit_pull_signals",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_jit_pull_order",
                table: "jit_pull_signals",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_jit_pull_status",
                table: "jit_pull_signals",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jit_pull_signals");
        }
    }
}
