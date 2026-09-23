using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Domain;
using Payments.RailSimulator.Infrastructure.Persistence;

namespace Payments.RailSimulator.Infrastructure.Services;

public sealed class RailRequestAuthenticator : IRailRequestAuthenticator
{
    private readonly RailSimulatorDbContext _dbContext;
    private readonly RailSimulatorOptions _options;

    public RailRequestAuthenticator(RailSimulatorDbContext dbContext, IOptions<RailSimulatorOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<RailRequestContext> AuthenticateAsync(RailAuthenticationInput input, CancellationToken cancellationToken = default)
    {
        var overrideMode = ParseScenarioOverride(input.ScenarioOverride);
        if (!_options.RequireRequestSignature)
        {
            return new RailRequestContext(string.IsNullOrWhiteSpace(input.ClientId) ? _options.DefaultClientId : input.ClientId.Trim(), overrideMode);
        }

        if (string.IsNullOrWhiteSpace(input.ClientId) || string.IsNullOrWhiteSpace(input.ApiKey) || string.IsNullOrWhiteSpace(input.Timestamp) || string.IsNullOrWhiteSpace(input.Signature))
        {
            throw new RailSimulatorException("rail.authentication", "Missing rail authentication headers.", 401);
        }

        if (!long.TryParse(input.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestampSeconds))
        {
            throw new RailSimulatorException("rail.authentication", "Invalid timestamp.", 401);
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        if (age.Duration() > TimeSpan.FromMinutes(5))
        {
            throw new RailSimulatorException("rail.authentication", "Timestamp is outside the allowed replay window.", 401);
        }

        var client = await _dbContext.ProviderClients
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ClientId == input.ClientId && item.ApiKey == input.ApiKey && item.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (client is null)
        {
            throw new RailSimulatorException("rail.authentication", "Invalid rail client credentials.", 401);
        }

        var canonical = $"{input.Method.ToUpperInvariant()}\n{input.Path}\n{input.Timestamp}\n{input.RawBody}";
        var expected = ComputeHmac(client.Secret, canonical);
        if (!FixedTimeHexEquals(expected, input.Signature))
        {
            throw new RailSimulatorException("rail.authentication", "Invalid request signature.", 401);
        }

        return new RailRequestContext(client.ClientId, overrideMode);
    }

    public string SignCallback(string secret, string timestamp, string rawBody)
        => ComputeHmac(secret, $"{timestamp}.{rawBody}");

    private static RailScenarioMode? ParseScenarioOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<RailScenarioMode>(normalized, true, out var parsed) ? parsed : throw new RailSimulatorException("rail.scenario", "Unsupported scenario override.", 400);
    }

    private static string ComputeHmac(string secret, string canonical)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
