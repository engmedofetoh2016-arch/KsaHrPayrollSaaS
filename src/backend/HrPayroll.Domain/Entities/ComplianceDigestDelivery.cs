using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class ComplianceDigestDelivery : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid? RetryOfDeliveryId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Manual";
    public string Frequency { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public bool Simulated { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Grade { get; set; } = "D";
    public DateTime? SentAtUtc { get; set; }
}
