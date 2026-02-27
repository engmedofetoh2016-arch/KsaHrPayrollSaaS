using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class AllowancePolicy : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsTaxable { get; set; } = false;
    public bool IsActive { get; set; } = true;
}
