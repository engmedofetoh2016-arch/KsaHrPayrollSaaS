using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class PayrollApprovalMatrix : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string PayrollScope { get; set; } = "Default";
    public string StageCode { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public int StageOrder { get; set; }
    public string ApproverRole { get; set; } = string.Empty;
    public bool AllowRollback { get; set; }
    public bool AutoApproveEnabled { get; set; }
    public int SlaEscalationHours { get; set; }
    public string EscalationRole { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
