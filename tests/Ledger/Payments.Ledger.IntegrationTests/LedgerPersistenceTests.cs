using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Application.Ledger;
using Payments.Ledger.Domain.Ledger;
using Payments.Ledger.Infrastructure.Messaging;
using Payments.Ledger.Infrastructure.Persistence;
using Payments.Ledger.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace Payments.Ledger.IntegrationTests;

public sealed class LedgerPersistenceTests : IAsyncLifetime
{
    static LedgerPersistenceTests()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.41");
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("payments_ledger_tests").WithUsername("payments").WithPassword("payments").Build();

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.41");
        await _postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Balanced_posting_persists_postings_projection_audit_and_outbox_atomically()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        var response = await service.PostTransactionAsync(FundingRequest("DEP-100", asset, liability, 100_000m));

        response.Postings.Should().HaveCount(2);
        (await dbContext.LedgerTransactions.CountAsync()).Should().Be(1);
        (await dbContext.Postings.CountAsync()).Should().Be(2);
        (await dbContext.LedgerAuditEvents.CountAsync()).Should().Be(1);
        (await dbContext.OutboxMessages.CountAsync()).Should().Be(1);
        (await service.GetBalanceAsync(liability, null)).Balance.Should().Be(100_000m);
    }

    [Fact]
    public async Task Unbalanced_posting_rolls_back_all_financial_records()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = new PostLedgerTransactionRequest("BAD-100", "WalletFunding", "NGN", "Bad", [new PostingRequest(asset, "Debit", 100_000m), new PostingRequest(liability, "Credit", 90_000m)]);

        await service.Invoking(item => item.PostTransactionAsync(request)).Should().ThrowAsync<Payments.BuildingBlocks.Domain.Primitives.DomainException>();

        (await dbContext.LedgerTransactions.CountAsync()).Should().Be(0);
        (await dbContext.Postings.CountAsync()).Should().Be(0);
        (await dbContext.IdempotencyRecords.CountAsync()).Should().Be(0);
        (await dbContext.OutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Same_idempotency_key_and_same_instruction_returns_existing_transaction()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = FundingRequest("DEP-IDEMP", asset, liability, 25_000m);

        var first = await service.PostTransactionAsync(request);
        var second = await service.PostTransactionAsync(request);

        second.TransactionId.Should().Be(first.TransactionId);
        (await dbContext.LedgerTransactions.CountAsync()).Should().Be(1);
        (await dbContext.Postings.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Same_idempotency_key_and_different_instruction_conflicts()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await service.PostTransactionAsync(FundingRequest("DEP-CONFLICT", asset, liability, 25_000m));

        await service.Invoking(item => item.PostTransactionAsync(FundingRequest("DEP-CONFLICT", asset, liability, 30_000m))).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Concurrent_same_instruction_creates_one_financial_effect()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        var request = FundingRequest("DEP-CONCURRENT", asset, liability, 10_000m);
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            await using var dbContext = CreateDbContext();
            return await CreateService(dbContext).PostTransactionAsync(request);
        });

        var responses = await Task.WhenAll(tasks);

        responses.Select(response => response.TransactionId).Distinct().Should().ContainSingle();
        await using var verify = CreateDbContext();
        (await verify.LedgerTransactions.CountAsync()).Should().Be(1);
        (await verify.Postings.CountAsync()).Should().Be(2);
        var liabilityBalance = await CreateService(verify).GetBalanceAsync(liability, null);
        liabilityBalance.Balance.Should().Be(10_000m);
    }

    [Fact]
    public async Task Multi_leg_posting_and_reversal_restore_financial_position()
    {
        var (asset, liability, revenue) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var request = new PostLedgerTransactionRequest("PAY-FEE", "PaymentWithFee", "NGN", "Payment with fee", [new PostingRequest(liability, "Debit", 101_000m), new PostingRequest(asset, "Credit", 100_000m), new PostingRequest(revenue, "Credit", 1_000m)]);

        var posted = await service.PostTransactionAsync(request);
        var reversed = await service.ReverseTransactionAsync(posted.TransactionId, new ReverseLedgerTransactionRequest("REV-PAY-FEE", "Customer dispute"));

        reversed.OriginalTransactionId.Should().Be(posted.TransactionId);
        (await service.GetBalanceAsync(liability, null)).Balance.Should().Be(0m);
        (await service.GetBalanceAsync(asset, null)).Balance.Should().Be(0m);
        (await service.GetBalanceAsync(revenue, null)).Balance.Should().Be(0m);
        (await dbContext.Reversals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_reversal_is_prevented()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var posted = await service.PostTransactionAsync(FundingRequest("DEP-REV", asset, liability, 5_000m));

        await service.ReverseTransactionAsync(posted.TransactionId, new ReverseLedgerTransactionRequest("REV-DEP-REV", "Mistake"));

        await service.Invoking(item => item.ReverseTransactionAsync(posted.TransactionId, new ReverseLedgerTransactionRequest("REV-DEP-REV-2", "Again"))).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Integrity_verification_detects_valid_projection()
    {
        var (asset, liability, _) = await SeedAccountsAsync();
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        await service.PostTransactionAsync(FundingRequest("DEP-INTEGRITY", asset, liability, 42_000m));

        var result = await service.VerifyIntegrityAsync();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Account_lifecycle_event_creates_customer_wallet_liability_account_idempotently()
    {
        await using var dbContext = CreateDbContext();
        var handler = new AccountLifecycleHandler(dbContext, new TestClock());
        var accountId = Guid.NewGuid();
        var envelope = new IntegrationEventEnvelope<AccountLifecycleIntegrationEvent>(Guid.NewGuid(), AccountLifecycleIntegrationEvent.EventType, AccountLifecycleIntegrationEvent.EventVersion, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null, "account-service", new AccountLifecycleIntegrationEvent(accountId, Guid.NewGuid(), "NGN", "Wallet", "Active", null));

        await handler.HandleAsync(envelope);
        await handler.HandleAsync(envelope);

        (await dbContext.LedgerAccounts.CountAsync(account => account.ExternalReference == $"account:{accountId:D}")).Should().Be(1);
        (await dbContext.ProcessedIntegrationEvents.CountAsync()).Should().Be(1);
    }

    private async Task<(Guid Asset, Guid Liability, Guid Revenue)> SeedAccountsAsync()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var currency = Currency.FromCode("NGN");
        var asset = LedgerAccount.Create(Guid.NewGuid().ToString("N"), $"1100-{Guid.NewGuid():N}"[..20], "Settlement", LedgerAccountType.Asset, currency, now);
        var liability = LedgerAccount.Create(Guid.NewGuid().ToString("N"), $"2100-{Guid.NewGuid():N}"[..20], "Wallet", LedgerAccountType.Liability, currency, now);
        var revenue = LedgerAccount.Create(Guid.NewGuid().ToString("N"), $"4100-{Guid.NewGuid():N}"[..20], "Fee Revenue", LedgerAccountType.Revenue, currency, now);
        dbContext.LedgerAccounts.AddRange(asset, liability, revenue);
        dbContext.BalanceProjections.AddRange(LedgerAccountBalance.Create(asset.Id, currency, now), LedgerAccountBalance.Create(liability.Id, currency, now), LedgerAccountBalance.Create(revenue.Id, currency, now));
        await dbContext.SaveChangesAsync();
        return (asset.Id, liability.Id, revenue.Id);
    }

    private static PostLedgerTransactionRequest FundingRequest(string reference, Guid asset, Guid liability, decimal amount)
        => new(reference, "WalletFunding", "NGN", "Wallet funding", [new PostingRequest(asset, "Debit", amount), new PostingRequest(liability, "Credit", amount)]);

    private LedgerService CreateService(LedgerDbContext dbContext) => new(dbContext, new TestClock(), new TestCurrentUser(), new TestRequestContext(), new CreateLedgerAccountRequestValidator(), new PostLedgerTransactionRequestValidator(), new ReverseLedgerTransactionRequestValidator());
    private LedgerDbContext CreateDbContext() => new(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class TestRequestContext : IRequestContext { public string CorrelationId { get; } = Guid.NewGuid().ToString("D"); public string? CausationId => null; public string TraceId => Guid.NewGuid().ToString("N"); }
    private sealed class TestCurrentUser : ICurrentUser
    {
        public string? UserId { get; } = Guid.NewGuid().ToString("D");
        public string? CustomerId => null;
        public IReadOnlyCollection<string> Roles => ["Administrator"];
        public IReadOnlyCollection<string> Permissions => ["ledger.account.create", "ledger.account.read", "ledger.transaction.post", "ledger.transaction.read", "ledger.transaction.reverse", "ledger.integrity.read", "ledger.audit.read"];
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}
