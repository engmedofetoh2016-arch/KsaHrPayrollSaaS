using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGosiV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GosiEmployeeContribution",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GosiEmployerContribution",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GosiWageBase",
                table: "PayrollLineSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GosiBasicWage",
                table: "EmployeeSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GosiHousingAllowance",
                table: "EmployeeSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsGosiEligible",
                table: "EmployeeSet",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSaudiNational",
                table: "EmployeeSet",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GosiEmployeeContribution",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "GosiEmployerContribution",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "GosiWageBase",
                table: "PayrollLineSet");

            migrationBuilder.DropColumn(
                name: "GosiBasicWage",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "GosiHousingAllowance",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "IsGosiEligible",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "IsSaudiNational",
                table: "EmployeeSet");
        }
    }
}
