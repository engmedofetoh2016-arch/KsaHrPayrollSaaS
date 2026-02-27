using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class DataQualityIssue : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string IssueCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string IssueStatus { get; set; } = "Open";
    public string IssueMessage { get; set; } = string.Empty;
    public string FixActionCode { get; set; } = string.Empty;
    public string FixPayloadJson { get; set; } = "{}";
    public DateTime DetectedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
}
