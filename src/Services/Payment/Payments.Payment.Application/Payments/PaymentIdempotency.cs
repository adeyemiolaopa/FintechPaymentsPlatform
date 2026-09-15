using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Payments.Payment.Application.Payments;

public static partial class PaymentOperationTypes
{
    public const string CreatePayment = "CreatePayment";
}

public sealed record PaymentCreationResult(PaymentResponse Payment, bool Replayed, int StatusCode);

public static partial class IdempotencyKeyRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new IdempotencyKeyValidationException("Idempotency-Key header is required.");
        var normalized = value.Trim();
        if (normalized.Length is < MinLength or > MaxLength) throw new IdempotencyKeyValidationException($"Idempotency-Key must be between {MinLength} and {MaxLength} characters.");
        if (!ValidKeyRegex().IsMatch(normalized)) throw new IdempotencyKeyValidationException("Idempotency-Key contains unsupported characters.");
        return normalized;
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.Compiled)]
    private static partial Regex ValidKeyRegex();
}

public sealed class IdempotencyKeyValidationException : Exception
{
    public IdempotencyKeyValidationException(string message) : base(message) { }
}

public sealed class IdempotencyKeyConflictException : Exception
{
    public IdempotencyKeyConflictException() : base("This idempotency key has already been used with a different request.") { }
}

public static class PaymentRequestHasher
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static string ComputeHash(CreatePaymentRequest request)
    {
        var canonical = CanonicalPaymentRequest.From(request);
        var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ToCanonicalJson(CreatePaymentRequest request)
        => JsonSerializer.Serialize(CanonicalPaymentRequest.From(request), CanonicalJsonOptions);

    private sealed record CanonicalPaymentRequest(
        string SourceAccountId,
        string Type,
        string Amount,
        string Currency,
        CanonicalPaymentDestination Destination,
        string? Description)
    {
        public static CanonicalPaymentRequest From(CreatePaymentRequest request)
        {
            var type = Enum.Parse<Domain.Payments.PaymentType>(request.Type, true).ToString();
            return new CanonicalPaymentRequest(
                request.SourceAccountId.ToString("D"),
                type,
                decimal.Round(request.Amount, 4, MidpointRounding.ToZero).ToString("0.####", CultureInfo.InvariantCulture),
                request.Currency.Trim().ToUpperInvariant(),
                CanonicalPaymentDestination.From(request.Destination),
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());
        }
    }

    private sealed record CanonicalPaymentDestination(
        string? AccountId,
        string? BankCode,
        string? AccountNumber,
        string? AccountName,
        string? CountryCode)
    {
        public static CanonicalPaymentDestination From(PaymentDestinationRequest destination)
            => new(
                destination.AccountId?.ToString("D"),
                NormalizeUpper(destination.BankCode),
                string.IsNullOrWhiteSpace(destination.AccountNumber) ? null : destination.AccountNumber.Trim(),
                string.IsNullOrWhiteSpace(destination.AccountName) ? null : destination.AccountName.Trim(),
                NormalizeUpper(destination.CountryCode));

        private static string? NormalizeUpper(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}
