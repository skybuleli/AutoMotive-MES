using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260705_AddSpcQualityModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eight_d_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NonConformanceReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    NcrNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TeamLeader = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TeamMembers = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProblemDescription = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ContainmentAction = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ContainmentDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    RootCauseAnalysis = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RootCause = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CorrectiveActionOwner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CorrectiveActionDueDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    VerificationMethod = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VerificationResult = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VerificationDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    PreventiveAction = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eight_d_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inspection_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Station = table.Column<int>(type: "integer", nullable: true),
                    SamplingFrequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    AqlValue = table.Column<double>(type: "double precision", nullable: true),
                    InspectionLevel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AcceptNumber = table.Column<int>(type: "integer", nullable: false),
                    RejectNumber = table.Column<int>(type: "integer", nullable: false),
                    EnableSpcChart = table.Column<bool>(type: "boolean", nullable: false),
                    SpcSubgroupSize = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Characteristics = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "non_conformance_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NcrNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QualityRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DiscoveredAt = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DefectQuantity = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResponsibleDept = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DiscoveredBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewerId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReviewComments = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DispositionDeadline = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    EightDReportId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseRemarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_conformance_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupplierCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InspectionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionPlanName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AqlScheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    AcceptNumber = table.Column<int>(type: "integer", nullable: false),
                    RejectNumber = table.Column<int>(type: "integer", nullable: false),
                    InspectorId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Verdict = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DefectCount = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    Characteristics = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "spc_rule_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacteristicCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleType = table.Column<int>(type: "integer", nullable: false),
                    AlertLevel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TriggerSubgroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ActionTaken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spc_rule_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "spc_samples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacteristicCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EquipmentCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SubgroupIndex = table.Column<int>(type: "integer", nullable: false),
                    SubgroupSize = table.Column<int>(type: "integer", nullable: false),
                    Values = table.Column<string>(type: "jsonb", nullable: false),
                    Mean = table.Column<double>(type: "double precision", nullable: false),
                    Range = table.Column<double>(type: "double precision", nullable: false),
                    StdDev = table.Column<double>(type: "double precision", nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    Source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spc_samples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_eight_d_reports_ReportNumber",
                table: "eight_d_reports",
                column: "ReportNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_inspection_plan_product",
                table: "inspection_plans",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_plans_PlanName_Version",
                table: "inspection_plans",
                columns: new[] { "PlanName", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ncr_batch",
                table: "non_conformance_reports",
                column: "BatchNumber");

            migrationBuilder.CreateIndex(
                name: "idx_ncr_order",
                table: "non_conformance_reports",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_ncr_product",
                table: "non_conformance_reports",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "idx_ncr_status",
                table: "non_conformance_reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformance_reports_NcrNumber",
                table: "non_conformance_reports",
                column: "NcrNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_quality_batch",
                table: "quality_records",
                column: "BatchNumber");

            migrationBuilder.CreateIndex(
                name: "idx_quality_order",
                table: "quality_records",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_quality_product",
                table: "quality_records",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "idx_quality_stage",
                table: "quality_records",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "idx_spc_alert_char",
                table: "spc_rule_alerts",
                column: "CharacteristicCode");

            migrationBuilder.CreateIndex(
                name: "idx_spc_alert_created",
                table: "spc_rule_alerts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_spc_characteristic",
                table: "spc_samples",
                column: "CharacteristicCode");

            migrationBuilder.CreateIndex(
                name: "idx_spc_collected_at",
                table: "spc_samples",
                column: "CollectedAt");

            migrationBuilder.CreateIndex(
                name: "idx_spc_equipment",
                table: "spc_samples",
                column: "EquipmentCode");

            migrationBuilder.CreateIndex(
                name: "idx_spc_order",
                table: "spc_samples",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "idx_spc_subgroup",
                table: "spc_samples",
                column: "SubgroupIndex");

            migrationBuilder.CreateIndex(
                name: "IX_spc_samples_CharacteristicCode_SubgroupIndex",
                table: "spc_samples",
                columns: new[] { "CharacteristicCode", "SubgroupIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eight_d_reports");

            migrationBuilder.DropTable(
                name: "inspection_plans");

            migrationBuilder.DropTable(
                name: "non_conformance_reports");

            migrationBuilder.DropTable(
                name: "quality_records");

            migrationBuilder.DropTable(
                name: "spc_rule_alerts");

            migrationBuilder.DropTable(
                name: "spc_samples");
        }
    }
}
