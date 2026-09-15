using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Infrastructure.Security;
using Payments.Identity.Application.Security;
using Payments.Identity.Domain.Users;

namespace Payments.Identity.Infrastructure.Security;

public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public IssuedToken IssueAccessToken(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("customer_id", user.CustomerId.ToString("D")),
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, now.UtcDateTime, expires.UtcDateTime, credentials);
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public IssuedRefreshToken IssueRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
        return new IssuedRefreshToken(token, hash, _clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }
}
