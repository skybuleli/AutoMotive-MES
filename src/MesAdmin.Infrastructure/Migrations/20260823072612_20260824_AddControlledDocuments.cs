using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260824_AddControlledDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "controlled_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    StationScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_controlled_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FileStoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_docs_type",
                table: "controlled_documents",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_controlled_documents_DocNumber",
                table: "controlled_documents",
                column: "DocNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_docver_document",
                table: "document_versions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "idx_docver_status",
                table: "document_versions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_DocumentId_VersionNo",
                table: "document_versions",
                columns: new[] { "DocumentId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "controlled_documents");

            migrationBuilder.DropTable(
                name: "document_versions");
        }
    }
}
