using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HrPayroll.Infrastructure.Persistence.Migrations;

[Migration("20260227120500_AddPayrollWorkflowAutomationFields")]
public partial class AddPayrollWorkflowAutomationFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AutoApproveEnabled",
            table: "PayrollApprovalMatrixSet",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "EscalationRole",
            table: "PayrollApprovalMatrixSet",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "SlaEscalationHours",
            table: "PayrollApprovalMatrixSet",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AutoApproveEnabled",
            table: "PayrollApprovalMatrixSet");

        migrationBuilder.DropColumn(
            name: "EscalationRole",
            table: "PayrollApprovalMatrixSet");

        migrationBuilder.DropColumn(
            name: "SlaEscalationHours",
            table: "PayrollApprovalMatrixSet");
    }
}
