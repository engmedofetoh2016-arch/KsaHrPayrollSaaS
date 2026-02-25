using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class OffboardingChecklist : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid OffboardingId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
}
