using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceDigestSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComplianceDigestEmail",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ComplianceDigestEnabled",
                table: "CompanyProfileSet",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ComplianceDigestFrequency",
                table: "CompanyProfileSet",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ComplianceDigestHourUtc",
                table: "CompanyProfileSet",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastComplianceDigestSentAtUtc",
                table: "CompanyProfileSet",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplianceDigestEmail",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "ComplianceDigestEnabled",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "ComplianceDigestFrequency",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "ComplianceDigestHourUtc",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "LastComplianceDigestSentAtUtc",
                table: "CompanyProfileSet");
        }
    }
}
