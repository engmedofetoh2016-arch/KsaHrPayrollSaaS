using System.Diagnostics;
using System.Security.Claims;
using HrPayroll.Application.Abstractions;
using HrPayroll.Domain.Entities;

namespace HrPayroll.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;

    public AuditLogMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApplicationDbContext dbContext, ITenantContext tenantContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            Guid? userId = null;
            var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdClaim, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var audit = new AuditLog
            {
                TenantId = tenantContext.TenantId,
                UserId = userId,
                Method = context.Request.Method,
                Path = path,
                StatusCode = context.Response.StatusCode,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                DurationMs = stopwatch.ElapsedMilliseconds
            };

            dbContext.AddEntity(audit);
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }
    }
}
