using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260707_AddSchedulingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capacity_calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EquipmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Station = table.Column<int>(type: "integer", nullable: false),
                    ShiftTemplate = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StandardChangeoverMinutes = table.Column<double>(type: "double precision", nullable: false),
                    CrossProductChangeoverMinutes = table.Column<double>(type: "double precision", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_capacity_calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "production_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Station = table.Column<int>(type: "integer", nullable: false),
                    Shift = table.Column<int>(type: "integer", nullable: false),
                    ScheduleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    StandardMinutes = table.Column<double>(type: "double precision", nullable: false),
                    ChangeoverMinutes = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RushType = table.Column<int>(type: "integer", nullable: false),
                    RushReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_schedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_capacity_calendars_EquipmentCode",
                table: "capacity_calendars",
                column: "EquipmentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_schedule_date",
                table: "production_schedules",
                column: "ScheduleDate");

            migrationBuilder.CreateIndex(
                name: "idx_schedule_equipment",
                table: "production_schedules",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_schedule_order",
                table: "production_schedules",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_schedule_status",
                table: "production_schedules",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capacity_calendars");

            migrationBuilder.DropTable(
                name: "production_schedules");
        }
    }
}
