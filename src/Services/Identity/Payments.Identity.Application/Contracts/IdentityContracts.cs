namespace Payments.Identity.Application.Contracts;

public sealed record RegisterUserRequest(string Email, string PhoneNumber, string Password);

public sealed record RegisterUserResponse(Guid UserId, Guid CustomerId, string Status, DateTimeOffset CreatedAtUtc);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, DateTimeOffset RefreshTokenExpiresAtUtc, string TokenType = "Bearer");

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RefreshTokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc, DateTimeOffset RefreshTokenExpiresAtUtc, string TokenType = "Bearer");

public sealed record LogoutRequest(string RefreshToken);
