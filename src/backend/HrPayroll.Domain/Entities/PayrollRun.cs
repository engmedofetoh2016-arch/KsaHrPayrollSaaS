using HrPayroll.Domain.Common;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

public class PayrollRun : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;
    public DateTime? CalculatedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? LockedAtUtc { get; set; }
}
