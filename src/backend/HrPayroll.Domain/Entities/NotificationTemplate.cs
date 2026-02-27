using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class NotificationTemplate : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
