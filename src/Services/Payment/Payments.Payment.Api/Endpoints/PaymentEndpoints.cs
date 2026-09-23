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
        payments.MapPost("/{paymentId:guid}/reverse", async (Guid paymentId, ReversePaymentRequest request, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.ReverseAsync(paymentId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.reverse");
        payments.MapPost("/{paymentId:guid}/verify-consistency", async (Guid paymentId, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.VerifyConsistencyAsync(paymentId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.audit.read");
        payments.MapPost("/verify-consistency", async (int batchSize, IPaymentService service, CancellationToken cancellationToken) => Results.Ok(await service.VerifyRecentConsistencyAsync(batchSize <= 0 ? 25 : batchSize, cancellationToken).ConfigureAwait(false))).RequireAuthorization("payment.audit.read");

        var rails = endpoints.MapGroup("/api/v1/rails").WithTags("Payment Rails");
        rails.MapPost("/callbacks/{provider}", async (string provider, HttpContext httpContext, IPaymentRailCallbackService service, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(httpContext.Request.Body);
            var rawBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var headers = httpContext.Request.Headers.ToDictionary(item => item.Key, item => (string?)item.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            var result = await service.ProcessCallbackAsync(provider, rawBody, headers, cancellationToken).ConfigureAwait(false);
            if (!result.Accepted)
            {
                return Results.Problem(result.Reason ?? "Rail callback was rejected.", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(result);
        });
        return endpoints;
    }
}
