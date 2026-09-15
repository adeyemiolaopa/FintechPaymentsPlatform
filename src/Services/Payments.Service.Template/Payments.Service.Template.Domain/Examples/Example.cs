using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Service.Template.Domain.Examples;

public sealed record ExampleId(Guid Value) : StronglyTypedId(Value)
{
    public static ExampleId New() => new(Guid.NewGuid());

    public static ExampleId From(Guid value) => new(value);
}

public sealed class Example : AggregateRoot<ExampleId>
{
    private Example()
        : base(ExampleId.From(Guid.Empty))
    {
        Name = string.Empty;
        Email = string.Empty;
        CreatedAtUtc = DateTimeOffset.UnixEpoch;
    }

    private Example(ExampleId id, string name, string email, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Name = name;
        Email = email;
        CreatedAtUtc = createdAtUtc;
        RaiseDomainEvent(new ExampleCreatedDomainEvent(Guid.NewGuid(), createdAtUtc, id.Value));
    }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Example Create(string name, string email, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("example_name_required", "Example name is required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("example_email_required", "Example email is required.");
        }

        return new Example(ExampleId.New(), name.Trim(), email.Trim().ToLowerInvariant(), createdAtUtc);
    }
}

public sealed record ExampleCreatedDomainEvent(Guid EventId, DateTimeOffset OccurredAtUtc, Guid ExampleId) : IDomainEvent;
