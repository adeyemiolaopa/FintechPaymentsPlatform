using Payments.BuildingBlocks.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationDbContext(DbContextOptions<ReconciliationDbContext> options) : DbContext(options)
{
    public DbSet<SettlementFile> SettlementFiles => Set<SettlementFile>();
    public DbSet<SettlementRecord> SettlementRecords => Set<SettlementRecord>();
    public DbSet<ReconciliationRun> Runs => Set<ReconciliationRun>();
    public DbSet<ReconciliationMatch> Matches => Set<ReconciliationMatch>();
    public DbSet<ReconciliationException> Exceptions => Set<ReconciliationException>();
    public DbSet<ReconciliationAuditEvent> AuditEvents => Set<ReconciliationAuditEvent>();
    public DbSet<ProviderStatusObservation> ProviderObservations => Set<ProviderStatusObservation>();
    public DbSet<ActiveReconciliationJob> ActiveJobs => Set<ActiveReconciliationJob>();
    public DbSet<ReconciliationOutboxMessage> OutboxMessages => Set<ReconciliationOutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<PaymentReferenceRecord> PaymentReferences => Set<PaymentReferenceRecord>();
    public DbSet<LedgerReferenceRecord> LedgerReferences => Set<LedgerReferenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reconciliation");
        modelBuilder.ConfigureInboxMessages();
        modelBuilder.Entity<PaymentReferenceRecord>(b =>
        {
            b.ToTable("payment_reference_records"); b.HasKey(x => x.PaymentId);
            b.Property(x => x.PaymentReference).HasMaxLength(128).IsRequired(); b.HasIndex(x => x.PaymentReference).IsUnique();
            b.Property(x => x.PaymentType).HasMaxLength(40); b.Property(x => x.Status).HasMaxLength(40); b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.ReasonCode).HasMaxLength(80);
        });
        modelBuilder.Entity<LedgerReferenceRecord>(b =>
        {
            b.ToTable("ledger_reference_records"); b.HasKey(x => x.TransactionId);
            b.Property(x => x.ExternalReference).HasMaxLength(128).IsRequired(); b.HasIndex(x => x.ExternalReference);
            b.Property(x => x.TransactionType).HasMaxLength(80); b.Property(x => x.Status).HasMaxLength(40); b.Property(x => x.Currency).HasMaxLength(3);
        });
        modelBuilder.Entity<SettlementFile>(b =>
        {
            b.ToTable("settlement_files"); b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.RejectionReason).HasMaxLength(500);
            b.Property(x => x.UploadedBy).HasMaxLength(128);
            b.HasIndex(x => new { x.Provider, x.FileHash }).IsUnique();
            b.HasIndex(x => new { x.Provider, x.SettlementDate });
        });
        modelBuilder.Entity<SettlementRecord>(b =>
        {
            b.ToTable("settlement_records"); b.HasKey(x => x.Id);
            b.Property(x => x.ProviderReference).HasMaxLength(128); b.Property(x => x.ClientReference).HasMaxLength(128);
            b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.ProviderStatus).HasMaxLength(40);
            b.Property(x => x.RawRecordHash).HasMaxLength(64); b.Property(x => x.Amount).HasPrecision(19, 4);
            b.HasIndex(x => new { x.SettlementFileId, x.SourceLineNumber }).IsUnique();
            b.HasIndex(x => x.ProviderReference); b.HasIndex(x => x.ClientReference);
        });
        modelBuilder.Entity<ReconciliationRun>(b =>
        {
            b.ToTable("reconciliation_runs"); b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(64); b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            b.HasIndex(x => x.SettlementFileId).IsUnique().HasFilter("\"SettlementFileId\" IS NOT NULL");
        });
        modelBuilder.Entity<ReconciliationMatch>(b =>
        {
            b.ToTable("reconciliation_matches"); b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            b.Property(x => x.ExceptionCode).HasConversion<string>().HasMaxLength(80);
            b.Property(x => x.PaymentReference).HasMaxLength(128); b.Property(x => x.ProviderReference).HasMaxLength(128);
            b.Property(x => x.ResolutionType).HasMaxLength(80);
            b.HasIndex(x => x.SettlementRecordId).IsUnique().HasFilter("\"SettlementRecordId\" IS NOT NULL");
            b.HasIndex(x => x.PaymentId); b.HasIndex(x => x.ProviderReference);
        });
        modelBuilder.Entity<ReconciliationException>(b =>
        {
            b.ToTable("reconciliation_exceptions"); b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(64); b.Property(x => x.ProviderReference).HasMaxLength(128);
            b.Property(x => x.PaymentReference).HasMaxLength(128);
            b.Property(x => x.Code).HasConversion<string>().HasMaxLength(80);
            b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.AssignedTo).HasMaxLength(128);
            b.HasIndex(x => new { x.Status, x.Severity, x.CreatedAtUtc });
            b.HasIndex(x => new { x.PaymentId, x.Code });
        });
        modelBuilder.Entity<ReconciliationAuditEvent>(b =>
        {
            b.ToTable("reconciliation_audit_events"); b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(80); b.Property(x => x.Actor).HasMaxLength(128);
            b.Property(x => x.ReasonCode).HasMaxLength(80); b.Property(x => x.Comment).HasMaxLength(1000);
            b.Property(x => x.DownstreamCommand).HasMaxLength(120);
            b.HasIndex(x => new { x.ExceptionId, x.OccurredAtUtc });
        });
        modelBuilder.Entity<ReconciliationOutboxMessage>(b =>
        {
            b.ToTable("outbox_messages"); b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            b.Property(x => x.Topic).HasMaxLength(160).IsRequired();
            b.Property(x => x.PartitionKey).HasMaxLength(128).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(1024);
            b.HasIndex(x => x.EventId).IsUnique();
            b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        });        modelBuilder.Entity<ActiveReconciliationJob>(b =>
        {
            b.ToTable("active_reconciliation_jobs"); b.HasKey(x => x.PaymentId);
            b.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.NextReconciliationAtUtc, x.LeaseUntilUtc });
        });        modelBuilder.Entity<ProviderStatusObservation>(b =>
        {
            b.ToTable("provider_status_observations"); b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(64); b.Property(x => x.ProviderReference).HasMaxLength(128);
            b.Property(x => x.ObservedStatus).HasMaxLength(40); b.Property(x => x.ResponseCode).HasMaxLength(40);
            b.HasIndex(x => new { x.PaymentId, x.ObservedAtUtc });
        });
    }
}
