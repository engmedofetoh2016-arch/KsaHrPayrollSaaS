using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class OffboardingChecklistTemplate : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool RequiresApproval { get; set; } = false;
    public bool RequiresEsign { get; set; } = false;
    public bool IsActive { get; set; } = true;
}
