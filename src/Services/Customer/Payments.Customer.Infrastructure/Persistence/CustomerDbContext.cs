using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Customer.Domain.Customers;

namespace Payments.Customer.Infrastructure.Persistence;

public sealed class CustomerDbContext : DbContext, IUnitOfWork
{
    public CustomerDbContext(DbContextOptions<CustomerDbContext> options) : base(options) { }

    public DbSet<Customer.Domain.Customers.Customer> Customers => Set<Customer.Domain.Customers.Customer>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();
    public DbSet<CustomerAuditEvent> CustomerAuditEvents => Set<CustomerAuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureInboxMessages();
        modelBuilder.HasDefaultSchema("customer");
        modelBuilder.Entity<Customer.Domain.Customers.Customer>(builder =>
        {
            builder.ToTable("customers");
            builder.Ignore(customer => customer.DomainEvents);
            builder.HasKey(customer => customer.Id);
            builder.Property(customer => customer.Email).HasMaxLength(254).IsRequired();
            builder.Property(customer => customer.PhoneNumber).HasMaxLength(32).IsRequired();
            builder.Property(customer => customer.FirstName).HasMaxLength(80);
            builder.Property(customer => customer.MiddleName).HasMaxLength(80);
            builder.Property(customer => customer.LastName).HasMaxLength(80);
            builder.Property(customer => customer.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(customer => customer.KycStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.OwnsOne(customer => customer.Address, address =>
            {
                address.Property(value => value.Line1).HasColumnName("address_line1").HasMaxLength(160);
                address.Property(value => value.Line2).HasColumnName("address_line2").HasMaxLength(160);
                address.Property(value => value.City).HasColumnName("address_city").HasMaxLength(80);
                address.Property(value => value.Region).HasColumnName("address_region").HasMaxLength(80);
                address.Property(value => value.CountryCode).HasColumnName("address_country_code").HasMaxLength(2);
                address.Property(value => value.PostalCode).HasColumnName("address_postal_code").HasMaxLength(32);
            });
            builder.HasIndex(customer => customer.IdentityUserId).IsUnique();
            builder.HasIndex(customer => customer.Email);
            builder.HasIndex(customer => customer.PhoneNumber);
        });

        modelBuilder.Entity<ProcessedIntegrationEvent>(builder =>
        {
            builder.ToTable("processed_integration_events");
            builder.HasKey(processed => processed.EventId);
            builder.Property(processed => processed.EventType).HasMaxLength(128).IsRequired();
            builder.HasIndex(processed => processed.EventType);
        });


        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(message => message.Id);
            builder.Property(message => message.EventId).IsRequired();
            builder.Property(message => message.EventVersion).IsRequired();
            builder.Property(message => message.AggregateType).HasMaxLength(80).IsRequired();
            builder.Property(message => message.AggregateId).HasMaxLength(128).IsRequired();
            builder.Property(message => message.Topic).HasMaxLength(160).IsRequired();
            builder.Property(message => message.Key).HasMaxLength(128).IsRequired();
            builder.Property(message => message.PartitionKey).HasMaxLength(128).IsRequired();
            builder.Property(message => message.EventType).HasMaxLength(128).IsRequired();
            builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(message => message.Headers).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(message => message.LastError).HasMaxLength(1024);
            builder.HasIndex(message => message.EventId).IsUnique();
            builder.HasIndex(message => message.PublishedAtUtc);
            builder.HasIndex(message => new { message.Status, message.NextAttemptAtUtc });
            builder.HasIndex(message => new { message.Topic, message.PartitionKey });
        });
        modelBuilder.Entity<CustomerAuditEvent>(builder =>
        {
            builder.ToTable("customer_audit_events");
            builder.HasKey(audit => audit.Id);
            builder.Property(audit => audit.EventType).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(audit => audit.Metadata).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.HasIndex(audit => audit.TargetCustomerId);
            builder.HasIndex(audit => audit.OccurredAtUtc);
        });
    }
}

public sealed class CustomerDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<CustomerDbContext>
{
    public CustomerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CUSTOMER_DATABASE__CONNECTIONSTRING")
            ?? "Host=127.0.0.1;Port=15432;Database=payments_customer;Username=payments;Password=change-me-local-only";
        var options = new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString).Options;
        return new CustomerDbContext(options);
    }
}

