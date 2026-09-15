using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Identity.Domain.Users;

namespace Payments.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext, IUnitOfWork
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<User>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
            builder.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
            builder.Property(user => user.PhoneNumber).HasMaxLength(32).IsRequired();
            builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
            builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.HasIndex(user => user.NormalizedEmail).IsUnique();
            builder.HasIndex(user => user.PhoneNumber).IsUnique();
            builder.Ignore(user => user.RefreshTokens);
            builder.Ignore(user => user.DomainEvents);
        });

        modelBuilder.Entity<Role>(builder =>
        {
            builder.ToTable("roles");
            builder.HasKey(role => role.Id);
            builder.Property(role => role.Name).HasMaxLength(64).IsRequired();
            builder.HasIndex(role => role.Name).IsUnique();
            builder.HasData(IdentitySeed.Roles);
        });

        modelBuilder.Entity<Permission>(builder =>
        {
            builder.ToTable("permissions");
            builder.HasKey(permission => permission.Id);
            builder.Property(permission => permission.Name).HasMaxLength(128).IsRequired();
            builder.HasIndex(permission => permission.Name).IsUnique();
            builder.HasData(IdentitySeed.Permissions);
        });

        modelBuilder.Entity<UserRole>(builder =>
        {
            builder.ToTable("user_roles");
            builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });
            builder.HasOne<Role>(userRole => userRole.Role).WithMany().HasForeignKey(userRole => userRole.RoleId);
        });

        modelBuilder.Entity<RolePermission>(builder =>
        {
            builder.ToTable("role_permissions");
            builder.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId });
            builder.HasOne<Role>().WithMany(role => role.RolePermissions).HasForeignKey(rolePermission => rolePermission.RoleId);
            builder.HasOne(rolePermission => rolePermission.Permission).WithMany().HasForeignKey(rolePermission => rolePermission.PermissionId);
            builder.HasData(IdentitySeed.RolePermissions);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("refresh_tokens");
            builder.HasKey(token => token.Id);
            builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            builder.Property(token => token.CreatedByIp).HasMaxLength(64);
            builder.Property(token => token.RevokedByIp).HasMaxLength(64);
            builder.HasIndex(token => token.TokenHash).IsUnique();
            builder.HasIndex(token => token.FamilyId);
            builder.HasOne<User>().WithMany().HasForeignKey(token => token.UserId);
        });

        modelBuilder.Entity<SecurityAuditEvent>(builder =>
        {
            builder.ToTable("security_audit_events");
            builder.HasKey(audit => audit.Id);
            builder.Property(audit => audit.EventType).HasMaxLength(128).IsRequired();
            builder.Property(audit => audit.CorrelationId).HasMaxLength(64).IsRequired();
            builder.Property(audit => audit.IpAddress).HasMaxLength(64);
            builder.Property(audit => audit.UserAgent).HasMaxLength(256);
            builder.Property(audit => audit.Metadata).HasColumnType("jsonb").HasDefaultValue("{}");
            builder.HasIndex(audit => audit.OccurredAtUtc);
            builder.HasIndex(audit => audit.TargetUserId);
        });

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages");
            builder.HasKey(outbox => outbox.Id);
            builder.Property(outbox => outbox.Topic).HasMaxLength(128).IsRequired();
            builder.Property(outbox => outbox.Key).HasMaxLength(128).IsRequired();
            builder.Property(outbox => outbox.EventType).HasMaxLength(128).IsRequired();
            builder.Property(outbox => outbox.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(outbox => outbox.LastError).HasMaxLength(512);
            builder.HasIndex(outbox => outbox.PublishedAtUtc);
            builder.HasIndex(outbox => outbox.EventType);
        });
    }
}

public static class IdentitySeed
{
    public static readonly Guid CustomerRoleId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid OperationsRoleId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid AdministratorRoleId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid SupportRoleId = Guid.Parse("10000000-0000-0000-0000-000000000004");

    public static readonly Guid CustomerReadSelfId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid CustomerUpdateSelfId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid CustomerReadAnyId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid CustomerSuspendId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    public static readonly Guid CustomerActivateId = Guid.Parse("20000000-0000-0000-0000-000000000005");
    public static readonly Guid IdentityUserReadId = Guid.Parse("20000000-0000-0000-0000-000000000006");
    public static readonly Guid IdentityUserManageId = Guid.Parse("20000000-0000-0000-0000-000000000007");
    public static readonly Guid AccountReadSelfId = Guid.Parse("20000000-0000-0000-0000-000000000008");
    public static readonly Guid AccountReadAnyId = Guid.Parse("20000000-0000-0000-0000-000000000009");
    public static readonly Guid AccountCreateId = Guid.Parse("20000000-0000-0000-0000-000000000010");
    public static readonly Guid AccountFreezeId = Guid.Parse("20000000-0000-0000-0000-000000000011");
    public static readonly Guid AccountUnfreezeId = Guid.Parse("20000000-0000-0000-0000-000000000012");
    public static readonly Guid AccountCloseId = Guid.Parse("20000000-0000-0000-0000-000000000013");
    public static readonly Guid AccountRestrictId = Guid.Parse("20000000-0000-0000-0000-000000000014");
    public static readonly Guid AccountUnrestrictId = Guid.Parse("20000000-0000-0000-0000-000000000015");
    public static readonly Guid BeneficiaryReadSelfId = Guid.Parse("20000000-0000-0000-0000-000000000016");
    public static readonly Guid BeneficiaryCreateId = Guid.Parse("20000000-0000-0000-0000-000000000017");
    public static readonly Guid BeneficiaryRemoveId = Guid.Parse("20000000-0000-0000-0000-000000000018");
    public static readonly Guid LedgerAccountCreateId = Guid.Parse("20000000-0000-0000-0000-000000000019");
    public static readonly Guid LedgerAccountReadId = Guid.Parse("20000000-0000-0000-0000-000000000020");
    public static readonly Guid LedgerTransactionPostId = Guid.Parse("20000000-0000-0000-0000-000000000021");
    public static readonly Guid LedgerTransactionReadId = Guid.Parse("20000000-0000-0000-0000-000000000022");
    public static readonly Guid LedgerTransactionReverseId = Guid.Parse("20000000-0000-0000-0000-000000000023");
    public static readonly Guid LedgerIntegrityReadId = Guid.Parse("20000000-0000-0000-0000-000000000024");
    public static readonly Guid LedgerAuditReadId = Guid.Parse("20000000-0000-0000-0000-000000000025");

    public static readonly Role[] Roles =
    [
        new(CustomerRoleId, "Customer"),
        new(OperationsRoleId, "Operations"),
        new(AdministratorRoleId, "Administrator"),
        new(SupportRoleId, "Support"),
    ];

    public static readonly Permission[] Permissions =
    [
        new(CustomerReadSelfId, "customer.read.self"),
        new(CustomerUpdateSelfId, "customer.update.self"),
        new(CustomerReadAnyId, "customer.read.any"),
        new(CustomerSuspendId, "customer.suspend"),
        new(CustomerActivateId, "customer.activate"),
        new(IdentityUserReadId, "identity.user.read"),
        new(IdentityUserManageId, "identity.user.manage"),
        new(AccountReadSelfId, "account.read.self"),
        new(AccountReadAnyId, "account.read.any"),
        new(AccountCreateId, "account.create"),
        new(AccountFreezeId, "account.freeze"),
        new(AccountUnfreezeId, "account.unfreeze"),
        new(AccountCloseId, "account.close"),
        new(AccountRestrictId, "account.restrict"),
        new(AccountUnrestrictId, "account.unrestrict"),
        new(BeneficiaryReadSelfId, "beneficiary.read.self"),
        new(BeneficiaryCreateId, "beneficiary.create"),
        new(BeneficiaryRemoveId, "beneficiary.remove"),
        new(LedgerAccountCreateId, "ledger.account.create"),
        new(LedgerAccountReadId, "ledger.account.read"),
        new(LedgerTransactionPostId, "ledger.transaction.post"),
        new(LedgerTransactionReadId, "ledger.transaction.read"),
        new(LedgerTransactionReverseId, "ledger.transaction.reverse"),
        new(LedgerIntegrityReadId, "ledger.integrity.read"),
        new(LedgerAuditReadId, "ledger.audit.read"),
    ];

    public static readonly RolePermission[] RolePermissions =
    [
        new(CustomerRoleId, CustomerReadSelfId),
        new(CustomerRoleId, CustomerUpdateSelfId),
        new(CustomerRoleId, AccountReadSelfId),
        new(CustomerRoleId, AccountCreateId),
        new(CustomerRoleId, BeneficiaryReadSelfId),
        new(CustomerRoleId, BeneficiaryCreateId),
        new(CustomerRoleId, BeneficiaryRemoveId),
        new(OperationsRoleId, CustomerReadAnyId),
        new(OperationsRoleId, CustomerSuspendId),
        new(OperationsRoleId, CustomerActivateId),
        new(OperationsRoleId, AccountReadAnyId),
        new(OperationsRoleId, AccountFreezeId),
        new(OperationsRoleId, AccountUnfreezeId),
        new(OperationsRoleId, AccountCloseId),
        new(OperationsRoleId, AccountRestrictId),
        new(OperationsRoleId, AccountUnrestrictId),
        new(OperationsRoleId, LedgerAccountReadId),
        new(OperationsRoleId, LedgerTransactionReadId),
        new(OperationsRoleId, LedgerIntegrityReadId),
        new(SupportRoleId, CustomerReadAnyId),
        new(SupportRoleId, AccountReadAnyId),
        new(SupportRoleId, LedgerAccountReadId),
        new(SupportRoleId, LedgerTransactionReadId),
        new(AdministratorRoleId, CustomerReadAnyId),
        new(AdministratorRoleId, CustomerSuspendId),
        new(AdministratorRoleId, CustomerActivateId),
        new(AdministratorRoleId, IdentityUserReadId),
        new(AdministratorRoleId, IdentityUserManageId),
        new(AdministratorRoleId, AccountReadSelfId),
        new(AdministratorRoleId, AccountReadAnyId),
        new(AdministratorRoleId, AccountCreateId),
        new(AdministratorRoleId, AccountFreezeId),
        new(AdministratorRoleId, AccountUnfreezeId),
        new(AdministratorRoleId, AccountCloseId),
        new(AdministratorRoleId, AccountRestrictId),
        new(AdministratorRoleId, AccountUnrestrictId),
        new(AdministratorRoleId, BeneficiaryReadSelfId),
        new(AdministratorRoleId, BeneficiaryCreateId),
        new(AdministratorRoleId, BeneficiaryRemoveId),
        new(AdministratorRoleId, LedgerAccountCreateId),
        new(AdministratorRoleId, LedgerAccountReadId),
        new(AdministratorRoleId, LedgerTransactionPostId),
        new(AdministratorRoleId, LedgerTransactionReadId),
        new(AdministratorRoleId, LedgerTransactionReverseId),
        new(AdministratorRoleId, LedgerIntegrityReadId),
        new(AdministratorRoleId, LedgerAuditReadId),
    ];
}

public sealed class IdentityDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DATABASE__CONNECTIONSTRING")
            ?? "Host=127.0.0.1;Port=15432;Database=payments_identity;Username=payments;Password=change-me-local-only";
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        return new IdentityDbContext(options);
    }
}


