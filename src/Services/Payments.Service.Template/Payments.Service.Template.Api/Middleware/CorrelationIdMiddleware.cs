using System.Diagnostics;
using Serilog.Context;

namespace Payments.Service.Template.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValidCorrelationId(supplied) ? supplied : Guid.NewGuid().ToString("D");
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        Activity.Current?.SetTag("correlation.id", correlationId);
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier))
        using (LogContext.PushProperty("SpanId", Activity.Current?.SpanId.ToString() ?? string.Empty))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static bool IsValidCorrelationId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
