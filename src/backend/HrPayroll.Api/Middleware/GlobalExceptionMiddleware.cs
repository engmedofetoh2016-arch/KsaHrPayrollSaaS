using Npgsql;

namespace HrPayroll.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error for request {method} {path}", context.Request.Method, context.Request.Path);

            var statusCode = ex is BadHttpRequestException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
            var errorMessage = ex is BadHttpRequestException badRequest
                ? badRequest.Message
                : "An unexpected error occurred.";

            if (TryMapSchemaMismatch(ex, out var schemaMessage))
            {
                statusCode = StatusCodes.Status500InternalServerError;
                errorMessage = schemaMessage;
            }

            var traceId = context.TraceIdentifier;
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = errorMessage,
                traceId
            });
        }
    }

    private static bool TryMapSchemaMismatch(Exception ex, out string message)
    {
        message = string.Empty;
        Exception? current = ex;
        while (current is not null)
        {
            if (current is PostgresException pgEx &&
                (pgEx.SqlState == "42P01" || pgEx.SqlState == "42703"))
            {
                message = "Database schema is outdated. Apply the latest migrations and retry.";
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
