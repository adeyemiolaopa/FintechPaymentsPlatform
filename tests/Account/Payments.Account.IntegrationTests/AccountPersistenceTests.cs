using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Account.Application.Accounts;
using Payments.Account.Domain.Accounts;
using Payments.Account.Infrastructure.Messaging;
using Payments.Account.Infrastructure.Persistence;
using Payments.Account.Infrastructure.Services;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using Testcontainers.PostgreSql;

namespace Payments.Account.IntegrationTests;

public sealed class AccountPersistenceTests : IAsyncLifetime
{
    static AccountPersistenceTests()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.41");
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("payments_account_tests").WithUsername("payments").WithPassword("payments").Build();

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.41");
        await _postgres.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Customer_lifecycle_events_upsert_local_reference_idempotently()
    {
        await using var dbContext = CreateDbContext();
        var handler = new CustomerLifecycleHandler(dbContext, new TestClock());
        var customerId = Guid.NewGuid();
        var envelope = new IntegrationEventEnvelope<CustomerLifecycleIntegrationEvent>(Guid.NewGuid(), CustomerLifecycleIntegrationEvent.EventType, CustomerLifecycleIntegrationEvent.EventVersion, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null, "customer-service", new CustomerLifecycleIntegrationEvent(customerId, "Active", "Verified"));

        await handler.HandleAsync(envelope);
        await handler.HandleAsync(envelope);

        (await dbContext.CustomerReferences.CountAsync()).Should().Be(1);
        (await dbContext.ProcessedIntegrationEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_overspend_available_balance()
    {
        var customerId = Guid.NewGuid();
        var accountId = await SeedFundedAccountAsync(customerId, 100_000m);
        var barrier = new Barrier(2);

        async Task<Result> ReserveAsync(string reference)
        {
            await using var dbContext = CreateDbContext();
            var service = CreateService(dbContext, customerId);
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            try
            {
                await service.ReserveFundsAsync(accountId, new CreateReservationRequest(reference, 80_000m, "NGN", DateTimeOffset.UtcNow.AddMinutes(10)));
                return Result.Success;
            }
            catch (Exception exception) when (exception is ConflictException or Payments.BuildingBlocks.Domain.Primitives.DomainException)
            {
                return Result.Rejected;
            }
        }

        var results = await Task.WhenAll(ReserveAsync("reserve-a"), ReserveAsync("reserve-b"));

        results.Count(result => result == Result.Success).Should().Be(1);
        results.Count(result => result == Result.Rejected).Should().Be(1);
        await using var verification = CreateDbContext();
        var account = await verification.Accounts.SingleAsync(account => account.Id == accountId);
        account.ReservedBalance.Should().BeLessThanOrEqualTo(100_000m);
        account.AvailableBalance.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task Reservation_reference_is_idempotent()
    {
        var customerId = Guid.NewGuid();
        var accountId = await SeedFundedAccountAsync(customerId, 100_000m);
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, customerId);
        var request = new CreateReservationRequest("same-reference", 10_000m, "NGN", DateTimeOffset.UtcNow.AddMinutes(10));

        var first = await service.ReserveFundsAsync(accountId, request);
        var second = await service.ReserveFundsAsync(accountId, request);

        first.ReservationId.Should().Be(second.ReservationId);
        (await dbContext.FundsReservations.CountAsync(reservation => reservation.AccountId == accountId)).Should().Be(1);
        (await dbContext.Accounts.SingleAsync(account => account.Id == accountId)).ReservedBalance.Should().Be(10_000m);
    }

    private async Task<Guid> SeedFundedAccountAsync(Guid customerId, decimal ledgerBalance)
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.CustomerReferences.Add(CustomerReference.Upsert(customerId, "Active", "Verified", now));
        var account = Account.Domain.Accounts.Account.OpenWallet(customerId, AccountNumber.Generate(), Currency.FromCode("NGN"), now);
        account.SeedLedgerBalanceForTest(Money.Of(ledgerBalance, Currency.FromCode("NGN")), now);
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    private AccountService CreateService(AccountDbContext dbContext, Guid customerId) => new(dbContext, new TestClock(), new TestCurrentUser(customerId), new TestRequestContext(), new CreateAccountRequestValidator(), new FreezeAccountRequestValidator(), new CreateRestrictionRequestValidator(), new CreateReservationRequestValidator(), Options.Create(new AccountPolicyOptions()));
    private AccountDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private enum Result { Success, Rejected }

    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class TestRequestContext : IRequestContext { public string CorrelationId { get; } = Guid.NewGuid().ToString("D"); public string? CausationId => null; public string TraceId => Guid.NewGuid().ToString("N"); }
    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid customerId) { CustomerId = customerId.ToString("D"); UserId = Guid.NewGuid().ToString("D"); }
        public string? UserId { get; }
        public string? CustomerId { get; }
        public IReadOnlyCollection<string> Roles => ["Customer"];
        public IReadOnlyCollection<string> Permissions => ["account.read.self", "account.create", "beneficiary.read.self", "beneficiary.create", "beneficiary.remove"];
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}
