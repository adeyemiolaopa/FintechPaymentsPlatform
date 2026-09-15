using Payments.Payment.Application.Payments;

namespace Payments.Payment.Api.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var payments = endpoints.MapGroup("/api/v1/payments").WithTags("Payments").RequireAuthorization();
        payments.MapPost("/", async (CreatePaymentRequest request, IPaymentService service, CancellationToken cancellationToken) => Results.Created("/api/v1/payments", await service.CreateAsync(request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.create");
        payments.MapGet("/", async (string? status, string? type, string? currency, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(new PaymentSearchRequest(status, type, currency, fromUtc, toUtc, page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize), cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapGet("/{paymentId:guid}", async (Guid paymentId, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAsync(paymentId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapGet("/{paymentId:guid}/timeline", async (Guid paymentId, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.GetTimelineAsync(paymentId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.read.self");
        payments.MapPost("/{paymentId:guid}/cancel", async (Guid paymentId, CancelPaymentRequest request, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.CancelAsync(paymentId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.cancel.self");
        return endpoints;
    }
}
