using HrPayroll.Domain.Common;
using HrPayroll.Domain.Enums;

namespace HrPayroll.Domain.Entities;

public class ExportArtifact : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid PayrollRunId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public ExportArtifactStatus Status { get; set; } = ExportArtifactStatus.Pending;
    public string? ErrorMessage { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? FileData { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
