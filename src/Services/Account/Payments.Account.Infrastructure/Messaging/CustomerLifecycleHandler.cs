using Microsoft.EntityFrameworkCore;
using Payments.Account.Domain.Accounts;
using Payments.Account.Infrastructure.Persistence;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Account.Infrastructure.Messaging;

public sealed class CustomerLifecycleHandler
{
    private readonly AccountDbContext _dbContext;
    private readonly IClock _clock;

    public CustomerLifecycleHandler(AccountDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task HandleAsync(IntegrationEventEnvelope<CustomerLifecycleIntegrationEvent> envelope, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.ProcessedIntegrationEvents.AnyAsync(processed => processed.EventId == envelope.EventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var existing = await _dbContext.CustomerReferences.SingleOrDefaultAsync(reference => reference.CustomerId == envelope.Payload.CustomerId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            _dbContext.CustomerReferences.Add(CustomerReference.Upsert(envelope.Payload.CustomerId, envelope.Payload.Status, envelope.Payload.KycStatus, envelope.OccurredAtUtc));
        }
        else
        {
            existing.Apply(envelope.Payload.Status, envelope.Payload.KycStatus, envelope.OccurredAtUtc);
        }

        _dbContext.ProcessedIntegrationEvents.Add(ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, _clock.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
