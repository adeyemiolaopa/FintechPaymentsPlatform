using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.Account.Domain.Accounts;
using Payments.BuildingBlocks.Application.Abstractions;

namespace Payments.Account.Infrastructure.Persistence;

public sealed class AccountDbContext : DbContext, IUnitOfWork
{
    public AccountDbContext(DbContextOptions<AccountDbContext> options) : base(options) { }

    public DbSet<Domain.Accounts.Account> Accounts => Set<Domain.Accounts.Account>();
    public DbSet<FundsReservation> FundsReservations => Set<FundsReservation>();
    public DbSet<AccountRestriction> AccountRestrictions => Set<AccountRestriction>();
    public DbSet<AccountLimit> AccountLimits => Set<AccountLimit>();
    public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();
    public DbSet<CustomerReference> CustomerReferences => Set<CustomerReference>();
    public DbSet<AccountAuditEvent> AccountAuditEvents => Set<AccountAuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var currencyConverter = new ValueConverter<Currency, string>(currency => currency.Code, code => Currency.FromCode(code));
        var accountNumberConverter = new ValueConverter<AccountNumber, string>(number => number.Value, value => AccountNumber.Create(value));

        modelBuilder.HasDefaultSchema("account");
        modelBuilder.Entity<Domain.Accounts.Account>(builder =>
        {
            builder.ToTable("accounts", table =>
            {
                table.HasCheckConstraint("ck_accounts_reserved_non_negative", "\"ReservedBalance\" >= 0");
                table.HasCheckConstraint("ck_accounts_ledger_non_negative", "\"LedgerBalance\" >= 0");
                table.HasCheckConstraint("ck_accounts_available_non_negative", "\"LedgerBalance\" - \"ReservedBalance\" >= 0");
            });
            builder.Ignore(account => account.DomainEvents);
            builder.HasKey(account => account.Id);
            builder.Property(account => account.CustomerId).IsRequired();
            builder.Property(account => account.AccountNumber).HasConversion(accountNumberConverter).HasMaxLength(10).IsRequired();
            builder.Property(account => account.AccountName).HasMaxLength(120).IsRequired();
            builder.Property(account => account.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(account => account.AccountType).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(account => account.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(account => account.LedgerBalance).HasPrecision(19, 4).IsRequired();
            builder.Property(account => account.ReservedBalance).HasPrecision(19, 4).IsRequired();
            builder.Property(account => account.Version).IsConcurrencyToken();
            builder.HasIndex(account => account.AccountNumber).IsUnique();
            builder.HasIndex(account => new { account.CustomerId, account.Currency, account.AccountType }).IsUnique().HasFilter("\"Status\" <> 'Closed'");
        });

        modelBuilder.Entity<FundsReservation>(builder =>
        {
            builder.ToTable("funds_reservations", table => table.HasCheckConstraint("ck_funds_reservations_amount_positive", "\"Amount\" > 0"));
            builder.HasKey(reservation => reservation.Id);
            builder.Property(reservation => reservation.ReferenceId).HasMaxLength(128).IsRequired();
            builder.Property(reservation => reservation.Amount).HasPrecision(19, 4).IsRequired();
            builder.Property(reservation => reservation.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(reservation => new { reservation.AccountId, reservation.ReferenceId }).IsUnique();
            builder.HasIndex(reservation => reservation.Status);
        });

        modelBuilder.Entity<AccountRestriction>(builder =>
        {
            builder.ToTable("account_restrictions");
            builder.HasKey(restriction => restriction.Id);
            builder.Property(restriction => restriction.RestrictionType).HasConversion<string>().HasMaxLength(48).IsRequired();
            builder.Property(restriction => restriction.Reason).HasMaxLength(240).IsRequired();
            builder.HasIndex(restriction => new { restriction.AccountId, restriction.RestrictionType }).HasFilter("\"RemovedAtUtc\" IS NULL");
        });

        modelBuilder.Entity<AccountLimit>(builder =>
        {
            builder.ToTable("account_limits");
            builder.HasKey(limit => limit.Id);
            builder.Property(limit => limit.LimitType).HasConversion<string>().HasMaxLength(48).IsRequired();
            builder.Property(limit => limit.Amount).HasPrecision(19, 4).IsRequired();
            builder.Property(limit => limit.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.HasIndex(limit => new { limit.AccountId, limit.LimitType });
        });

        modelBuilder.Entity<Beneficiary>(builder =>
        {
            builder.ToTable("beneficiaries");
            builder.HasKey(beneficiary => beneficiary.Id);
            builder.Property(beneficiary => beneficiary.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(beneficiary => beneficiary.Name).HasMaxLength(120).IsRequired();
            builder.Property(beneficiary => beneficiary.BankCode).HasMaxLength(12);
            builder.Property(beneficiary => beneficiary.AccountNumber).HasMaxLength(20).IsRequired();
            builder.Property(beneficiary => beneficiary.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(beneficiary => beneficiary.CountryCode).HasMaxLength(2).IsRequired();
            builder.Property(beneficiary => beneficiary.Nickname).HasMaxLength(80);
            builder.Property(beneficiary => beneficiary.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(beneficiary => new { beneficiary.CustomerId, beneficiary.BankCode, beneficiary.AccountNumber, beneficiary.Currency }).IsUnique().HasFilter("\"Status\" = 'Active'");
        });

        modelBuilder.Entity<CustomerReference>(builder =>
        {
            builder.ToTable("customer_references");
            builder.HasKey(reference => reference.CustomerId);
            builder.Property(reference => reference.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(reference => reference.KycStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<AccountAuditEvent>(builder =>
        {
            builder.ToTable("account_audit_events");
            builder.HasKey(audit => audit.Id);
            builder.Property(audit => audit.EventType).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(audit => audit.Reason).HasMaxLength(240);
            builder.Property(audit => audit.Metadata).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.HasIndex(audit => audit.AccountId);
            builder.HasIndex(audit => audit.CustomerId);
            builder.HasIndex(audit => audit.OccurredAtUtc);
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(message => message.Id);
            builder.Property(message => message.Topic).HasMaxLength(160).IsRequired();
            builder.Property(message => message.Key).HasMaxLength(128).IsRequired();
            builder.Property(message => message.EventType).HasMaxLength(128).IsRequired();
            builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(message => message.LastError).HasMaxLength(1024);
            builder.HasIndex(message => message.PublishedAtUtc);
        });

        modelBuilder.Entity<ProcessedIntegrationEvent>(builder =>
        {
            builder.ToTable("processed_integration_events");
            builder.HasKey(processed => processed.EventId);
            builder.Property(processed => processed.EventType).HasMaxLength(128).IsRequired();
        });
    }
}

public sealed class AccountDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AccountDbContext>
{
    public AccountDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ACCOUNT_DATABASE__CONNECTIONSTRING")
            ?? "Host=127.0.0.1;Port=15432;Database=payments_account;Username=payments;Password=change-me-local-only";
        var options = new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(connectionString).Options;
        return new AccountDbContext(options);
    }
}
