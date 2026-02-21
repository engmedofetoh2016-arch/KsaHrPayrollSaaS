using HrPayroll.Domain.Common;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

public class LeaveBalance : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public LeaveType LeaveType { get; set; }
    public decimal AllocatedDays { get; set; }
    public decimal UsedDays { get; set; }
}
