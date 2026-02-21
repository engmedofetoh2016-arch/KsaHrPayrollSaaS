using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEosV1AndEmployeeStartDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "EmployeeSet",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "EosAfterFiveYearsMonthFactor",
                table: "CompanyProfileSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EosFirstFiveYearsMonthFactor",
                table: "CompanyProfileSet",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "EmployeeSet");

            migrationBuilder.DropColumn(
                name: "EosAfterFiveYearsMonthFactor",
                table: "CompanyProfileSet");

            migrationBuilder.DropColumn(
                name: "EosFirstFiveYearsMonthFactor",
                table: "CompanyProfileSet");
        }
    }
}
