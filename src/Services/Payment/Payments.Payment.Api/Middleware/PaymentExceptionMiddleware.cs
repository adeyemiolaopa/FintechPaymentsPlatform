using System.Net;
using System.Text.Json;
using FluentValidation;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Payment.Api.Middleware;

public sealed class PaymentExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PaymentExceptionMiddleware> _logger;

    public PaymentExceptionMiddleware(RequestDelegate next, ILogger<PaymentExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            FluentValidation.ValidationException => (HttpStatusCode.BadRequest, "Validation failed"),
            DomainException domain when domain.Code == "payment.invalid_transition" => (HttpStatusCode.Conflict, "Invalid payment transition"),
            DomainException => (HttpStatusCode.BadRequest, "Payment rule violation"),
            ConflictException => (HttpStatusCode.Conflict, "Conflict"),
            NotFoundException => (HttpStatusCode.NotFound, "Not found"),
            UnauthorizedApplicationException => (HttpStatusCode.Unauthorized, "Unauthorized"),
            ForbiddenApplicationException => (HttpStatusCode.Forbidden, "Forbidden"),
            _ => (HttpStatusCode.InternalServerError, "Unexpected error"),
        };

        if (status == HttpStatusCode.InternalServerError) _logger.LogError(exception, "Unhandled payment API error.");
        else _logger.LogWarning(exception, "Payment API handled error {StatusCode}.", (int)status);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { type = $"https://httpstatuses.com/{(int)status}", title, status = (int)status, detail = exception.Message, traceId = context.TraceIdentifier }, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ConfigureAwait(false);
    }
}
