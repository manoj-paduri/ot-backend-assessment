using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OT.Assessment.Core.Wagers;

namespace OT.Assessment.App.ErrorHandling;

/// <summary>Turns unhandled exceptions into RFC 7807 problem responses without leaking internals.</summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away: nothing to report and nobody to answer.
            return true;
        }

        var (status, title) = exception switch
        {
            MessagePublishException => (StatusCodes.Status503ServiceUnavailable, "The message bus is temporarily unavailable."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        _logger.LogError(exception, "Request {Method} {Path} failed with {Status}",
            httpContext.Request.Method, httpContext.Request.Path, status);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Instance = httpContext.Request.Path },
            cancellationToken);

        return true;
    }
}
