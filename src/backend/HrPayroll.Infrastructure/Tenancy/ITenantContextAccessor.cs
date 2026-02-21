using HrPayroll.Application.Abstractions;

namespace HrPayroll.Infrastructure.Tenancy;

public interface ITenantContextAccessor : ITenantContext
{
    void SetTenantId(Guid tenantId);
}
