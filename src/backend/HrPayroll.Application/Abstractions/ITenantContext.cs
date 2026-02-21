namespace HrPayroll.Application.Abstractions;

public interface ITenantContext
{
    Guid TenantId { get; }
}
