using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairPayrollSchemaDriftV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PayrollLineSet"
                ADD COLUMN IF NOT EXISTS "LoanDeduction" numeric NOT NULL DEFAULT 0.0;
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AllowancePolicySet" (
                    "Id" uuid NOT NULL,
                    "TenantId" uuid NOT NULL,
                    "PolicyName" text NOT NULL,
                    "JobTitle" text NOT NULL,
                    "MonthlyAmount" numeric NOT NULL,
                    "EffectiveFrom" date NOT NULL,
                    "EffectiveTo" date,
                    "IsTaxable" boolean NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "UpdatedAtUtc" timestamp with time zone,
                    CONSTRAINT "PK_AllowancePolicySet" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_AllowancePolicySet_TenantId_IsActive_JobTitle"
                ON "AllowancePolicySet" ("TenantId", "IsActive", "JobTitle");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AllowancePolicySet_TenantId_PolicyName"
                ON "AllowancePolicySet" ("TenantId", "PolicyName");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: this migration is a production safety repair
            // and should not drop potentially in-use schema objects.
        }
    }
}
