using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Domain.Ledger;
using Payments.Ledger.Infrastructure.Persistence;

namespace Payments.Ledger.Infrastructure.Messaging;

public sealed class AccountLifecycleHandler
{
    private const string LedgerAccountsTopic = "ledger.accounts.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly LedgerDbContext _dbContext;
    private readonly IClock _clock;

    public AccountLifecycleHandler(LedgerDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task HandleAsync(IntegrationEventEnvelope<AccountLifecycleIntegrationEvent> envelope, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.ProcessedIntegrationEvents.AnyAsync(processed => processed.EventId == envelope.EventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (string.Equals(envelope.Payload.AccountType, "Wallet", StringComparison.OrdinalIgnoreCase) && string.Equals(envelope.Payload.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var externalReference = $"account:{envelope.Payload.AccountId:D}";
            if (!await _dbContext.LedgerAccounts.AnyAsync(account => account.ExternalReference == externalReference, cancellationToken).ConfigureAwait(false))
            {
                var account = LedgerAccount.Create(externalReference, $"2100-{envelope.Payload.Currency}-{envelope.Payload.AccountId.ToString("N")[..8]}", "Customer Wallet Liability", LedgerAccountType.Liability, Currency.FromCode(envelope.Payload.Currency), envelope.OccurredAtUtc);
                _dbContext.LedgerAccounts.Add(account);
                _dbContext.BalanceProjections.Add(LedgerAccountBalance.Create(account.Id, account.Currency, envelope.OccurredAtUtc));
                _dbContext.LedgerAuditEvents.Add(LedgerAuditEvent.Create("LedgerAccountCreated", ActorType.Service, "account-service", null, account.Id, _clock.UtcNow, envelope.CorrelationId, envelope.CausationId, null, JsonSerializer.Serialize(new { sourceAccountId = envelope.Payload.AccountId, envelope.Payload.CustomerId, account.AccountCode }, SerializerOptions)));
                var payload = new LedgerAccountCreatedIntegrationEvent(account.Id, account.ExternalReference, account.AccountCode, account.AccountType.ToString(), account.Currency.Code);
                var outboxEnvelope = new IntegrationEventEnvelope<LedgerAccountCreatedIntegrationEvent>(Guid.NewGuid(), LedgerAccountCreatedIntegrationEvent.EventType, LedgerAccountCreatedIntegrationEvent.EventVersion, _clock.UtcNow, envelope.CorrelationId, envelope.CausationId, "ledger-service", payload);
                _dbContext.OutboxMessages.Add(OutboxMessage.Create(LedgerAccountsTopic, account.Id.ToString("D"), outboxEnvelope.EventType, JsonSerializer.Serialize(outboxEnvelope, SerializerOptions), _clock.UtcNow, outboxEnvelope.EventId, outboxEnvelope.EventVersion, "LedgerAccount", account.Id.ToString("D")));
            }
        }

        _dbContext.ProcessedIntegrationEvents.Add(ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, _clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
