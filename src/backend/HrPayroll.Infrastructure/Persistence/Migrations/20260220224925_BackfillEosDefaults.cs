using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillEosDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CompanyProfileSet"
                ALTER COLUMN "EosFirstFiveYearsMonthFactor" SET DEFAULT 0.5;
                ALTER TABLE "CompanyProfileSet"
                ALTER COLUMN "EosAfterFiveYearsMonthFactor" SET DEFAULT 1.0;
                UPDATE "CompanyProfileSet"
                SET "EosFirstFiveYearsMonthFactor" = 0.5
                WHERE "EosFirstFiveYearsMonthFactor" = 0;
                UPDATE "CompanyProfileSet"
                SET "EosAfterFiveYearsMonthFactor" = 1.0
                WHERE "EosAfterFiveYearsMonthFactor" = 0;
                UPDATE "EmployeeSet"
                SET "StartDate" = "CreatedAtUtc"::date
                WHERE "StartDate" = '-infinity'::date OR "StartDate" < DATE '1900-01-01';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CompanyProfileSet"
                ALTER COLUMN "EosFirstFiveYearsMonthFactor" SET DEFAULT 0.0;
                ALTER TABLE "CompanyProfileSet"
                ALTER COLUMN "EosAfterFiveYearsMonthFactor" SET DEFAULT 0.0;
                """);
        }
    }
}
