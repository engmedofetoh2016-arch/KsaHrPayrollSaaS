namespace HrPayroll.Infrastructure.Tenancy;

public class TenantContextAccessor : ITenantContextAccessor
{
    public Guid TenantId { get; private set; }

    public void SetTenantId(Guid tenantId)
    {
        TenantId = tenantId;
    }
}
