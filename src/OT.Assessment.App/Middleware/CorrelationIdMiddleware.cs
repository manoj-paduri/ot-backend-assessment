using System.Text.RegularExpressions;

namespace OT.Assessment.App.Middleware;

/// <summary>
/// Gives every request a correlation id: the caller's <c>X-Correlation-Id</c> if it is a sane value,
/// otherwise a new one. It is echoed on the response and attached to every log entry written while the
/// request is processed, so one request can be followed across the logs.
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();

        // Only accept short, harmless values: the id ends up in logs and response headers.
        return SafeIdPattern().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeIdPattern();
}
