using System.Net;
using System.Text.Json;
using FluentValidation;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Payment.Application.Payments;

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
        var (status, title, type) = exception switch
        {
            FluentValidation.ValidationException => (HttpStatusCode.BadRequest, "Validation failed", "https://httpstatuses.com/400"),
            IdempotencyKeyValidationException => (HttpStatusCode.BadRequest, "Invalid idempotency key", "https://httpstatuses.com/400"),
            IdempotencyKeyConflictException => (HttpStatusCode.Conflict, "Idempotency key conflict", "https://errors.example.com/idempotency-conflict"),
            DomainException domain when domain.Code == "payment.invalid_transition" => (HttpStatusCode.Conflict, "Invalid payment transition", "https://httpstatuses.com/409"),
            DomainException => (HttpStatusCode.BadRequest, "Payment rule violation", "https://httpstatuses.com/400"),
            ConflictException => (HttpStatusCode.Conflict, "Conflict", "https://httpstatuses.com/409"),
            NotFoundException => (HttpStatusCode.NotFound, "Not found", "https://httpstatuses.com/404"),
            UnauthorizedApplicationException => (HttpStatusCode.Unauthorized, "Unauthorized", "https://httpstatuses.com/401"),
            ForbiddenApplicationException => (HttpStatusCode.Forbidden, "Forbidden", "https://httpstatuses.com/403"),
            _ => (HttpStatusCode.InternalServerError, "Unexpected error", "https://httpstatuses.com/500"),
        };

        if (status == HttpStatusCode.InternalServerError) _logger.LogError(exception, "Unhandled payment API error.");
        else _logger.LogWarning(exception, "Payment API handled error {StatusCode}.", (int)status);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { type, title, status = (int)status, detail = exception.Message, traceId = context.TraceIdentifier }, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ConfigureAwait(false);
    }
}
