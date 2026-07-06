using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddHydraulicTestResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hydraulic_test_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductSerial = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CycleNumber = table.Column<int>(type: "integer", nullable: false),
                    PressureBuildTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    PressureBuildPass = table.Column<bool>(type: "boolean", nullable: true),
                    HoldPressureBar = table.Column<double>(type: "double precision", nullable: true),
                    HoldPressurePass = table.Column<bool>(type: "boolean", nullable: true),
                    PressureReleaseTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    PressureReleasePass = table.Column<bool>(type: "boolean", nullable: true),
                    LeakRateCcHr = table.Column<double>(type: "double precision", nullable: true),
                    LeakRatePass = table.Column<bool>(type: "boolean", nullable: true),
                    OverallPass = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EquipmentLocked = table.Column<bool>(type: "boolean", nullable: false),
                    UnlockedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UnlockedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    SolenoidTests = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hydraulic_test_results", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_hydraulic_equipment",
                table: "hydraulic_test_results",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_hydraulic_order",
                table: "hydraulic_test_results",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_hydraulic_serial",
                table: "hydraulic_test_results",
                column: "ProductSerial");

            migrationBuilder.CreateIndex(
                name: "idx_hydraulic_status",
                table: "hydraulic_test_results",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hydraulic_test_results");
        }
    }
}
