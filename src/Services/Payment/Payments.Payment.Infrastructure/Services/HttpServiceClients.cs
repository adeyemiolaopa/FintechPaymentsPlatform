using System.Net;
using System.Net.Http.Json;
using Payments.Payment.Application.Payments;
using Payments.Payment.Domain.Payments;

namespace Payments.Payment.Infrastructure.Services;

public sealed class HttpAccountServiceClient : IAccountServiceClient
{
    private readonly HttpClient _httpClient;
    public HttpAccountServiceClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<AccountClientResponse> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => await SendAsync<AccountClientResponse>(() => _httpClient.GetAsync($"/api/v1/accounts/{accountId:D}", cancellationToken), PaymentFailureReasonCode.SourceAccountNotFound, cancellationToken).ConfigureAwait(false);

    public async Task<AccountBalanceClientResponse> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
        => await SendAsync<AccountBalanceClientResponse>(() => _httpClient.GetAsync($"/api/v1/accounts/{accountId:D}/balance", cancellationToken), PaymentFailureReasonCode.SourceAccountNotFound, cancellationToken).ConfigureAwait(false);

    public async Task<ReservationClientResponse> ReserveFundsAsync(Guid accountId, CreateFundsReservationCommand command, CancellationToken cancellationToken = default)
        => await SendAsync<ReservationClientResponse>(() => _httpClient.PostAsJsonAsync($"/api/v1/accounts/{accountId:D}/reservations", command, cancellationToken), PaymentFailureReasonCode.InsufficientFunds, cancellationToken).ConfigureAwait(false);

    public async Task<ReservationClientResponse> CommitReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
        => await SendAsync<ReservationClientResponse>(() => _httpClient.PostAsync($"/api/v1/accounts/{accountId:D}/reservations/{reservationId:D}/commit", null, cancellationToken), PaymentFailureReasonCode.SystemFailure, cancellationToken).ConfigureAwait(false);

    public async Task<ReservationClientResponse> ReleaseReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
        => await SendAsync<ReservationClientResponse>(() => _httpClient.PostAsync($"/api/v1/accounts/{accountId:D}/reservations/{reservationId:D}/release", null, cancellationToken), PaymentFailureReasonCode.SystemFailure, cancellationToken).ConfigureAwait(false);

    private static async Task<T> SendAsync<T>(Func<Task<HttpResponseMessage>> send, PaymentFailureReasonCode deterministicFailure, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send().ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false) ?? throw new DownstreamTransientException(PaymentFailureReasonCode.SystemFailure, "Downstream account response was empty.");
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                throw new DownstreamBusinessException(deterministicFailure, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            throw new DownstreamTransientException(PaymentFailureReasonCode.AccountServiceUnavailable, $"Account Service returned {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException exception)
        {
            throw new DownstreamTransientException(PaymentFailureReasonCode.AccountServiceUnavailable, "Account Service request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DownstreamTransientException(PaymentFailureReasonCode.AccountServiceUnavailable, "Account Service is unavailable.", exception);
        }
    }
}

public sealed class HttpLedgerServiceClient : ILedgerServiceClient
{
    private readonly HttpClient _httpClient;
    public HttpLedgerServiceClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<LedgerTransactionClientResponse> PostTransactionAsync(PostLedgerTransactionCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/api/v1/ledger/transactions", command, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LedgerTransactionClientResponse>(cancellationToken).ConfigureAwait(false) ?? throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Ledger response was empty.");
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                throw new DownstreamBusinessException(PaymentFailureReasonCode.SystemFailure, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, $"Ledger Service returned {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException exception)
        {
            throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Ledger Service request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Ledger Service is unavailable.", exception);
        }
    }
}
