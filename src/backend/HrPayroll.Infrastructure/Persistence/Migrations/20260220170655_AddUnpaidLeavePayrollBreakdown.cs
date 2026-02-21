using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnpaidLeavePayrollBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ManualDeductions",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDays",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnpaidLeaveDeduction",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualDeductions",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDays",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "UnpaidLeaveDeduction",
                table: "PayrollLineSet");
        }
    }
}
