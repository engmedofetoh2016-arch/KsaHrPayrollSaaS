using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceScoreSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplianceScoreSnapshotSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Grade = table.Column<string>(type: "text", nullable: false),
                    SaudizationPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    WpsCompanyReady = table.Column<bool>(type: "boolean", nullable: false),
                    EmployeesMissingPaymentData = table.Column<int>(type: "integer", nullable: false),
                    CriticalAlerts = table.Column<int>(type: "integer", nullable: false),
                    WarningAlerts = table.Column<int>(type: "integer", nullable: false),
                    NoticeAlerts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceScoreSnapshotSet", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceScoreSnapshotSet_TenantId_SnapshotDate",
                table: "ComplianceScoreSnapshotSet",
                columns: new[] { "TenantId", "SnapshotDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplianceScoreSnapshotSet");
        }
    }
}
