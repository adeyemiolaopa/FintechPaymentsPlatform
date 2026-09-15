using Payments.Ledger.Application.Ledger;

namespace Payments.Ledger.Api.Endpoints;

public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var ledger = endpoints.MapGroup("/api/v1/ledger").WithTags("Ledger").RequireAuthorization();

        ledger.MapPost("/accounts", async (CreateLedgerAccountRequest request, ILedgerService service, CancellationToken cancellationToken) => Results.Created("/api/v1/ledger/accounts", await service.CreateAccountAsync(request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.account.create");
        ledger.MapGet("/accounts/{ledgerAccountId:guid}", async (Guid ledgerAccountId, ILedgerService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAccountAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.account.read");
        ledger.MapGet("/accounts/{ledgerAccountId:guid}/balance", async (Guid ledgerAccountId, DateTimeOffset? asOfUtc, ILedgerService service, CancellationToken cancellationToken) => Results.Ok(await service.GetBalanceAsync(ledgerAccountId, asOfUtc, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.account.read");
        ledger.MapGet("/accounts/{ledgerAccountId:guid}/entries", async (Guid ledgerAccountId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? side, string? transactionType, string? cursor, int pageSize, ILedgerService service, CancellationToken cancellationToken) => Results.Ok(await service.GetEntriesAsync(ledgerAccountId, fromUtc, toUtc, side, transactionType, cursor, pageSize, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.transaction.read");

        ledger.MapPost("/transactions", async (PostLedgerTransactionRequest request, ILedgerService service, CancellationToken cancellationToken) => Results.Created("/api/v1/ledger/transactions", await service.PostTransactionAsync(request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.transaction.post");
        ledger.MapGet("/transactions/{transactionId:guid}", async (Guid transactionId, ILedgerService service, CancellationToken cancellationToken) => Results.Ok(await service.GetTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.transaction.read");
        ledger.MapPost("/transactions/{transactionId:guid}/reverse", async (Guid transactionId, ReverseLedgerTransactionRequest request, ILedgerService service, CancellationToken cancellationToken) => Results.Created($"/api/v1/ledger/transactions/{transactionId}/reverse", await service.ReverseTransactionAsync(transactionId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.transaction.reverse");

        ledger.MapPost("/integrity/verify", async (ILedgerService service, CancellationToken cancellationToken) => Results.Ok(await service.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false))).RequireAuthorization("ledger.integrity.read");
        return endpoints;
    }
}
