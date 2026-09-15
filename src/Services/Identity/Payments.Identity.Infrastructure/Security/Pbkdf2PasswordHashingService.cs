using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Payments.Identity.Application.Security;

namespace Payments.Identity.Infrastructure.Security;

public sealed class PasswordHashingOptions
{
    public const string SectionName = "PasswordHashing";

    public int Iterations { get; init; } = 210_000;
    public int SaltBytes { get; init; } = 16;
    public int HashBytes { get; init; } = 32;
}

public sealed class Pbkdf2PasswordHashingService : IPasswordHashingService
{
    private const string Version = "PBKDF2-SHA256";
    private readonly PasswordHashingOptions _options;

    public Pbkdf2PasswordHashingService(IOptions<PasswordHashingOptions> options) => _options = options.Value;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(_options.SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _options.Iterations, HashAlgorithmName.SHA256, _options.HashBytes);
        return string.Join('$', Version, _options.Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string passwordHash, string password)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != Version || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public string HashSecret(string secret)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash);
    }
}
