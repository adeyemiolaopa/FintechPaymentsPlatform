using System.Net;
using System.Net.Http.Json;
using Payments.Reconciliation.Application;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationSourceOptions
{
    public string PaymentBaseUrl { get; set; } = "http://payment-api:8080";
    public string LedgerBaseUrl { get; set; } = "http://ledger-api:8080";
    public string SharedKey { get; set; } = "";
}

public sealed class HttpReconciliationSources(IHttpClientFactory clients, Microsoft.Extensions.Options.IOptions<ReconciliationSourceOptions> options) : IReconciliationSources
{
    private readonly ReconciliationSourceOptions _options = options.Value;

    public async Task<PaymentEvidence?> FindPaymentAsync(string provider, string? providerReference, string? clientReference, CancellationToken ct)
    {
        var path = $"/internal/reconciliation/payments?provider={Uri.EscapeDataString(provider)}&providerReference={Uri.EscapeDataString(providerReference ?? "")}&clientReference={Uri.EscapeDataString(clientReference ?? "")}";
        return await GetAsync<PaymentEvidence>("payment", path, ct).ConfigureAwait(false);
    }

    public Task<PaymentEvidence?> GetPaymentAsync(Guid paymentId, CancellationToken ct)
        => GetAsync<PaymentEvidence>("payment", $"/internal/reconciliation/payments/{paymentId:D}", ct);

    public async Task<IReadOnlyList<PaymentEvidence>> PendingPaymentsAsync(int offset, int limit, CancellationToken ct)
        => await GetAsync<List<PaymentEvidence>>("payment", $"/internal/reconciliation/pending?offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 500)}", ct).ConfigureAwait(false) ?? [];
    public async Task<LedgerEvidence?> FindLedgerAsync(PaymentEvidence payment, CancellationToken ct)
    {
        var path = payment.LedgerTransactionId is { } transactionId
            ? $"/internal/reconciliation/transactions/{transactionId:D}"
            : $"/internal/reconciliation/transactions/by-reference?externalReference={Uri.EscapeDataString(payment.PaymentId.ToString("D"))}";
        var transaction = await GetAsync<LedgerTransactionDto>("ledger", path, ct).ConfigureAwait(false);
        if (transaction is null) return null;
        var debit = transaction.Postings.Where(x => x.Side.Equals("Debit", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
        return new LedgerEvidence(transaction.TransactionId, transaction.ExternalReference, transaction.Status, debit, transaction.Currency, DateTimeOffset.UtcNow);
    }

    public async Task<ProviderEvidence?> QueryProviderAsync(PaymentEvidence payment, CancellationToken ct)
    {
        var result = await GetAsync<ProviderEvidence>("payment", $"/internal/reconciliation/payments/{payment.PaymentId:D}/provider", ct).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<PaymentEvidence>> ExpectedSettlementsAsync(string provider, DateOnly date, CancellationToken ct)
    {
        var all = new List<PaymentEvidence>();
        while (true)
        {
            var path = $"/internal/reconciliation/expected?provider={Uri.EscapeDataString(provider)}&settlementDate={date:yyyy-MM-dd}&offset={all.Count}&limit=500";
            var page = await GetAsync<List<PaymentEvidence>>("payment", path, ct).ConfigureAwait(false) ?? [];
            all.AddRange(page);
            if (page.Count < 500) return all;
        }
    }

    public async Task<PaymentEvidence?> RequestPaymentRecoveryAsync(Guid paymentId, CancellationToken ct)
    {
        using var client = Create("payment");
        using var response = await client.PostAsync($"/internal/reconciliation/payments/{paymentId:D}/recover", null, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await GetAsync<PaymentEvidence>("payment", $"/internal/reconciliation/payments/{paymentId:D}", ct).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string name, string path, CancellationToken ct)
    {
        using var client = Create(name);
        using var response = await client.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
    }

    private HttpClient Create(string name)
    {
        if (string.IsNullOrWhiteSpace(_options.SharedKey)) throw new InvalidOperationException("Internal reconciliation shared key is required.");
        var client = clients.CreateClient(name);
        client.BaseAddress = new Uri(name == "payment" ? _options.PaymentBaseUrl : _options.LedgerBaseUrl);
        client.DefaultRequestHeaders.Add("X-Reconciliation-Key", _options.SharedKey);
        return client;
    }

    private sealed record LedgerPostingDto(string Side, decimal Amount);
    private sealed record LedgerTransactionDto(Guid TransactionId, string ExternalReference, string Status, string Currency, IReadOnlyList<LedgerPostingDto> Postings);
}
