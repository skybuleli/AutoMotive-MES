using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260707_AddOfflineSyncRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offline_sync_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OperationTimestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ConflictResolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offline_sync_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_offline_created_at",
                table: "offline_sync_records",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_offline_entity_id",
                table: "offline_sync_records",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "idx_offline_entity_type",
                table: "offline_sync_records",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "idx_offline_op_type",
                table: "offline_sync_records",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "idx_offline_status",
                table: "offline_sync_records",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_offline_terminal",
                table: "offline_sync_records",
                column: "TerminalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offline_sync_records");
        }
    }
}
