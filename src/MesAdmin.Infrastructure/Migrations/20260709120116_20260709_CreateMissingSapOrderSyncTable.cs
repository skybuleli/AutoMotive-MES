using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MesAdmin.Infrastructure.Migrations
{
    /// <summary>
    /// 修复：补建 sap_order_sync_records 表。
    /// 原 20260706_AddSapIntegration 迁移的 Up() 为空（模型快照含此实体但从未在数据库建表），
    /// 导致 SapOrderSyncService 轮询查询报 relation "sap_order_sync_records" does not exist。
    /// 本迁移手写 CreateTable 使数据库与模型对齐（幂等：IF NOT EXISTS 保护，兼容手工已建表环境）。
    /// </summary>
    public partial class _20260709_CreateMissingSapOrderSyncTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 幂等保护：若某些环境已手工建表则跳过，避免重复建表报错。
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS sap_order_sync_records (
    ""Id"" uuid NOT NULL,
    ""OrderId"" uuid NOT NULL,
    ""OrderNumber"" character varying(32) NOT NULL,
    ""ExternalOrderNumber"" character varying(64) NOT NULL,
    ""Status"" character varying(16) NOT NULL,
    ""QualifiedQuantity"" integer NOT NULL,
    ""SapSynced"" boolean NOT NULL,
    ""SapDocumentNumber"" character varying(64) NULL,
    ""SyncError"" character varying(512) NULL,
    ""SyncedAt"" timestamptz NULL,
    ""CreatedAt"" timestamptz NOT NULL,
    CONSTRAINT ""PK_sap_order_sync_records"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS idx_sap_order_sync_order ON sap_order_sync_records (""OrderId"");
CREATE INDEX IF NOT EXISTS idx_sap_order_sync_external ON sap_order_sync_records (""ExternalOrderNumber"");
CREATE INDEX IF NOT EXISTS idx_sap_order_sync_status ON sap_order_sync_records (""SapSynced"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sap_order_sync_records");
        }
    }
}
