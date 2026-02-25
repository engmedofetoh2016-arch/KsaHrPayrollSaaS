using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class OffboardingChecklistItem : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid ChecklistId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
