using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Identity.Application.Contracts;
using Payments.Identity.Application.Security;
using Payments.Identity.Domain.Users;
using Payments.Identity.Infrastructure.Persistence;

namespace Payments.Identity.Infrastructure.Security;

public sealed class IdentityAppService : IIdentityService
{
    private const string Topic = "identity.lifecycle.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("Payments.Identity");
    private static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>("auth_login_attempts_total");
    private static readonly Counter<long> LoginSuccesses = Meter.CreateCounter<long>("auth_login_success_total");
    private static readonly Counter<long> LoginFailures = Meter.CreateCounter<long>("auth_login_failure_total");
    private static readonly Counter<long> LockedAccounts = Meter.CreateCounter<long>("auth_locked_accounts_total");
    private static readonly Counter<long> Refreshes = Meter.CreateCounter<long>("auth_refresh_total");
    private static readonly Counter<long> RefreshFailures = Meter.CreateCounter<long>("auth_refresh_failure_total");

    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHashingService _passwordHashing;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly IValidator<RegisterUserRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RefreshTokenRequest> _refreshValidator;
    private readonly ILogger<IdentityAppService> _logger;
    private readonly string _dummyHash;

    public IdentityAppService(
        IdentityDbContext dbContext,
        IPasswordHashingService passwordHashing,
        ITokenIssuer tokenIssuer,
        IClock clock,
        IRequestContext requestContext,
        IValidator<RegisterUserRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<RefreshTokenRequest> refreshValidator,
        ILogger<IdentityAppService> logger)
    {
        _dbContext = dbContext;
        _passwordHashing = passwordHashing;
        _tokenIssuer = tokenIssuer;
        _clock = clock;
        _requestContext = requestContext;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _refreshValidator = refreshValidator;
        _logger = logger;
        _dummyHash = _passwordHashing.HashPassword("DummyPassword123!");
    }

    public async Task<RegisterUserResponse> RegisterAsync(RegisterUserRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await _registerValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var customerId = Guid.NewGuid();
        var user = User.Register(customerId, request.Email, request.PhoneNumber, _passwordHashing.HashPassword(request.Password), now);
        var customerRole = await _dbContext.Roles.SingleAsync(role => role.Id == IdentitySeed.CustomerRoleId, cancellationToken).ConfigureAwait(false);
        user.AssignRole(customerRole);

        var payload = new IdentityUserRegisteredIntegrationEvent(user.Id, user.CustomerId, user.Email.Trim().ToLowerInvariant(), user.PhoneNumber);
        var envelope = new IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent>(
            Guid.NewGuid(),
            IdentityUserRegisteredIntegrationEvent.EventType,
            IdentityUserRegisteredIntegrationEvent.EventVersion,
            now,
            _requestContext.CorrelationId,
            _requestContext.CausationId,
            "identity-service",
            payload);

        _dbContext.Users.Add(user);
        _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("UserRegistered", user.Id, user.Id, customerId, now, _requestContext.CorrelationId, ipAddress, userAgent));
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(Topic, user.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Identity already exists for the supplied email or phone number.");
        }

        _logger.LogInformation("Registered identity user {UserId} for customer {CustomerId}", user.Id, user.CustomerId);
        return new RegisterUserResponse(user.Id, user.CustomerId, user.Status.ToString(), user.CreatedAtUtc);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await _loginValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        LoginAttempts.Add(1);
        var normalizedEmail = User.NormalizeEmail(request.Email);
        var now = _clock.UtcNow;
        var user = await LoadUserByEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            _passwordHashing.VerifyPassword(_dummyHash, request.Password);
            LoginFailures.Add(1);
            throw new InvalidCredentialsException();
        }

        if (!user.CanAuthenticate(now))
        {
            LockedAccounts.Add(1);
            _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("LoginFailed", user.Id, user.Id, user.CustomerId, now, _requestContext.CorrelationId, ipAddress, userAgent, "{\"reason\":\"locked_or_inactive\"}"));
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            throw new LockedIdentityException(user.LockoutEndAtUtc);
        }

        if (!_passwordHashing.VerifyPassword(user.PasswordHash, request.Password))
        {
            user.RecordFailedLogin(now, threshold: 5, window: TimeSpan.FromMinutes(15), lockoutDuration: TimeSpan.FromMinutes(15));
            _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("LoginFailed", user.Id, user.Id, user.CustomerId, now, _requestContext.CorrelationId, ipAddress, userAgent));
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            LoginFailures.Add(1);
            if (!user.CanAuthenticate(now))
            {
                LockedAccounts.Add(1);
            }

            throw new InvalidCredentialsException();
        }

        user.RecordSuccessfulLogin(now);
        var response = IssueTokens(user, ipAddress, now);
        _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("LoginSucceeded", user.Id, user.Id, user.CustomerId, now, _requestContext.CorrelationId, ipAddress, userAgent));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LoginSuccesses.Add(1);
        _logger.LogInformation("Identity login succeeded for user {UserId}", user.Id);
        return response;
    }

    public async Task<RefreshTokenResponse> RefreshAsync(RefreshTokenRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await _refreshValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        Refreshes.Add(1);
        var now = _clock.UtcNow;
        var tokenHash = _passwordHashing.HashSecret(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            RefreshFailures.Add(1);
            throw new InvalidCredentialsException();
        }

        var user = await LoadUserByIdAsync(existing.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidCredentialsException();

        if (!existing.IsActive(now))
        {
            if (existing.IsRevoked && existing.ReplacedByTokenId is not null)
            {
                existing.MarkReuseDetected(now, ipAddress);
                var family = await _dbContext.RefreshTokens.Where(token => token.FamilyId == existing.FamilyId).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var token in family)
                {
                    token.Revoke(now, ipAddress);
                }
            }

            _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("RefreshTokenRejected", user.Id, user.Id, user.CustomerId, now, _requestContext.CorrelationId, ipAddress, userAgent));
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            RefreshFailures.Add(1);
            throw new InvalidCredentialsException();
        }

        var refresh = _tokenIssuer.IssueRefreshToken();
        var replacement = user.IssueRefreshToken(refresh.TokenHash, ipAddress, now, refresh.ExpiresAtUtc, existing.FamilyId);
        _dbContext.RefreshTokens.Add(replacement);
        existing.Revoke(now, ipAddress, replacement.Id);
        var accessToken = _tokenIssuer.IssueAccessToken(user, GetRoles(user), GetPermissions(user));
        _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("RefreshTokenIssued", user.Id, user.Id, user.CustomerId, now, _requestContext.CorrelationId, ipAddress, userAgent));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new RefreshTokenResponse(accessToken.Token, refresh.Token, accessToken.ExpiresAtUtc, refresh.ExpiresAtUtc);
    }

    public async Task LogoutAsync(LogoutRequest request, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var tokenHash = _passwordHashing.HashSecret(request.RefreshToken);
        var token = await _dbContext.RefreshTokens.SingleOrDefaultAsync(refreshToken => refreshToken.TokenHash == tokenHash, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return;
        }

        token.Revoke(now, ipAddress);
        _dbContext.SecurityAuditEvents.Add(SecurityAuditEvent.Create("RefreshTokenRevoked", token.UserId, token.UserId, null, now, _requestContext.CorrelationId, ipAddress, userAgent));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private LoginResponse IssueTokens(User user, string? ipAddress, DateTimeOffset now)
    {
        var access = _tokenIssuer.IssueAccessToken(user, GetRoles(user), GetPermissions(user));
        var refresh = _tokenIssuer.IssueRefreshToken();
        var refreshToken = user.IssueRefreshToken(refresh.TokenHash, ipAddress, now, refresh.ExpiresAtUtc);
        _dbContext.RefreshTokens.Add(refreshToken);
        return new LoginResponse(access.Token, refresh.Token, access.ExpiresAtUtc, refresh.ExpiresAtUtc);
    }

    private async Task<User?> LoadUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => await _dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ThenInclude(role => role.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission)
            .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

    private async Task<User?> LoadUserByIdAsync(Guid userId, CancellationToken cancellationToken)
        => await _dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .ThenInclude(role => role.RolePermissions)
            .ThenInclude(rolePermission => rolePermission.Permission)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            .ConfigureAwait(false);

    private static IReadOnlyCollection<string> GetRoles(User user) => user.UserRoles.Select(userRole => userRole.Role.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyCollection<string> GetPermissions(User user) => user.UserRoles.SelectMany(userRole => userRole.Role.RolePermissions).Select(rolePermission => rolePermission.Permission.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

