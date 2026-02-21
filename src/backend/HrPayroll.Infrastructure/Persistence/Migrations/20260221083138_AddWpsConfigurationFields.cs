using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWpsConfigurationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankIban",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmployeeNumber",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WpsCompanyBankCode",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WpsCompanyBankName",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WpsCompanyIban",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankIban",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "EmployeeNumber",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "WpsCompanyBankCode",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "WpsCompanyBankName",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "WpsCompanyIban",
                table: "CompanyProfileSet");
        }
    }
}
