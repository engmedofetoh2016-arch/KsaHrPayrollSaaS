using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class EmployeeLoanInstallment : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeLoanId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? PayrollRunId { get; set; }
    public DateTime? DeductedAtUtc { get; set; }
}
