using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class PayrollForecastResult : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid ScenarioId { get; set; }
    public int ForecastYear { get; set; }
    public int ForecastMonth { get; set; }
    public decimal ProjectedPayrollCost { get; set; }
    public int ProjectedHeadcount { get; set; }
    public decimal ProjectedSaudizationPercent { get; set; }
    public int ComplianceRiskScore { get; set; }
    public string ResultJson { get; set; } = "{}";
}
