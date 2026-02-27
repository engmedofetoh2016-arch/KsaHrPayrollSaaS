using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class PayrollApprovalAction : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public DateTime ActionAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public Guid? RolledBackActionId { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
