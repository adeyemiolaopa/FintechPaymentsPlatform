using Payments.Identity.Application.Contracts;
using Payments.Identity.Domain.Users;

namespace Payments.Identity.Application.Security;

public interface IIdentityService
{
    Task<RegisterUserResponse> RegisterAsync(RegisterUserRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<RefreshTokenResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task LogoutAsync(LogoutRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
}

public interface IPasswordHashingService
{
    string HashPassword(string password);
    bool VerifyPassword(string passwordHash, string password);
    string HashSecret(string secret);
}

public interface ITokenIssuer
{
    IssuedToken IssueAccessToken(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
    IssuedRefreshToken IssueRefreshToken();
}

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record IssuedRefreshToken(string Token, string TokenHash, DateTimeOffset ExpiresAtUtc);
