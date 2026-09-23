using Payments.Account.Application.Accounts;

namespace Payments.Account.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var accounts = endpoints.MapGroup("/api/v1/accounts").WithTags("Accounts").RequireAuthorization();
        accounts.MapPost("/", async (CreateAccountRequest request, IAccountService service, CancellationToken cancellationToken) => Results.Created("/api/v1/accounts", await service.CreateAccountAsync(request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.create");
        accounts.MapGet("/", async (IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.ListMyAccountsAsync(cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.read.self");
        accounts.MapGet("/{accountId:guid}", async (Guid accountId, IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.GetAccountAsync(accountId, cancellationToken).ConfigureAwait(false)));
        accounts.MapGet("/{accountId:guid}/balance", async (Guid accountId, IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.GetBalanceAsync(accountId, cancellationToken).ConfigureAwait(false)));
        accounts.MapPost("/{accountId:guid}/freeze", async (Guid accountId, FreezeAccountRequest request, IAccountService service, CancellationToken cancellationToken) => { await service.FreezeAsync(accountId, request, cancellationToken).ConfigureAwait(false); return Results.NoContent(); }).RequireAuthorization("account.freeze");
        accounts.MapPost("/{accountId:guid}/unfreeze", async (Guid accountId, IAccountService service, CancellationToken cancellationToken) => { await service.UnfreezeAsync(accountId, cancellationToken).ConfigureAwait(false); return Results.NoContent(); }).RequireAuthorization("account.unfreeze");
        accounts.MapPost("/{accountId:guid}/close", async (Guid accountId, IAccountService service, CancellationToken cancellationToken) => { await service.CloseAsync(accountId, cancellationToken).ConfigureAwait(false); return Results.NoContent(); }).RequireAuthorization("account.close");
        accounts.MapPost("/{accountId:guid}/restrictions", async (Guid accountId, CreateRestrictionRequest request, IAccountService service, CancellationToken cancellationToken) => Results.Created($"/api/v1/accounts/{accountId}/restrictions", await service.AddRestrictionAsync(accountId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.restrict");
        accounts.MapDelete("/{accountId:guid}/restrictions/{restrictionId:guid}", async (Guid accountId, Guid restrictionId, IAccountService service, CancellationToken cancellationToken) => { await service.RemoveRestrictionAsync(accountId, restrictionId, cancellationToken).ConfigureAwait(false); return Results.NoContent(); }).RequireAuthorization("account.unrestrict");
        accounts.MapPost("/{accountId:guid}/reservations", async (Guid accountId, CreateReservationRequest request, IAccountService service, CancellationToken cancellationToken) => Results.Created($"/api/v1/accounts/{accountId}/reservations", await service.ReserveFundsAsync(accountId, request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.create");
        accounts.MapGet("/{accountId:guid}/reservations/{reservationId:guid}", async (Guid accountId, Guid reservationId, IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.GetReservationAsync(accountId, reservationId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.read.self");
        accounts.MapPost("/{accountId:guid}/reservations/{reservationId:guid}/commit", async (Guid accountId, Guid reservationId, IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.CommitReservationAsync(accountId, reservationId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.create");
        accounts.MapPost("/{accountId:guid}/reservations/{reservationId:guid}/release", async (Guid accountId, Guid reservationId, IAccountService service, CancellationToken cancellationToken) => Results.Ok(await service.ReleaseReservationAsync(accountId, reservationId, cancellationToken).ConfigureAwait(false))).RequireAuthorization("account.create");

        var beneficiaries = endpoints.MapGroup("/api/v1/beneficiaries").WithTags("Beneficiaries").RequireAuthorization();
        beneficiaries.MapPost("/", async (CreateBeneficiaryRequest request, IBeneficiaryService service, CancellationToken cancellationToken) => Results.Created("/api/v1/beneficiaries", await service.AddAsync(request, cancellationToken).ConfigureAwait(false))).RequireAuthorization("beneficiary.create");
        beneficiaries.MapGet("/", async (int page, int pageSize, string? status, string? type, IBeneficiaryService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(page, pageSize, status, type, cancellationToken).ConfigureAwait(false))).RequireAuthorization("beneficiary.read.self");
        beneficiaries.MapDelete("/{beneficiaryId:guid}", async (Guid beneficiaryId, IBeneficiaryService service, CancellationToken cancellationToken) => { await service.RemoveAsync(beneficiaryId, cancellationToken).ConfigureAwait(false); return Results.NoContent(); }).RequireAuthorization("beneficiary.remove");
        return endpoints;
    }
}
