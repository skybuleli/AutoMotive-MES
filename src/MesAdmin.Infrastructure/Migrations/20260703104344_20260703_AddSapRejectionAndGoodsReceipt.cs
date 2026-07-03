using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260703_AddSapRejectionAndGoodsReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "goods_receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TraceabilityLabelCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SapSynced = table.Column<bool>(type: "boolean", nullable: false),
                    SapSyncedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goods_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sap_rejection_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BomVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoutingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlannedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    WritebackStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WritebackError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    WritebackAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sap_rejection_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_OrderId",
                table: "goods_receipts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goods_receipts_TraceabilityLabelCode",
                table: "goods_receipts",
                column: "TraceabilityLabelCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_sap_rejection_external",
                table: "sap_rejection_records",
                column: "ExternalOrderNumber");

            migrationBuilder.CreateIndex(
                name: "idx_sap_rejection_writeback",
                table: "sap_rejection_records",
                column: "WritebackStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "goods_receipts");

            migrationBuilder.DropTable(
                name: "sap_rejection_records");
        }
    }
}
