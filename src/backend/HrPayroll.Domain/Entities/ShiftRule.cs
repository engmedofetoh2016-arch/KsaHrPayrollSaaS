using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ShiftRule : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal StandardDailyHours { get; set; } = 8m;
    public decimal OvertimeMultiplierWeekday { get; set; } = 1.5m;
    public decimal OvertimeMultiplierWeekend { get; set; } = 2m;
    public decimal OvertimeMultiplierHoliday { get; set; } = 2.5m;
    public string WeekendDaysCsv { get; set; } = "5,6";
    public bool IsActive { get; set; } = true;
}
