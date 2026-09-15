using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payments.Customer.Application.Customers;
using Payments.Customer.Domain.Customers;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Customer.Infrastructure.Persistence;

public sealed class CustomerRepository : ICustomerRepository
{
    private const string CustomerLifecycleTopic = "customer.lifecycle.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CustomerDbContext _dbContext;

    public CustomerRepository(CustomerDbContext dbContext) => _dbContext = dbContext;

    public Task<Customer.Domain.Customers.Customer?> GetByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
        => _dbContext.Customers.SingleOrDefaultAsync(customer => customer.Id == customerId, cancellationToken);

    public Task<Customer.Domain.Customers.Customer?> GetByIdentityUserIdAsync(Guid identityUserId, CancellationToken cancellationToken = default)
        => _dbContext.Customers.SingleOrDefaultAsync(customer => customer.IdentityUserId == identityUserId, cancellationToken);

    public Task<bool> HasProcessedEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        => _dbContext.ProcessedIntegrationEvents.AnyAsync(processed => processed.EventId == eventId, cancellationToken);

    public void Add(Customer.Domain.Customers.Customer customer) => _dbContext.Customers.Add(customer);

    public void AddProcessedEvent(ProcessedIntegrationEvent processedEvent) => _dbContext.ProcessedIntegrationEvents.Add(processedEvent);

    public void AddAudit(CustomerAuditEvent auditEvent) => _dbContext.CustomerAuditEvents.Add(auditEvent);

    public void AddOutbox(OutboxMessage outboxMessage) => _dbContext.OutboxMessages.Add(outboxMessage);

    public void AddLifecycleOutbox(Customer.Domain.Customers.Customer customer, DateTimeOffset occurredAtUtc, string correlationId, string? causationId = null)
    {
        var payload = new CustomerLifecycleIntegrationEvent(customer.Id, customer.Status.ToString(), customer.KycStatus.ToString());
        var envelope = new IntegrationEventEnvelope<CustomerLifecycleIntegrationEvent>(Guid.NewGuid(), CustomerLifecycleIntegrationEvent.EventType, CustomerLifecycleIntegrationEvent.EventVersion, occurredAtUtc, correlationId, causationId, "customer-service", payload);
        AddOutbox(OutboxMessage.Create(CustomerLifecycleTopic, customer.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), occurredAtUtc, envelope.EventId, envelope.EventVersion, "Customer", customer.Id.ToString("D")));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
