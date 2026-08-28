using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260822_AddGaugeCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calibration_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GaugeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibratedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    CertificateNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NextDueAfter = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gauges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GaugeNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    RangeSpec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResolutionSpec = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccuracyClass = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalibrationCycleDays = table.Column<int>(type: "integer", nullable: false),
                    LastCalibratedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StorageLocation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gauges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_cal_records_gauge",
                table: "calibration_records",
                column: "GaugeId");

            migrationBuilder.CreateIndex(
                name: "idx_gauges_next_due",
                table: "gauges",
                column: "NextDueAt");

            migrationBuilder.CreateIndex(
                name: "idx_gauges_status",
                table: "gauges",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_gauges_GaugeNumber",
                table: "gauges",
                column: "GaugeNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calibration_records");

            migrationBuilder.DropTable(
                name: "gauges");
        }
    }
}
