using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Customer.Application.Customers;

namespace Payments.Customer.Infrastructure.Messaging;

public sealed class IdentityUserRegisteredHandler : IIntegrationEventHandler<IdentityUserRegisteredIntegrationEvent>
{
    private readonly ICustomerRepository _repository;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly ILogger<IdentityUserRegisteredHandler> _logger;

    public IdentityUserRegisteredHandler(ICustomerRepository repository, IClock clock, IRequestContext requestContext, ILogger<IdentityUserRegisteredHandler> logger)
    {
        _repository = repository;
        _clock = clock;
        _requestContext = requestContext;
        _logger = logger;
    }


    public Task HandleAsync(IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent> envelope, CancellationToken cancellationToken = default)
        => HandleAsync(envelope, CreateDefaultContext(envelope), cancellationToken);

    private static IntegrationEventContext CreateDefaultContext(IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent> envelope)
        => new(envelope.EventId, envelope.EventType, envelope.EventVersion, "direct-test", string.Empty, 0, 0, envelope.CorrelationId, envelope.CausationId, envelope.OccurredAtUtc, string.Empty);
    public async Task HandleAsync(IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent> envelope, IntegrationEventContext context, CancellationToken cancellationToken = default)
    {
        if (await _repository.HasProcessedEventAsync(envelope.EventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var existing = await _repository.GetByIdentityUserIdAsync(envelope.Payload.UserId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var customer = Customer.Domain.Customers.Customer.RegisterFromIdentity(envelope.Payload.CustomerId, envelope.Payload.UserId, envelope.Payload.Email, envelope.Payload.PhoneNumber, _clock.UtcNow);
            _repository.Add(customer);
            _repository.AddAudit(Customer.Domain.Customers.CustomerAuditEvent.Create("CustomerRegistered", envelope.Payload.UserId, customer.Id, _clock.UtcNow, envelope.CorrelationId));
            _repository.AddLifecycleOutbox(customer, _clock.UtcNow, envelope.CorrelationId, envelope.CausationId);
            _logger.LogInformation("Created customer {CustomerId} from identity user {UserId} via event {EventId}", customer.Id, envelope.Payload.UserId, context.EventId);
        }

        _repository.AddProcessedEvent(Customer.Domain.Customers.ProcessedIntegrationEvent.Create(envelope.EventId, envelope.EventType, _clock.UtcNow));
        await _repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
