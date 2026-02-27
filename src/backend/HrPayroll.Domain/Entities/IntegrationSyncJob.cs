using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class IntegrationSyncJob : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestPayloadJson { get; set; } = "{}";
    public string ResponsePayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Queued";
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? DeadlineAtUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
}
