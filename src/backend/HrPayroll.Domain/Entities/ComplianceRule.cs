using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ComplianceRule : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string RuleCategory { get; set; } = string.Empty;
    public string RuleConfigJson { get; set; } = "{}";
    public string Severity { get; set; } = "Warning";
    public bool IsEnabled { get; set; } = true;
}
