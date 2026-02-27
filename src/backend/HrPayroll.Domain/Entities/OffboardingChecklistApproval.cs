using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class OffboardingChecklistApproval : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid ChecklistItemId { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? RequestedByUserId { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}
