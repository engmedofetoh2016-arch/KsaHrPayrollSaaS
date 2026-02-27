using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class NotificationQueueItem : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string RecipientType { get; set; } = string.Empty;
    public string RecipientValue { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Queued";
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
