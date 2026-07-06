using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddAndonModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "andon_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Station = table.Column<int>(type: "integer", nullable: false),
                    AlarmType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessValue = table.Column<double>(type: "double precision", nullable: false),
                    ProcessTag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    UpperLimit = table.Column<double>(type: "double precision", nullable: true),
                    LowerLimit = table.Column<double>(type: "double precision", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    NonConformanceReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    EscalationLevel = table.Column<int>(type: "integer", nullable: false),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CloseRemarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_andon_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_andon_equipment",
                table: "andon_events",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_andon_occurred",
                table: "andon_events",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "idx_andon_status",
                table: "andon_events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_andon_events_EventNumber",
                table: "andon_events",
                column: "EventNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "andon_events");
        }
    }
}
