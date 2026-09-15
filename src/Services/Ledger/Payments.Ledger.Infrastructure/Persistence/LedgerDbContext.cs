using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Ledger.Domain.Ledger;

namespace Payments.Ledger.Infrastructure.Persistence;

public sealed class LedgerDbContext : DbContext, IUnitOfWork
{
    public LedgerDbContext(DbContextOptions<LedgerDbContext> options) : base(options) { }

    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<LedgerAccountBalance> BalanceProjections => Set<LedgerAccountBalance>();
    public DbSet<LedgerReversal> Reversals => Set<LedgerReversal>();
    public DbSet<LedgerIdempotencyRecord> IdempotencyRecords => Set<LedgerIdempotencyRecord>();
    public DbSet<LedgerAuditEvent> LedgerAuditEvents => Set<LedgerAuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedIntegrationEvent> ProcessedIntegrationEvents => Set<ProcessedIntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var currencyConverter = new ValueConverter<Currency, string>(currency => currency.Code, code => Currency.FromCode(code));
        modelBuilder.HasDefaultSchema("ledger");

        modelBuilder.Entity<LedgerAccount>(builder =>
        {
            builder.ToTable("ledger_accounts");
            builder.HasKey(account => account.Id);
            builder.Property(account => account.ExternalReference).HasMaxLength(128).IsRequired();
            builder.Property(account => account.AccountCode).HasMaxLength(64).IsRequired();
            builder.Property(account => account.AccountName).HasMaxLength(160).IsRequired();
            builder.Property(account => account.AccountType).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(account => account.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(account => account.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(account => account.ExternalReference).IsUnique();
            builder.HasIndex(account => account.AccountCode).IsUnique();
            builder.HasIndex(account => new { account.Currency, account.AccountType });
        });

        modelBuilder.Entity<LedgerTransaction>(builder =>
        {
            builder.ToTable("ledger_transactions");
            builder.HasKey(transaction => transaction.Id);
            builder.Property(transaction => transaction.ExternalReference).HasMaxLength(128).IsRequired();
            builder.Property(transaction => transaction.TransactionType).HasMaxLength(80).IsRequired();
            builder.Property(transaction => transaction.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(transaction => transaction.Description).HasMaxLength(240).IsRequired();
            builder.Property(transaction => transaction.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(transaction => transaction.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(transaction => transaction.CausationId).HasMaxLength(64);
            builder.Property(transaction => transaction.ReversalReason).HasMaxLength(240);
            builder.HasIndex(transaction => transaction.ExternalReference).IsUnique();
            builder.HasIndex(transaction => transaction.OccurredAtUtc);
            builder.HasIndex(transaction => transaction.CorrelationId);
            builder.Navigation(transaction => transaction.Postings).UsePropertyAccessMode(PropertyAccessMode.Field);
            builder.HasMany(transaction => transaction.Postings).WithOne().HasForeignKey(posting => posting.TransactionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Posting>(builder =>
        {
            builder.ToTable("ledger_postings", table => table.HasCheckConstraint("ck_ledger_postings_amount_positive", "\"Amount\" > 0"));
            builder.HasKey(posting => posting.Id);
            builder.Property(posting => posting.Side).HasConversion<string>().HasMaxLength(16).IsRequired();
            builder.Property(posting => posting.Amount).HasPrecision(19, 4).IsRequired();
            builder.Property(posting => posting.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(posting => posting.Description).HasMaxLength(240);
            builder.HasIndex(posting => new { posting.LedgerAccountId, posting.CreatedAtUtc, posting.Id });
            builder.HasIndex(posting => posting.TransactionId);
            builder.HasIndex(posting => new { posting.TransactionId, posting.Sequence }).IsUnique();
        });

        modelBuilder.Entity<LedgerAccountBalance>(builder =>
        {
            builder.ToTable("ledger_account_balances", table =>
            {
                table.HasCheckConstraint("ck_ledger_balances_debit_non_negative", "\"DebitTotal\" >= 0");
                table.HasCheckConstraint("ck_ledger_balances_credit_non_negative", "\"CreditTotal\" >= 0");
            });
            builder.HasKey(balance => balance.LedgerAccountId);
            builder.Property(balance => balance.Currency).HasConversion(currencyConverter).HasMaxLength(3).IsRequired();
            builder.Property(balance => balance.DebitTotal).HasPrecision(19, 4).IsRequired();
            builder.Property(balance => balance.CreditTotal).HasPrecision(19, 4).IsRequired();
        });

        modelBuilder.Entity<LedgerReversal>(builder =>
        {
            builder.ToTable("ledger_reversals");
            builder.HasKey(reversal => reversal.OriginalTransactionId);
            builder.Property(reversal => reversal.Reason).HasMaxLength(240).IsRequired();
            builder.Property(reversal => reversal.ReversedBy).HasMaxLength(128).IsRequired();
            builder.HasIndex(reversal => reversal.ReversalTransactionId).IsUnique();
        });

        modelBuilder.Entity<LedgerIdempotencyRecord>(builder =>
        {
            builder.ToTable("ledger_idempotency_records");
            builder.HasKey(record => record.IdempotencyKey);
            builder.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
            builder.Property(record => record.RequestHash).HasMaxLength(128).IsRequired();
            builder.HasIndex(record => record.TransactionId).IsUnique();
        });

        modelBuilder.Entity<LedgerAuditEvent>(builder =>
        {
            builder.ToTable("ledger_audit_events");
            builder.HasKey(audit => audit.Id);
            builder.Property(audit => audit.EventType).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(audit => audit.ActorId).HasMaxLength(128);
            builder.Property(audit => audit.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(audit => audit.CausationId).HasMaxLength(64);
            builder.Property(audit => audit.Reason).HasMaxLength(240);
            builder.Property(audit => audit.Metadata).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.HasIndex(audit => audit.TransactionId);
            builder.HasIndex(audit => audit.LedgerAccountId);
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
            builder.HasIndex(processed => processed.EventType);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardImmutableLedgerEntries(ChangeTracker.Entries());
        return base.SaveChangesAsync(cancellationToken);
    }

    private static void GuardImmutableLedgerEntries(IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if ((entry.Entity is Posting or LedgerTransaction) && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new DomainException("ledger.immutable", "Posted ledger transactions and postings are immutable.");
            }
        }
    }
}

public sealed class LedgerDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LEDGER_DATABASE__CONNECTIONSTRING")
            ?? "Host=127.0.0.1;Port=15432;Database=payments_ledger;Username=payments;Password=change-me-local-only";
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(connectionString).Options;
        return new LedgerDbContext(options);
    }
}
