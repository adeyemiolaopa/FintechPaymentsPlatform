using FluentAssertions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Service.Template.Domain.Examples;

namespace Payments.UnitTests;

public sealed class DomainPrimitiveTests
{
    [Fact]
    public void Aggregate_root_records_and_clears_domain_events()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.Touch();

        aggregate.DomainEvents.Should().ContainSingle();
        aggregate.ClearDomainEvents();
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Value_objects_compare_by_components()
    {
        var left = new TestValueObject("NGN", 100);
        var right = new TestValueObject("NGN", 100);

        left.Should().Be(right);
    }

    [Fact]
    public void Example_creation_normalizes_email_and_raises_event()
    {
        var now = DateTimeOffset.UtcNow;

        var example = Example.Create(" Demo ", "USER@Example.COM", now);

        example.Name.Should().Be("Demo");
        example.Email.Should().Be("user@example.com");
        example.DomainEvents.Should().ContainSingle(evt => evt is ExampleCreatedDomainEvent);
    }

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate(Guid id)
            : base(id)
        {
        }

        public void Touch() => RaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private sealed record TestDomainEvent(Guid EventId, DateTimeOffset OccurredAtUtc) : IDomainEvent;

    private sealed class TestValueObject : ValueObject
    {
        private readonly string _currency;
        private readonly int _amount;

        public TestValueObject(string currency, int amount)
        {
            _currency = currency;
            _amount = amount;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return _currency;
            yield return _amount;
        }
    }
}
