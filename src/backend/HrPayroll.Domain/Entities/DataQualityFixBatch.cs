using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class DataQualityFixBatch : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public Guid? TriggeredByUserId { get; set; }
    public int TotalItems { get; set; }
    public int SuccessItems { get; set; }
    public int FailedItems { get; set; }
    public string Status { get; set; } = "Completed";
    public string ResultJson { get; set; } = "{}";
}
