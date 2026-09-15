using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Payment.Domain.Payments;
using Payments.Payment.Infrastructure.Persistence;

namespace Payments.Payment.Infrastructure.Messaging;

public sealed class PaymentReferenceHandler
{
    private readonly PaymentDbContext _dbContext;
    private readonly IClock _clock;

    public PaymentReferenceHandler(PaymentDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task HandleCustomerAsync(IntegrationEventEnvelope<CustomerLifecycleIntegrationEvent> envelope, CancellationToken cancellationToken = default)
    {
        if (await AlreadyProcessedAsync(envelope.EventId, cancellationToken).ConfigureAwait(false)) return;
        var now = _clock.UtcNow;
        var existing = await _dbContext.CustomerReferences.SingleOrDefaultAsync(reference => reference.CustomerId == envelope.Payload.CustomerId, cancellationToken).ConfigureAwait(false);
        if (existing is null) _dbContext.CustomerReferences.Add(CustomerReference.Upsert(envelope.Payload.CustomerId, envelope.Payload.Status, envelope.Payload.KycStatus, now));
        else existing.Update(envelope.Payload.Status, envelope.Payload.KycStatus, now);
        _dbContext.ProcessedIntegrationEvents.Add(ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, now));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAccountAsync(IntegrationEventEnvelope<AccountLifecycleIntegrationEvent> envelope, CancellationToken cancellationToken = default)
    {
        if (await AlreadyProcessedAsync(envelope.EventId, cancellationToken).ConfigureAwait(false)) return;
        var now = _clock.UtcNow;
        var existing = await _dbContext.AccountReferences.SingleOrDefaultAsync(reference => reference.AccountId == envelope.Payload.AccountId, cancellationToken).ConfigureAwait(false);
        if (existing is null) _dbContext.AccountReferences.Add(AccountReference.Upsert(envelope.Payload.AccountId, envelope.Payload.CustomerId, envelope.Payload.Currency, envelope.Payload.AccountType, envelope.Payload.Status, null, now));
        else existing.Update(envelope.Payload.Currency, envelope.Payload.AccountType, envelope.Payload.Status, now);
        _dbContext.ProcessedIntegrationEvents.Add(ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, now));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleLedgerAccountAsync(IntegrationEventEnvelope<LedgerAccountCreatedIntegrationEvent> envelope, CancellationToken cancellationToken = default)
    {
        if (await AlreadyProcessedAsync(envelope.EventId, cancellationToken).ConfigureAwait(false)) return;
        var now = _clock.UtcNow;
        if (envelope.Payload.ExternalReference.StartsWith("account:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(envelope.Payload.ExternalReference[8..], out var accountId))
        {
            var existing = await _dbContext.AccountReferences.SingleOrDefaultAsync(reference => reference.AccountId == accountId, cancellationToken).ConfigureAwait(false);
            existing?.AttachLedgerAccount(envelope.Payload.LedgerAccountId, now);
        }

        _dbContext.ProcessedIntegrationEvents.Add(ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, now));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> AlreadyProcessedAsync(Guid eventId, CancellationToken cancellationToken)
        => await _dbContext.ProcessedIntegrationEvents.AnyAsync(item => item.EventId == eventId, cancellationToken).ConfigureAwait(false);
}
