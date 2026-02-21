using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ComplianceScoreSnapshot : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int Score { get; set; }
    public string Grade { get; set; } = "D";
    public decimal SaudizationPercent { get; set; }
    public bool WpsCompanyReady { get; set; }
    public int EmployeesMissingPaymentData { get; set; }
    public int CriticalAlerts { get; set; }
    public int WarningAlerts { get; set; }
    public int NoticeAlerts { get; set; }
}
