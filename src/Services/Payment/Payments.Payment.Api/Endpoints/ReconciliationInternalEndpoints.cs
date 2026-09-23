using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Payments.Payment.Application.Payments;
using Payments.Payment.Domain.Payments;
using Payments.Payment.Infrastructure.Persistence;

namespace Payments.Payment.Api.Endpoints;

public static class ReconciliationInternalEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/reconciliation");
        group.AddEndpointFilter((context, next) =>
        {
            var http = context.HttpContext;
            var configured = http.RequestServices.GetRequiredService<IConfiguration>()["InternalReconciliation:SharedKey"];
            var supplied = http.Request.Headers["X-Reconciliation-Key"].ToString();
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied))
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? next(context) : ValueTask.FromResult<object?>(Results.Unauthorized());
        });
        group.MapGet("/payments", async (string provider, string? providerReference, string? clientReference, PaymentDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(provider) || (string.IsNullOrWhiteSpace(providerReference) && string.IsNullOrWhiteSpace(clientReference))) return Results.BadRequest();
            var submission = await db.RailSubmissions.AsNoTracking().Where(x => x.Provider == provider &&
                ((providerReference != null && x.ProviderReference == providerReference) || (clientReference != null && x.ClientReference == clientReference)))
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (submission is null) return Results.NotFound();
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == submission.PaymentId, ct).ConfigureAwait(false);
            return Results.Ok(Map(payment, submission));
        });
        group.MapGet("/pending", async (int offset, int limit, PaymentDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Payments.AsNoTracking()
                .Where(x => x.Status == PaymentStatus.Processing || x.Status == PaymentStatus.SubmittedToRail || x.Status == PaymentStatus.PendingReconciliation)
                .Join(db.RailSubmissions.AsNoTracking(), payment => payment.Id, submission => submission.PaymentId, (payment, submission) => new { payment, submission })
                .OrderBy(x => x.payment.Id).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows.Select(x => Map(x.payment, x.submission)).ToArray());
        }); group.MapGet("/payments/{paymentId:guid}", async (Guid paymentId, PaymentDbContext db, CancellationToken ct) =>
        {
            var submission = await db.RailSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct).ConfigureAwait(false);
            if (submission is null) return Results.NotFound();
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId, ct).ConfigureAwait(false);
            return Results.Ok(Map(payment, submission));
        });
        group.MapGet("/payments/{paymentId:guid}/provider", async (Guid paymentId, PaymentDbContext db, IRailRouter router, CancellationToken ct) =>
        {
            var submission = await db.RailSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct).ConfigureAwait(false);
            if (submission is null) return Results.NotFound();
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId, ct).ConfigureAwait(false);
            var destination = payment.Destination;
            var instruction = new RailTransferInstruction(payment.Id, submission.ClientReference, payment.SourceAccountId.ToString("D"), destination.BankCode ?? "", destination.AccountNumber ?? "", destination.AccountName ?? "", destination.CountryCode ?? "", payment.Amount, payment.Currency.Code, payment.Description);
            var route = await router.RouteAsync(instruction, ct).ConfigureAwait(false);
            var status = await route.Adapter.GetTransferStatusAsync(submission.ClientReference, submission.ProviderReference, ct).ConfigureAwait(false);
            return Results.Ok(new { Provider = submission.Provider, ProviderReference = status.ProviderReference, submission.ClientReference, Status = status.RawStatus ?? status.Outcome.ToString(), Amount = status.ProviderAmount, Currency = status.ProviderCurrency, ProviderClientReference = status.ProviderClientReference, ResponseCode = status.ProviderResponseCode, ObservedAtUtc = status.CheckedAtUtc, Outcome = status.Outcome.ToString() });
        });
        group.MapGet("/expected", async (string provider, DateOnly settlementDate, int offset, int limit, PaymentDbContext db, CancellationToken ct) =>
        {
            var start = new DateTimeOffset(settlementDate.AddDays(-2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(settlementDate.AddDays(3).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var submissions = await db.RailSubmissions.AsNoTracking().Where(x => x.Provider == provider && x.Outcome == RailSubmissionOutcome.Succeeded && x.RespondedAtUtc >= start && x.RespondedAtUtc < end)
                .OrderBy(x => x.PaymentId).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct).ConfigureAwait(false);
            var ids = submissions.Select(x => x.PaymentId).ToArray();
            var payments = await db.Payments.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
            return Results.Ok(submissions.Select(x => Map(payments[x.PaymentId], x)).ToArray());
        });
        group.MapPost("/payments/{paymentId:guid}/recover", async (Guid paymentId, IPaymentService service, CancellationToken ct) =>
        {
            await service.RecoverOneAsync(paymentId, ct).ConfigureAwait(false);
            return Results.Accepted();
        });
        return app;
    }

    private static object Map(Payments.Payment.Domain.Payments.Payment payment, RailSubmission submission) => new
    {
        PaymentId = payment.Id,
        PaymentReference = payment.Reference,
        submission.Provider,
        submission.ProviderReference,
        submission.ClientReference,
        payment.Amount,
        Currency = payment.Currency.Code,
        Status = payment.Status.ToString(),
        payment.LedgerTransactionId,
        SourceUpdatedAtUtc = payment.UpdatedAtUtc
    };
}
