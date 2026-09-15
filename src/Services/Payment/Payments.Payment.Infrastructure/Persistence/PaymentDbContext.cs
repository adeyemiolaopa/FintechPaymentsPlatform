using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Payment.Domain.Payments;

namespace Payments.Payment.Infrastructure.Persistence;

public sealed class PaymentDbContext : DbContext, IUnitOfWork
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Domain.Payments.Payment> Payments => Set<Domain.Payments.Payment>();
    public DbSet<PaymentStateTransition> PaymentStateTransitions => Set<PaymentStateTransition>();
    public DbSet<PaymentAuditEvent> PaymentAuditEvents => Set<PaymentAuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<CustomerReference> CustomerReferences => Set<CustomerReference>();
    public DbSet<AccountReference> AccountReferences => Set<AccountReference>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var currencyConverter = new ValueConverter<Currency, string>(currency => currency.Code, code => Currency.FromCode(code));
        modelBuilder.ConfigureInboxMessages();
        modelBuilder.HasDefaultSchema("payment");

        modelBuilder.Entity<Domain.Payments.Payment>(builder =>
        {
            builder.ToTable("payments", table => table.HasCheckConstraint("ck_payments_amount_positive", "\"Amount\" > 0"));
            builder.HasKey(payment => payment.Id);
            builder.Property(payment => payment.CustomerId).IsRequired();
            builder.Property(payment => payment.SourceAccountId).IsRequired();
            builder.Property(payment => payment.PaymentType).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(payment => payment.Amount).HasPrecision(19, 4).IsRequired();
            builder.Property(payment => payment.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(payment => payment.Reference).HasMaxLength(32).IsRequired();
            builder.Property(payment => payment.Description).HasMaxLength(240);
            builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(payment => payment.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(payment => payment.ExternalReference).HasMaxLength(128).IsRequired();
            builder.Property(payment => payment.ReasonCode).HasConversion<string>().HasMaxLength(80);
            builder.Property(payment => payment.ReasonDescription).HasMaxLength(240);
            builder.Property(payment => payment.Version).IsRequired();
            builder.HasIndex(payment => payment.Reference).IsUnique();
            builder.HasIndex(payment => payment.ExternalReference).IsUnique();
            builder.HasIndex(payment => new { payment.CustomerId, payment.CreatedAtUtc });
            builder.HasIndex(payment => payment.Status);
            builder.HasIndex(payment => payment.CreatedAtUtc);
            builder.HasIndex(payment => new { payment.Status, payment.UpdatedAtUtc });
            builder.Ignore(payment => payment.StateTransitions);
            builder.OwnsOne(payment => payment.Destination, destination =>
            {
                destination.Property(item => item.DestinationType).HasConversion<string>().HasMaxLength(40).HasColumnName("DestinationType").IsRequired();
                destination.Property(item => item.AccountId).HasColumnName("DestinationAccountId");
                destination.Property(item => item.BankCode).HasMaxLength(12).HasColumnName("DestinationBankCode");
                destination.Property(item => item.AccountNumber).HasMaxLength(32).HasColumnName("DestinationAccountNumber");
                destination.Property(item => item.AccountName).HasMaxLength(120).HasColumnName("DestinationAccountName");
                destination.Property(item => item.CountryCode).HasMaxLength(2).HasColumnName("DestinationCountryCode");
            });
        });

        modelBuilder.Entity<PaymentStateTransition>(builder =>
        {
            builder.ToTable("payment_state_transitions");
            builder.HasKey(transition => transition.Id);
            builder.Property(transition => transition.FromStatus).HasConversion<string>().HasMaxLength(40);
            builder.Property(transition => transition.ToStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(transition => transition.ReasonCode).HasConversion<string>().HasMaxLength(80);
            builder.Property(transition => transition.ReasonDescription).HasMaxLength(240);
            builder.Property(transition => transition.ActorType).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(transition => transition.ActorId).HasMaxLength(128).IsRequired();
            builder.Property(transition => transition.CorrelationId).HasMaxLength(64).IsRequired();
            builder.HasIndex(transition => new { transition.PaymentId, transition.OccurredAtUtc });
        });

        modelBuilder.Entity<PaymentAuditEvent>(builder =>
        {
            builder.ToTable("payment_audit_events");
            builder.HasKey(audit => audit.Id);
            builder.Property(audit => audit.EventType).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.ActorType).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(audit => audit.ActorId).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(audit => audit.Reason).HasMaxLength(240);
            builder.Property(audit => audit.Metadata).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.HasIndex(audit => new { audit.PaymentId, audit.OccurredAtUtc });
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

        modelBuilder.Entity<CustomerReference>(builder =>
        {
            builder.ToTable("customer_references");
            builder.HasKey(reference => reference.CustomerId);
            builder.Property(reference => reference.Status).HasMaxLength(40).IsRequired();
            builder.Property(reference => reference.KycStatus).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<AccountReference>(builder =>
        {
            builder.ToTable("account_references");
            builder.HasKey(reference => reference.AccountId);
            builder.Property(reference => reference.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(reference => reference.AccountType).HasMaxLength(40).IsRequired();
            builder.Property(reference => reference.Status).HasMaxLength(40).IsRequired();
            builder.HasIndex(reference => reference.CustomerId);
            builder.HasIndex(reference => reference.LedgerAccountId).IsUnique().HasFilter("\"LedgerAccountId\" IS NOT NULL");
        });


        modelBuilder.Entity<IdempotencyRecord>(builder =>
        {
            builder.ToTable("payment_idempotency_records");
            builder.HasKey(record => record.Id);
            builder.Property(record => record.OperationType).HasMaxLength(80).IsRequired();
            builder.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
            builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
            builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(record => record.ResourceType).HasMaxLength(80);
            builder.Property(record => record.ResponseBody).HasColumnType("jsonb");
            builder.HasIndex(record => new { record.CustomerId, record.OperationType, record.IdempotencyKey }).IsUnique();
            builder.HasIndex(record => new { record.Status, record.UpdatedAtUtc });
            builder.HasIndex(record => record.ExpiresAtUtc);
            builder.HasIndex(record => record.ResourceId);
        });
        modelBuilder.Entity<ProcessedIntegrationEvent>(builder =>
        {
            builder.ToTable("processed_integration_events");
            builder.HasKey(processed => processed.EventId);
            builder.Property(processed => processed.EventType).HasMaxLength(128).IsRequired();
            builder.HasIndex(processed => processed.EventType);
        });
    }
}

public sealed class PaymentDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_DATABASE__CONNECTIONSTRING")
            ?? "Host=127.0.0.1;Port=15432;Database=payments_payment;Username=payments;Password=change-me-local-only";
        var options = new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(connectionString).Options;
        return new PaymentDbContext(options);
    }
}
