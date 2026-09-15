using Payments.Payment.Application.Payments;

namespace Payments.Payment.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var payments = endpoints.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();
        payments.MapPost("/", async (CreatePaymentRequest request, HttpContext httpContext, IPaymentService service, CancellationToken cancellationToken) =>
        {
            var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            var result = await service.CreateAsync(request, idempotencyKey ?? string.Empty, cancellationToken).ConfigureAwait(false);
            var location = $"/api/v1/payments/{result.Payment.PaymentId:D}";
            httpContext.Response.Headers.Location = location;
            if (result.Replayed) httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            return result.StatusCode == StatusCodes.Status202Accepted
                ? Results.Accepted(location, result.Payment)
                : Results.Created(location, result.Payment);
        })
        .RequireAuthorization("payment.create");
        payments.MapGet("/", async (string? status, string? type, string? currency, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(new PaymentSearchRequest(status, type, currency, fromUtc, toUtc, page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize), cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapGet("/{paymentId:guid}", async (Guid paymentId, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(paymentId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapGet("/{paymentId:guid}/timeline", async (Guid paymentId, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.GetTimelineAsync(paymentId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapPost("/{paymentId:guid}/cancel", async (Guid paymentId, CancelPaymentRequest request, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.CancelAsync(paymentId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.cancel.self");
        return endpoints;
    }
}
