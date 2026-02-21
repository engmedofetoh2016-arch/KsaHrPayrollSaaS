using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class Employee : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public decimal BaseSalary { get; set; }
    public bool IsSaudiNational { get; set; }
    public bool IsGosiEligible { get; set; }
    public decimal GosiBasicWage { get; set; }
    public decimal GosiHousingAllowance { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankIban { get; set; } = string.Empty;
    public string IqamaNumber { get; set; } = string.Empty;
    public DateOnly? IqamaExpiryDate { get; set; }
    public DateOnly? WorkPermitExpiryDate { get; set; }
}
