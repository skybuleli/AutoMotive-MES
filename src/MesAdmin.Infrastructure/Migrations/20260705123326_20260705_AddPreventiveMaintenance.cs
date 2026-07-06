using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddPreventiveMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EquipmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaintenanceType = table.Column<int>(type: "integer", nullable: false),
                    ThresholdValue = table.Column<double>(type: "double precision", nullable: false),
                    TaskDescription = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkContent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTriggeredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LastTriggeredCycleCount = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_work_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaintenancePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EquipmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaintenanceType = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    TriggerValue = table.Column<double>(type: "double precision", nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CompletionRemarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_work_orders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_mt_plan_equipment",
                table: "maintenance_plans",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_mt_order_equipment",
                table: "maintenance_work_orders",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_mt_order_plan",
                table: "maintenance_work_orders",
                column: "MaintenancePlanId");

            migrationBuilder.CreateIndex(
                name: "idx_mt_order_status",
                table: "maintenance_work_orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_orders_OrderNumber",
                table: "maintenance_work_orders",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_plans");

            migrationBuilder.DropTable(
                name: "maintenance_work_orders");
        }
    }
}
