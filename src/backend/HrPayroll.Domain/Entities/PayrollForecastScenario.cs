using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class PayrollForecastScenario : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public Guid? BasePayrollRunId { get; set; }
    public int PlannedSaudiHires { get; set; }
    public int PlannedNonSaudiHires { get; set; }
    public int PlannedAttrition { get; set; }
    public decimal PlannedSalaryDeltaPercent { get; set; }
    public string AssumptionsJson { get; set; } = "{}";
    public Guid? CreatedByUserId { get; set; }
}
