using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Customer.Application.Customers;
using Payments.Customer.Infrastructure.Messaging;
using Payments.Customer.Infrastructure.Persistence;

namespace Payments.Customer.Infrastructure;

public sealed class CustomerDatabaseOptions
{
    public const string SectionName = "CustomerDatabase";

    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_customer;Username=payments;Password=change-me-local-only";
}

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CustomerDatabaseOptions>().Bind(configuration.GetSection(CustomerDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Customer database connection string is required").ValidateOnStart();
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName));
        services.AddOptions<CustomerConsumerOptions>().Bind(configuration.GetSection(CustomerConsumerOptions.SectionName));
        services.AddOptions<CustomerOutboxOptions>().Bind(configuration.GetSection(CustomerOutboxOptions.SectionName));
        var database = configuration.GetSection(CustomerDatabaseOptions.SectionName).Get<CustomerDatabaseOptions>() ?? new CustomerDatabaseOptions();
        services.AddDbContext<CustomerDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerProfileService, CustomerProfileService>();
        services.AddScoped<IdentityUserRegisteredHandler>();
        services.AddHostedService<IdentityUserRegisteredConsumer>();
        services.AddHostedService<CustomerOutboxPublisher>();
        return services;
    }
}
