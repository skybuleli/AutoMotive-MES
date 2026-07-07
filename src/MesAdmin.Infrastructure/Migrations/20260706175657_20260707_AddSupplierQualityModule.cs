using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260707_AddSupplierQualityModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "critical_supplier_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ControlLevel = table.Column<int>(type: "integer", nullable: false),
                    RequiresFullInspection = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresOnSiteAudit = table.Column<bool>(type: "boolean", nullable: false),
                    AuditIntervalMonths = table.Column<int>(type: "integer", nullable: false),
                    RequiresSpcDataSubmission = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresComplianceReport = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_critical_supplier_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ppap_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterialName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PpapLevel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ExpiryDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ppap_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_score_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Period = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IncomingQualityScore = table.Column<double>(type: "double precision", nullable: false),
                    IncomingQualityData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OnTimeDeliveryScore = table.Column<double>(type: "double precision", nullable: false),
                    OnTimeDeliveryData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EightDResponseScore = table.Column<double>(type: "double precision", nullable: false),
                    EightDResponseData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PpapPassRateScore = table.Column<double>(type: "double precision", nullable: false),
                    PpapPassRateData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PriceCompetitivenessScore = table.Column<double>(type: "double precision", nullable: false),
                    PriceCompetitivenessData = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    WeightedTotal = table.Column<double>(type: "double precision", nullable: false),
                    EvaluatedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_score_cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreditCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContactPerson = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MaterialCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaterialCodes = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    IsCritical = table.Column<bool>(type: "boolean", nullable: false),
                    LatestScore = table.Column<double>(type: "double precision", nullable: false),
                    LatestScoreAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    IsoCertification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsoExpiryDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_critical_supplier_settings_MaterialCode",
                table: "critical_supplier_settings",
                column: "MaterialCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ppap_material",
                table: "ppap_documents",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "idx_ppap_status",
                table: "ppap_documents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_ppap_supplier",
                table: "ppap_documents",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "idx_scorecard_period",
                table: "supplier_score_cards",
                column: "Period");

            migrationBuilder.CreateIndex(
                name: "idx_scorecard_supplier",
                table: "supplier_score_cards",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "ux_scorecard_supplier_period",
                table: "supplier_score_cards",
                columns: new[] { "SupplierId", "Period" });

            migrationBuilder.CreateIndex(
                name: "idx_supplier_category",
                table: "suppliers",
                column: "MaterialCategory");

            migrationBuilder.CreateIndex(
                name: "idx_supplier_tier",
                table: "suppliers",
                column: "Tier");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_SupplierCode",
                table: "suppliers",
                column: "SupplierCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "critical_supplier_settings");

            migrationBuilder.DropTable(
                name: "ppap_documents");

            migrationBuilder.DropTable(
                name: "supplier_score_cards");

            migrationBuilder.DropTable(
                name: "suppliers");
        }
    }
}
