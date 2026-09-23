using System.Security.Cryptography;
using System.Text;
using Payments.Ledger.Application.Ledger;

namespace Payments.Ledger.Api.Endpoints;

public static class LedgerReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapLedgerReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/reconciliation/transactions/{id:guid}", async (Guid id, HttpContext http, IConfiguration config, ILedgerService service, CancellationToken ct) =>
        {
            var configured = config["InternalReconciliation:SharedKey"];
            var supplied = http.Request.Headers["X-Reconciliation-Key"].ToString();
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied) ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                return Results.Unauthorized();
            return Results.Ok(await service.GetTransactionAsync(id, ct).ConfigureAwait(false));
        });
        app.MapGet("/internal/reconciliation/transactions/by-reference", async (string externalReference, HttpContext http, IConfiguration config, ILedgerService service, CancellationToken ct) =>
        {
            var configured = config["InternalReconciliation:SharedKey"];
            var supplied = http.Request.Headers["X-Reconciliation-Key"].ToString();
            if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied) ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Length > 128) return Results.BadRequest();
            var transaction = await service.GetTransactionByExternalReferenceAsync(externalReference, ct).ConfigureAwait(false);
            return transaction is null ? Results.NotFound() : Results.Ok(transaction);
        }); return app;
    }
}
