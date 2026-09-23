using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Payments.Payment.Application.Payments;
using Payments.Payment.Domain.Payments;

namespace Payments.Payment.Infrastructure.Services;

public sealed class ExternalTransferOptions
{
    public const string SectionName = "ExternalTransfers";
    public Guid DefaultClearingLedgerAccountId { get; init; }
    public int PendingStatusBackoffSeconds { get; init; } = 60;
    public int AmbiguousStatusBackoffSeconds { get; init; } = 120;
    public int CircuitBreakerFailureThreshold { get; init; } = 3;
    public int CircuitBreakerOpenSeconds { get; init; } = 30;
    public IReadOnlyCollection<RailProviderConfiguration> Providers { get; init; } =
    [
        new RailProviderConfiguration
        {
            ProviderName = "SimulatorRail",
            Market = "NG",
            Currency = "NGN",
            SupportedPaymentType = "ExternalBankTransfer",
            Priority = 1,
            Enabled = true,
            BaseUrl = "http://rail-simulator-api:8080",
            ClientId = "fintech-platform",
            ApiKey = "local-rail-api-key",
            ClientSecret = "local-rail-client-secret",
            WebhookSecret = "local-webhook-secret",
            TimeoutSeconds = 5,
            MaxConcurrency = 50
        }
    ];
}

public sealed class RailProviderConfiguration
{
    public string ProviderName { get; init; } = "SimulatorRail";
    public string Market { get; init; } = "NG";
    public string Currency { get; init; } = "NGN";
    public string SupportedPaymentType { get; init; } = "ExternalBankTransfer";
    public int Priority { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = "http://rail-simulator-api:8080";
    public string ClientId { get; init; } = "fintech-platform";
    public string ApiKey { get; init; } = "local-rail-api-key";
    public string ClientSecret { get; init; } = "local-rail-client-secret";
    public string WebhookSecret { get; init; } = "local-webhook-secret";
    public int TimeoutSeconds { get; init; } = 5;
    public int MaxConcurrency { get; init; } = 50;
}

public sealed class ConfiguredRailRouter : IRailRouter
{
    private readonly IEnumerable<IPaymentRailAdapter> _adapters;
    private readonly ExternalTransferOptions _options;

    public ConfiguredRailRouter(IEnumerable<IPaymentRailAdapter> adapters, IOptions<ExternalTransferOptions> options)
    {
        _adapters = adapters;
        _options = options.Value;
    }

    public Task<RailRoute> RouteAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = _options.Providers
            .Where(item => item.Enabled)
            .Where(item => string.Equals(item.Market, instruction.DestinationCountryCode, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.Currency, instruction.Currency, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.Equals(item.SupportedPaymentType, PaymentType.ExternalBankTransfer.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Priority)
            .FirstOrDefault() ?? throw new DownstreamTransientException(PaymentFailureReasonCode.ProviderUnavailable, "No enabled rail provider route is configured for this transfer.");

        var adapter = _adapters.FirstOrDefault(item => string.Equals(item.ProviderName, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
            ?? throw new DownstreamTransientException(PaymentFailureReasonCode.ProviderUnavailable, $"Rail adapter {provider.ProviderName} is not registered.");

        return Task.FromResult(new RailRoute(provider.ProviderName, provider.Market, provider.Currency, provider.SupportedPaymentType, adapter));
    }
}

public sealed class SimulatorRailAdapter : IPaymentRailAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ExternalTransferOptions _options;
    private readonly RailCircuitBreaker _circuitBreaker;

    public SimulatorRailAdapter(IHttpClientFactory httpClientFactory, IOptions<ExternalTransferOptions> options, RailCircuitBreaker circuitBreaker)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _circuitBreaker = circuitBreaker;
    }

    public string ProviderName => "SimulatorRail";

    public async Task<RailSubmissionResult> SubmitTransferAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider();
        if (!_circuitBreaker.CanCall(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds, out var retryAfter))
        {
            return new RailSubmissionResult(RailSubmissionOutcome.Pending, null, "CIRCUIT_OPEN", "Rail provider circuit is open.", DateTimeOffset.UtcNow, retryAfter, "CircuitOpen", "CircuitOpen");
        }

        var body = JsonSerializer.Serialize(new SimulatorTransferRequest(instruction.ClientReference, instruction.DestinationBankCode, instruction.DestinationAccountNumber, instruction.DestinationAccountName, instruction.Amount, instruction.Currency, instruction.Narration, "FINTECHPAY"), JsonOptions);
        var path = "/api/v1/transfers";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            Sign(request, provider, "POST", path, body);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(provider.TimeoutSeconds, 1, 30)));
            using var response = await CreateClient(provider).SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
                return new RailSubmissionResult(RailSubmissionOutcome.Pending, null, "429", "Provider throttled the request.", DateTimeOffset.UtcNow, ReadRetryAfter(response), "Throttled");
            }

            if ((int)response.StatusCode >= 500)
            {
                _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
                return new RailSubmissionResult(RailSubmissionOutcome.Ambiguous, null, ((int)response.StatusCode).ToString(), "Provider returned a server error after submission uncertainty.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.AmbiguousStatusBackoffSeconds), "Http5xx");
            }

            if (!response.IsSuccessStatusCode)
            {
                _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
                return new RailSubmissionResult(RailSubmissionOutcome.Failed, null, ((int)response.StatusCode).ToString(), await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow, null, response.StatusCode.ToString());
            }

            var payload = await response.Content.ReadFromJsonAsync<SimulatorTransferResponse>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? throw new HttpRequestException("Rail simulator response was empty.");
            _circuitBreaker.RecordSuccess(provider.ProviderName);
            return ToSubmissionResult(payload, DateTimeOffset.UtcNow);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
            return new RailSubmissionResult(RailSubmissionOutcome.Ambiguous, null, "TIMEOUT", "Rail request timed out; provider outcome is ambiguous.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.AmbiguousStatusBackoffSeconds), "Timeout", exception.Message);
        }
        catch (HttpRequestException exception)
        {
            _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
            return new RailSubmissionResult(RailSubmissionOutcome.Pending, null, "NETWORK", "Rail provider is unavailable before definitive outcome.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.PendingStatusBackoffSeconds), "NetworkUnavailable", exception.Message);
        }
        catch (JsonException exception)
        {
            _circuitBreaker.RecordFailure(provider.ProviderName, _options.CircuitBreakerFailureThreshold, _options.CircuitBreakerOpenSeconds);
            return new RailSubmissionResult(RailSubmissionOutcome.Ambiguous, null, "MALFORMED", "Rail provider returned malformed data.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.AmbiguousStatusBackoffSeconds), "MalformedResponse", exception.Message);
        }
    }

    public async Task<RailStatusResult> GetTransferStatusAsync(string clientReference, string? providerReference, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider();
        var path = string.IsNullOrWhiteSpace(providerReference)
            ? $"/api/v1/transfers/status?clientReference={Uri.EscapeDataString(clientReference)}"
            : $"/api/v1/transfers/{Uri.EscapeDataString(providerReference)}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            Sign(request, provider, "GET", path, string.Empty);
            using var response = await CreateClient(provider).SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new RailStatusResult(RailSubmissionOutcome.Pending, providerReference, "404", "Provider transfer not visible yet.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.PendingStatusBackoffSeconds), "NotFound");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new RailStatusResult(RailSubmissionOutcome.Ambiguous, providerReference, ((int)response.StatusCode).ToString(), "Provider status query failed.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.AmbiguousStatusBackoffSeconds), response.StatusCode.ToString());
            }

            var payload = await response.Content.ReadFromJsonAsync<SimulatorStatusResponse>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? throw new HttpRequestException("Rail simulator status response was empty.");
            var outcome = Classify(payload.Status, payload.FailureCode);
            return new RailStatusResult(outcome, payload.ProviderReference, payload.FailureCode ?? CodeForStatus(payload.Status), payload.FailureReason ?? payload.Status, DateTimeOffset.UtcNow, outcome is RailSubmissionOutcome.Pending or RailSubmissionOutcome.Ambiguous ? TimeSpan.FromSeconds(_options.PendingStatusBackoffSeconds) : null, payload.Status, null, payload.Amount, payload.Currency, payload.ClientReference);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new RailStatusResult(RailSubmissionOutcome.Ambiguous, providerReference, "STATUS_QUERY_FAILED", "Provider status query could not determine outcome.", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(_options.AmbiguousStatusBackoffSeconds), "StatusQueryFailed", exception.Message);
        }
    }

    public Task<bool> ValidateDestinationAsync(PaymentDestinationRequest destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!string.IsNullOrWhiteSpace(destination.BankCode) && !string.IsNullOrWhiteSpace(destination.AccountNumber) && !string.IsNullOrWhiteSpace(destination.AccountName));
    }

    private static RailSubmissionResult ToSubmissionResult(SimulatorTransferResponse response, DateTimeOffset now)
    {
        var outcome = Classify(response.Status, response.ResponseCode);
        return new RailSubmissionResult(outcome, response.ProviderReference, response.ResponseCode, response.ResponseMessage, now, outcome is RailSubmissionOutcome.Pending or RailSubmissionOutcome.Ambiguous ? TimeSpan.FromMinutes(1) : null, response.Status);
    }

    private static RailSubmissionOutcome Classify(string status, string? code)
        => status switch
        {
            "Successful" => RailSubmissionOutcome.Succeeded,
            "Failed" => RailSubmissionOutcome.Failed,
            "Processing" or "Received" => RailSubmissionOutcome.Pending,
            _ => string.Equals(code, "00", StringComparison.OrdinalIgnoreCase) ? RailSubmissionOutcome.Succeeded : RailSubmissionOutcome.Ambiguous,
        };

    private static string CodeForStatus(string status) => status switch
    {
        "Successful" => "00",
        "Processing" or "Received" => "01",
        "Failed" => "51",
        _ => "UNKNOWN",
    };

    private HttpClient CreateClient(RailProviderConfiguration provider)
    {
        var client = _httpClientFactory.CreateClient("rail-simulator");
        client.BaseAddress = new Uri(provider.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(provider.TimeoutSeconds + 1, 2, 35));
        return client;
    }

    private RailProviderConfiguration GetProvider()
        => _options.Providers.First(provider => string.Equals(provider.ProviderName, ProviderName, StringComparison.OrdinalIgnoreCase));

    private static void Sign(HttpRequestMessage request, RailProviderConfiguration provider, string method, string path, string rawBody)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var canonical = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{rawBody}";
        request.Headers.Add("X-Rail-Client-Id", provider.ClientId);
        request.Headers.Add("X-Rail-Api-Key", provider.ApiKey);
        request.Headers.Add("X-Rail-Timestamp", timestamp);
        request.Headers.Add("X-Rail-Signature", ComputeHmac(provider.ClientSecret, canonical));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        => response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

    private static string ComputeHmac(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private sealed record SimulatorTransferRequest(string ClientReference, string DestinationBankCode, string DestinationAccountNumber, string DestinationAccountName, decimal Amount, string Currency, string? Narration, string SourceInstitution);
    private sealed record SimulatorTransferResponse(string ProviderReference, string ClientReference, string Status, string ResponseCode, string ResponseMessage, decimal Amount, string Currency, DateTimeOffset? ProcessedAtUtc, DateTimeOffset? CompletedAtUtc);
    private sealed record SimulatorStatusResponse(string ProviderReference, string ClientReference, string Status, decimal Amount, string Currency, DateTimeOffset ReceivedAtUtc, DateTimeOffset? ProcessedAtUtc, DateTimeOffset? CompletedAtUtc, string? FailureCode, string? FailureReason);
}

public sealed class RailCallbackAuthenticator : IRailCallbackAuthenticator
{
    private readonly ExternalTransferOptions _options;

    public RailCallbackAuthenticator(IOptions<ExternalTransferOptions> options) => _options = options.Value;

    public bool Validate(RailCallbackAuthenticationInput input)
    {
        var provider = _options.Providers.FirstOrDefault(item => string.Equals(item.ProviderName, input.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null || string.IsNullOrWhiteSpace(input.Timestamp) || string.IsNullOrWhiteSpace(input.Signature)) return false;
        if (!long.TryParse(input.Timestamp, out var seconds)) return false;
        if ((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > TimeSpan.FromMinutes(10)) return false;
        var expected = ComputeHmac(provider.WebhookSecret, $"{input.Timestamp}.{input.RawBody}");
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(input.Signature));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeHmac(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

public sealed class RailCircuitBreaker
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CircuitState> _states = [];

    public bool CanCall(string provider, int threshold, int openSeconds, out TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            retryAfter = null;
            if (!_states.TryGetValue(provider, out var state) || state.OpenUntilUtc is not { } openUntilUtc) return true;
            if (openUntilUtc <= DateTimeOffset.UtcNow)
            {
                _states[provider] = state with { OpenUntilUtc = null, FailureCount = 0 };
                return true;
            }

            retryAfter = openUntilUtc - DateTimeOffset.UtcNow;
            return false;
        }
    }

    public void RecordSuccess(string provider)
    {
        lock (_gate) _states[provider] = new CircuitState(0, null);
    }

    public void RecordFailure(string provider, int threshold, int openSeconds)
    {
        lock (_gate)
        {
            _states.TryGetValue(provider, out var state);
            var failures = (state?.FailureCount ?? 0) + 1;
            var configuredThreshold = Math.Max(1, threshold);
            var configuredOpenSeconds = Math.Max(1, openSeconds);
            _states[provider] = failures >= configuredThreshold ? new CircuitState(failures, DateTimeOffset.UtcNow.AddSeconds(configuredOpenSeconds)) : new CircuitState(failures, null);
        }
    }

    private sealed record CircuitState(int FailureCount, DateTimeOffset? OpenUntilUtc);
}