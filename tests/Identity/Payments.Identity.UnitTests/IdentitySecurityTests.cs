using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Infrastructure.Security;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.Identity.Application.Contracts;
using Payments.Identity.Application.Security;
using Payments.Identity.Domain.Users;
using Payments.Identity.Infrastructure.Security;
using System.IdentityModel.Tokens.Jwt;

namespace Payments.Identity.UnitTests;

public sealed class IdentitySecurityTests
{
    [Fact]
    public void Password_policy_rejects_weak_passwords()
    {
        var validator = new RegisterUserRequestValidator();
        var result = validator.Validate(new RegisterUserRequest("customer@example.com", "+2348012345678", "weak"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Password_hash_is_salted_and_does_not_store_plaintext()
    {
        var hasher = new Pbkdf2PasswordHashingService(Options.Create(new PasswordHashingOptions { Iterations = 100_000 }));
        var first = hasher.HashPassword("StrongPassword123!");
        var second = hasher.HashPassword("StrongPassword123!");
        first.Should().NotBe("StrongPassword123!");
        first.Should().NotBe(second);
        hasher.VerifyPassword(first, "StrongPassword123!").Should().BeTrue();
        hasher.VerifyPassword(first, "WrongPassword123!").Should().BeFalse();
    }

    [Fact]
    public void Jwt_contains_minimal_identity_and_authorization_claims()
    {
        var user = User.Register(Guid.NewGuid(), "customer@example.com", "+2348012345678", "hash", DateTimeOffset.UtcNow);
        var issuer = new JwtTokenIssuer(Options.Create(new JwtOptions
        {
            Issuer = "FintechPaymentsPlatform",
            Audience = "FintechPaymentsPlatform.Api",
            SigningKey = "unit-test-signing-key-that-is-at-least-32-bytes",
            AccessTokenMinutes = 10,
            RefreshTokenDays = 14,
        }), new SystemClock());

        var token = issuer.IssueAccessToken(user, ["Customer"], ["customer.read.self"]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        jwt.Claims.Should().Contain(claim => claim.Type == "sub" && claim.Value == user.Id.ToString("D"));
        jwt.Claims.Should().Contain(claim => claim.Type == "customer_id" && claim.Value == user.CustomerId.ToString("D"));
        jwt.Claims.Should().Contain(claim => claim.Type == "permission" && claim.Value == "customer.read.self");
        jwt.Claims.Should().NotContain(claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
        token.ExpiresAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void User_lockout_uses_explicit_status_not_boolean_flags()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Register(Guid.NewGuid(), "customer@example.com", "+2348012345678", "hash", now);
        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedLogin(now.AddMinutes(i), 5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }

        user.Status.Should().Be(UserStatus.Locked);
        user.CanAuthenticate(now.AddMinutes(5)).Should().BeFalse();
    }
}
