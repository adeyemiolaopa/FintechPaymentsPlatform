using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Payments.BuildingBlocks.Application.Abstractions;

namespace Payments.BuildingBlocks.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "FintechPaymentsPlatform";
    public string Audience { get; init; } = "FintechPaymentsPlatform.Api";
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 10;
    public int RefreshTokenDays { get; init; } = 14;
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUser _currentUser;

    public PermissionAuthorizationHandler(ICurrentUser currentUser) => _currentUser = currentUser;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (_currentUser.IsAuthenticated && _currentUser.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddJwtBearerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrWhiteSpace(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException("JWT signing key must be supplied through configuration and be at least 32 bytes.");
        }

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(jwt => !string.IsNullOrWhiteSpace(jwt.Issuer), "JWT issuer is required")
            .Validate(jwt => !string.IsNullOrWhiteSpace(jwt.Audience), "JWT audience is required")
            .Validate(jwt => Encoding.UTF8.GetByteCount(jwt.SigningKey) >= 32, "JWT signing key must be at least 32 bytes")
            .Validate(jwt => jwt.AccessTokenMinutes is >= 5 and <= 15, "Access token lifetime must be between 5 and 15 minutes")
            .ValidateOnStart();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.RequireHttpsMetadata = false;
                jwt.SaveToken = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

        return services;
    }

    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services, params string[] permissions)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
            }
        });
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
