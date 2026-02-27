using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class AllowancePolicyMatrix : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public string GradeCode { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public decimal HousingAmount { get; set; }
    public decimal TransportAmount { get; set; }
    public decimal MealAmount { get; set; }
    public string ProrationMethod { get; set; } = "CalendarDays";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsTaxable { get; set; }
    public bool IsActive { get; set; } = true;
}
