using HrPayroll.Domain.Common;

namespace HrPayroll.Domain.Entities;

public class EmployeeSelfServiceRequest : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string PayloadJson { get; set; } = "{}";
    public Guid? ReviewerUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string ResolutionNotes { get; set; } = string.Empty;
}
