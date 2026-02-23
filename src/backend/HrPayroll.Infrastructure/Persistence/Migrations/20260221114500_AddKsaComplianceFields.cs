using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKsaComplianceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IqamaNumber",
                table: "EmployeeSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "IqamaExpiryDate",
                table: "EmployeeSet",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkPermitExpiryDate",
                table: "EmployeeSet",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IqamaNumber",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "IqamaExpiryDate",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "WorkPermitExpiryDate",
                table: "EmployeeSet");
        }
    }
}

