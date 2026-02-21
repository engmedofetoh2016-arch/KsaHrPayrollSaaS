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

            context.Response.StatusCode = ex is BadHttpRequestException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex is BadHttpRequestException badRequest
                    ? badRequest.Message
                    : "An unexpected error occurred."
            });
        }
    }
}
