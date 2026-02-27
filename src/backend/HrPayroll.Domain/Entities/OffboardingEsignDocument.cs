using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class OffboardingEsignDocument : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid ChecklistItemId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string DocumentUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "Signed";
    public Guid? SignedByUserId { get; set; }
    public DateTime? SignedAtUtc { get; set; }
}
