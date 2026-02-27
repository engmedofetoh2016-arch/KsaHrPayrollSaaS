using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ComplianceRuleEvent : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime TriggeredAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
