using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class PayrollLine : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal ManualDeductions { get; set; }
    public decimal LoanDeduction { get; set; }
    public decimal UnpaidLeaveDays { get; set; }
    public decimal UnpaidLeaveDeduction { get; set; }
    public decimal GosiWageBase { get; set; }
    public decimal GosiEmployeeContribution { get; set; }
    public decimal GosiEmployerContribution { get; set; }
    public decimal Deductions { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal NetAmount { get; set; }
}
