using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class TimesheetEntry : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? ShiftRuleId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal HoursWorked { get; set; }
    public decimal ApprovedOvertimeHours { get; set; }
    public bool IsWeekend { get; set; }
    public bool IsHoliday { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}
