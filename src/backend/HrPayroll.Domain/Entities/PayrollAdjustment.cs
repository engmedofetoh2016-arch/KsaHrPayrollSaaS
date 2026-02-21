using HrPayroll.Domain.Common;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

public class PayrollAdjustment : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PayrollAdjustmentType Type { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}
