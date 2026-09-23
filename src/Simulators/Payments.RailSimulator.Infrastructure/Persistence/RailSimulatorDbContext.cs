using Microsoft.EntityFrameworkCore;
using Payments.RailSimulator.Domain;

namespace Payments.RailSimulator.Infrastructure.Persistence;

public sealed class RailSimulatorDbContext : DbContext
{
    public RailSimulatorDbContext(DbContextOptions<RailSimulatorDbContext> options) : base(options) { }

    public DbSet<RailTransfer> RailTransfers => Set<RailTransfer>();
    public DbSet<CallbackAttempt> CallbackAttempts => Set<CallbackAttempt>();
    public DbSet<ProviderScenario> ProviderScenarios => Set<ProviderScenario>();
    public DbSet<CallbackConfiguration> CallbackConfigurations => Set<CallbackConfiguration>();
    public DbSet<SettlementRecord> SettlementRecords => Set<SettlementRecord>();
    public DbSet<ProviderClient> ProviderClients => Set<ProviderClient>();
    public DbSet<RequestAuditEvent> RequestAuditEvents => Set<RequestAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("rail");

        modelBuilder.Entity<RailTransfer>(entity =>
        {
            entity.ToTable("rail_transfers");
            entity.HasKey(transfer => transfer.Id);
            entity.HasIndex(transfer => transfer.ProviderReference).IsUnique();
            entity.HasIndex(transfer => new { transfer.ClientId, transfer.ClientReference }).IsUnique();
            entity.Property(transfer => transfer.ProviderReference).HasMaxLength(64).IsRequired();
            entity.Property(transfer => transfer.ClientId).HasMaxLength(80).IsRequired();
            entity.Property(transfer => transfer.ClientReference).HasMaxLength(128).IsRequired();
            entity.Property(transfer => transfer.SourceInstitution).HasMaxLength(32).IsRequired();
            entity.Property(transfer => transfer.DestinationBankCode).HasMaxLength(12).IsRequired();
            entity.Property(transfer => transfer.DestinationAccountNumberMasked).HasMaxLength(32).IsRequired();
            entity.Property(transfer => transfer.DestinationAccountName).HasMaxLength(160).IsRequired();
            entity.Property(transfer => transfer.Amount).HasPrecision(18, 4);
            entity.Property(transfer => transfer.Currency).HasMaxLength(3).IsRequired();
            entity.Property(transfer => transfer.Narration).HasMaxLength(240);
            entity.Property(transfer => transfer.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(transfer => transfer.ConfiguredScenario).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(transfer => transfer.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(transfer => transfer.FailureCode).HasMaxLength(16);
            entity.Property(transfer => transfer.FailureReason).HasMaxLength(240);
            entity.Property(transfer => transfer.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<CallbackAttempt>(entity =>
        {
            entity.ToTable("callback_attempts");
            entity.HasKey(callback => callback.Id);
            entity.HasIndex(callback => new { callback.RailTransferId, callback.CallbackEventId, callback.AttemptNumber }).IsUnique();
            entity.Property(callback => callback.TargetUrl).HasMaxLength(2048).IsRequired();
            entity.Property(callback => callback.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(callback => callback.Error).HasMaxLength(1024);
        });

        modelBuilder.Entity<ProviderScenario>(entity =>
        {
            entity.ToTable("provider_scenarios");
            entity.HasKey(scenario => scenario.Id);
            entity.Property(scenario => scenario.Mode).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(scenario => scenario.HealthState).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CallbackConfiguration>(entity =>
        {
            entity.ToTable("callback_configurations");
            entity.HasKey(configuration => configuration.Id);
            entity.Property(configuration => configuration.Url).HasMaxLength(2048).IsRequired();
            entity.Property(configuration => configuration.WebhookSecret).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<SettlementRecord>(entity =>
        {
            entity.ToTable("settlement_records");
            entity.HasKey(record => record.Id);
            entity.HasIndex(record => record.ProviderReference);
            entity.Property(record => record.ProviderReference).HasMaxLength(64).IsRequired();
            entity.Property(record => record.ClientReference).HasMaxLength(128).IsRequired();
            entity.Property(record => record.Amount).HasPrecision(18, 4);
            entity.Property(record => record.Currency).HasMaxLength(3).IsRequired();
            entity.Property(record => record.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ProviderClient>(entity =>
        {
            entity.ToTable("provider_clients");
            entity.HasKey(client => client.Id);
            entity.HasIndex(client => client.ClientId).IsUnique();
            entity.HasIndex(client => client.ApiKey).IsUnique();
            entity.Property(client => client.ClientId).HasMaxLength(80).IsRequired();
            entity.Property(client => client.ApiKey).HasMaxLength(128).IsRequired();
            entity.Property(client => client.Secret).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<RequestAuditEvent>(entity =>
        {
            entity.ToTable("request_audit_events");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.EventType).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.ProviderReference).HasMaxLength(64);
            entity.Property(audit => audit.ClientReference).HasMaxLength(128);
            entity.Property(audit => audit.Message).HasMaxLength(1024).IsRequired();
        });
    }
}
