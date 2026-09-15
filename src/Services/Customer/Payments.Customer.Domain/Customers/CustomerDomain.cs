using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Customer.Domain.Customers;

public enum CustomerStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Closed = 3,
}

public enum CustomerKycStatus
{
    NotStarted = 0,
    Pending = 1,
    Verified = 2,
    Rejected = 3,
    NeedsReview = 4,
}

public sealed class Customer : AggregateRoot<Guid>
{
    private Customer() : base(Guid.Empty) { }

    private Customer(Guid id, Guid identityUserId, string email, string phoneNumber, DateTimeOffset now)
        : base(id)
    {
        IdentityUserId = identityUserId;
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber.Trim();
        Status = CustomerStatus.Pending;
        KycStatus = CustomerKycStatus.NotStarted;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new CustomerRegisteredDomainEvent(Guid.NewGuid(), id, identityUserId, now));
    }

    public Guid IdentityUserId { get; private set; }
    public string? FirstName { get; private set; }
    public string? MiddleName { get; private set; }
    public string? LastName { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public DateOnly? DateOfBirth { get; private set; }
    public CustomerStatus Status { get; private set; }
    public CustomerKycStatus KycStatus { get; private set; }
    public CustomerAddress? Address { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static Customer RegisterFromIdentity(Guid customerId, Guid identityUserId, string email, string phoneNumber, DateTimeOffset now)
        => new(customerId, identityUserId, email, phoneNumber, now);

    public void UpdateProfile(string? firstName, string? middleName, string? lastName, CustomerAddress? address, Guid actorUserId, DateTimeOffset now)
    {
        if (Status == CustomerStatus.Closed)
        {
            throw new DomainException("customer.closed", "Closed customers cannot be updated.");
        }

        FirstName = NormalizeName(firstName);
        MiddleName = NormalizeName(middleName);
        LastName = NormalizeName(lastName);
        Address = address;
        UpdatedAtUtc = now;
        UpdatedBy = actorUserId;
    }

    public void Activate(Guid actorUserId, DateTimeOffset now)
    {
        if (Status == CustomerStatus.Closed)
        {
            throw new DomainException("customer.closed", "Closed customers cannot be activated.");
        }

        Status = CustomerStatus.Active;
        UpdatedAtUtc = now;
        UpdatedBy = actorUserId;
        RaiseDomainEvent(new CustomerActivatedDomainEvent(Guid.NewGuid(), Id, actorUserId, now));
    }

    public void Suspend(Guid actorUserId, string reason, DateTimeOffset now)
    {
        if (Status == CustomerStatus.Closed)
        {
            throw new DomainException("customer.closed", "Closed customers cannot be suspended.");
        }

        Status = CustomerStatus.Suspended;
        UpdatedAtUtc = now;
        UpdatedBy = actorUserId;
        RaiseDomainEvent(new CustomerSuspendedDomainEvent(Guid.NewGuid(), Id, actorUserId, reason, now));
    }

    private static string? NormalizeName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CustomerAddress(string Line1, string? Line2, string City, string Region, string CountryCode, string? PostalCode);

public sealed record CustomerRegisteredDomainEvent(Guid EventId, Guid CustomerId, Guid IdentityUserId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record CustomerSuspendedDomainEvent(Guid EventId, Guid CustomerId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed record CustomerActivatedDomainEvent(Guid EventId, Guid CustomerId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

public sealed class ProcessedIntegrationEvent
{
    private ProcessedIntegrationEvent() { }

    private ProcessedIntegrationEvent(Guid eventId, string eventType, DateTimeOffset processedAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static ProcessedIntegrationEvent Create(Guid eventId, string eventType, DateTimeOffset processedAtUtc) => new(eventId, eventType, processedAtUtc);
}

public sealed class CustomerAuditEvent
{
    private CustomerAuditEvent() { }

    private CustomerAuditEvent(string eventType, Guid? actorUserId, Guid targetCustomerId, DateTimeOffset now, string correlationId, string metadata)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        ActorUserId = actorUserId;
        TargetCustomerId = targetCustomerId;
        OccurredAtUtc = now;
        CorrelationId = correlationId;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public Guid TargetCustomerId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string Metadata { get; private set; } = "{}";

    public static CustomerAuditEvent Create(string eventType, Guid? actorUserId, Guid targetCustomerId, DateTimeOffset now, string correlationId, string metadata = "{}")
        => new(eventType, actorUserId, targetCustomerId, now, correlationId, metadata);
}




public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(string topic, string key, string eventType, string payload, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Topic = topic;
        Key = key;
        EventType = eventType;
        Payload = payload;
        OccurredAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string topic, string key, string eventType, string payload, DateTimeOffset now) => new(topic, key, eventType, payload, now);
    public void MarkPublished(DateTimeOffset now) { PublishedAtUtc = now; LastError = null; }
    public void MarkFailed(string error) => LastError = error.Length > 1024 ? error[..1024] : error;
}
