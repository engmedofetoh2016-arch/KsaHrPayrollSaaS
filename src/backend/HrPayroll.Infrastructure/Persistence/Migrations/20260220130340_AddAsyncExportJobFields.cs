using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsyncExportJobFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "ExportArtifactSet",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ExportArtifactSet",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FileData",
                table: "ExportArtifactSet",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ExportArtifactSet",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_ExportArtifactSet_TenantId_Status_CreatedAtUtc",
                table: "ExportArtifactSet",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExportArtifactSet_TenantId_Status_CreatedAtUtc",
                table: "ExportArtifactSet");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "ExportArtifactSet");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ExportArtifactSet");

            migrationBuilder.DropColumn(
                name: "FileData",
                table: "ExportArtifactSet");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ExportArtifactSet");
        }
    }
}
