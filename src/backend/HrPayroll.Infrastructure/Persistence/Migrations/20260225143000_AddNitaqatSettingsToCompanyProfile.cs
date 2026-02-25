using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNitaqatSettingsToCompanyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NitaqatActivity",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "NitaqatSizeBand",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "Small");

            migrationBuilder.AddColumn<decimal>(
                name: "NitaqatTargetPercent",
                table: "CompanyProfileSet",
                type: "numeric",
                nullable: false,
                defaultValue: 30m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NitaqatActivity",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "NitaqatSizeBand",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "NitaqatTargetPercent",
                table: "CompanyProfileSet");
        }
    }
}
