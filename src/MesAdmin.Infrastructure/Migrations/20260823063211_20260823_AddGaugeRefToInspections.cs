using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _20260823_AddGaugeRefToInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GaugeId",
                table: "spc_samples",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GaugeId",
                table: "quality_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GaugeId",
                table: "first_article_inspections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_spc_gauge",
                table: "spc_samples",
                column: "GaugeId");

            migrationBuilder.CreateIndex(
                name: "idx_quality_gauge",
                table: "quality_records",
                column: "GaugeId");

            migrationBuilder.CreateIndex(
                name: "idx_fai_gauge",
                table: "first_article_inspections",
                column: "GaugeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_spc_gauge",
                table: "spc_samples");

            migrationBuilder.DropIndex(
                name: "idx_quality_gauge",
                table: "quality_records");

            migrationBuilder.DropIndex(
                name: "idx_fai_gauge",
                table: "first_article_inspections");

            migrationBuilder.DropColumn(
                name: "GaugeId",
                table: "spc_samples");

            migrationBuilder.DropColumn(
                name: "GaugeId",
                table: "quality_records");

            migrationBuilder.DropColumn(
                name: "GaugeId",
                table: "first_article_inspections");
        }
    }
}
