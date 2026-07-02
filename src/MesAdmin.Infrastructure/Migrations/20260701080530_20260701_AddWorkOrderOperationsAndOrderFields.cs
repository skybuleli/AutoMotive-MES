using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260701_AddWorkOrderOperationsAndOrderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualEndAt",
                table: "production_orders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActualStartAt",
                table: "production_orders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "production_orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderNumber",
                table: "production_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LineId",
                table: "production_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedEndAt",
                table: "production_orders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlannedStartAt",
                table: "production_orders",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Shift",
                table: "production_orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSystem",
                table: "production_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkCenterId",
                table: "production_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "work_order_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Station = table.Column<int>(type: "integer", nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EquipmentId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Parameters = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_operations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_order_operations_OrderId_Sequence",
                table: "work_order_operations",
                columns: new[] { "OrderId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_operations");

            migrationBuilder.DropColumn(
                name: "ActualEndAt",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "ActualStartAt",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderNumber",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "LineId",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "PlannedEndAt",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "PlannedStartAt",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "Shift",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "SourceSystem",
                table: "production_orders");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                table: "production_orders");
        }
    }
}
