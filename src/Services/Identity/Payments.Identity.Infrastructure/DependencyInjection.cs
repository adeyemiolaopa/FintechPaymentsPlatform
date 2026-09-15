using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Identity.Application.Security;
using Payments.Identity.Infrastructure.Messaging;
using Payments.Identity.Infrastructure.Persistence;
using Payments.Identity.Infrastructure.Security;

namespace Payments.Identity.Infrastructure;

public sealed class IdentityDatabaseOptions
{
    public const string SectionName = "IdentityDatabase";

    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_identity;Username=payments;Password=change-me-local-only";
}

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityDatabaseOptions>().Bind(configuration.GetSection(IdentityDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Identity database connection string is required").ValidateOnStart();
        services.AddOptions<PasswordHashingOptions>().Bind(configuration.GetSection(PasswordHashingOptions.SectionName)).Validate(options => options.Iterations >= 100_000, "PBKDF2 iterations must be production-oriented").ValidateOnStart();
        services.AddOptions<IdentityOutboxOptions>().Bind(configuration.GetSection(IdentityOutboxOptions.SectionName));
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName));

        var database = configuration.GetSection(IdentityDatabaseOptions.SectionName).Get<IdentityDatabaseOptions>() ?? new IdentityDatabaseOptions();
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<IPasswordHashingService, Pbkdf2PasswordHashingService>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IIdentityService, IdentityAppService>();
        services.AddHostedService<IdentityOutboxPublisher>();
        return services;
    }
}
