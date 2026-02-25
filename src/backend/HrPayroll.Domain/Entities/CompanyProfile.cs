using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class CompanyProfile : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "SAR";
    public int DefaultPayDay { get; set; } = 25;
    public decimal EosFirstFiveYearsMonthFactor { get; set; } = 0.5m;
    public decimal EosAfterFiveYearsMonthFactor { get; set; } = 1.0m;
    public string WpsCompanyBankName { get; set; } = string.Empty;
    public string WpsCompanyBankCode { get; set; } = string.Empty;
    public string WpsCompanyIban { get; set; } = string.Empty;
    public bool ComplianceDigestEnabled { get; set; }
    public string ComplianceDigestEmail { get; set; } = string.Empty;
    public string ComplianceDigestFrequency { get; set; } = "Weekly";
    public int ComplianceDigestHourUtc { get; set; } = 6;
    public DateTime? LastComplianceDigestSentAtUtc { get; set; }
    public string NitaqatActivity { get; set; } = "General";
    public string NitaqatSizeBand { get; set; } = "Small";
    public decimal NitaqatTargetPercent { get; set; } = 30m;
}
