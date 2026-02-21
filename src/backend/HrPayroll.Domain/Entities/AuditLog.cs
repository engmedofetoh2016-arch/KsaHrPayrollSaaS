using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class AuditLog : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public long DurationMs { get; set; }
}
