using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ComplianceAlert : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public bool IsSaudiNational { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public int DaysLeft { get; set; }
    public string Severity { get; set; } = "Notice";
    public bool IsResolved { get; set; }
    public string ResolveReason { get; set; } = string.Empty;
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime LastDetectedAtUtc { get; set; }
}
