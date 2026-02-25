using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class EmployeeLoan : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string LoanType { get; set; } = string.Empty;
    public decimal PrincipalAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public int TotalInstallments { get; set; }
    public int PaidInstallments { get; set; }
    public string Status { get; set; } = "Draft";
    public string Notes { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
}
