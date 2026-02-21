using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class AttendanceInput : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int DaysPresent { get; set; }
    public int DaysAbsent { get; set; }
    public decimal OvertimeHours { get; set; }
}
