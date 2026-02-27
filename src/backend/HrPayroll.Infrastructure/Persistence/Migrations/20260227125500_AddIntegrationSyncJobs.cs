using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations;

[Migration("20260227125500_AddIntegrationSyncJobs")]
public partial class AddIntegrationSyncJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IntegrationSyncJobSet",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "text", nullable: false),
                Operation = table.Column<string>(type: "text", nullable: false),
                EntityType = table.Column<string>(type: "text", nullable: false),
                EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                RequestPayloadJson = table.Column<string>(type: "text", nullable: false),
                ResponsePayloadJson = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeadlineAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "text", nullable: false),
                ExternalReference = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationSyncJobSet", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationSyncJobSet_TenantId_DeadlineAtUtc_Status",
            table: "IntegrationSyncJobSet",
            columns: new[] { "TenantId", "DeadlineAtUtc", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationSyncJobSet_TenantId_IdempotencyKey",
            table: "IntegrationSyncJobSet",
            columns: new[] { "TenantId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationSyncJobSet_TenantId_Provider_Status_NextAttemptAtUtc",
            table: "IntegrationSyncJobSet",
            columns: new[] { "TenantId", "Provider", "Status", "NextAttemptAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IntegrationSyncJobSet");
    }
}
