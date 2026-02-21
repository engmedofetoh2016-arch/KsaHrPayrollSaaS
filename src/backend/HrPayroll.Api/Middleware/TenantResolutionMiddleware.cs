using HrPayroll.Infrastructure.Tenancy;
using System.Security.Claims;

namespace HrPayroll.Api.Middleware;

public class TenantResolutionMiddleware
{
    private const string TenantHeaderName = "X-Tenant-Id";
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        Guid tenantId = Guid.Empty;

        if (context.Request.Headers.TryGetValue(TenantHeaderName, out var tenantHeader)
            && Guid.TryParse(tenantHeader.ToString(), out var headerTenantId))
        {
            tenantId = headerTenantId;
        }
        else
        {
            var claimValue = context.User.FindFirstValue("tenant_id");
            if (Guid.TryParse(claimValue, out var claimTenantId))
            {
                tenantId = claimTenantId;
            }
        }

        if (tenantId != Guid.Empty)
        {
            tenantContextAccessor.SetTenantId(tenantId);
        }

        await _next(context);
    }
}
