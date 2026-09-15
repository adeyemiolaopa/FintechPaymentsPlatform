using FluentAssertions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Customer.Domain.Customers;

namespace Payments.Customer.UnitTests;

public sealed class CustomerLifecycleTests
{
    [Fact]
    public void Customer_lifecycle_allows_suspend_and_activate()
    {
        var actor = Guid.NewGuid();
        var customer = Payments.Customer.Domain.Customers.Customer.RegisterFromIdentity(Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", "+2348012345678", DateTimeOffset.UtcNow);
        customer.Activate(actor, DateTimeOffset.UtcNow);
        customer.Suspend(actor, "risk_review", DateTimeOffset.UtcNow);
        customer.Status.Should().Be(CustomerStatus.Suspended);
        customer.Activate(actor, DateTimeOffset.UtcNow);
        customer.Status.Should().Be(CustomerStatus.Active);
    }

    [Fact]
    public void Customer_profile_updates_do_not_allow_identity_attribute_changes()
    {
        var actor = Guid.NewGuid();
        var customer = Payments.Customer.Domain.Customers.Customer.RegisterFromIdentity(Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", "+2348012345678", DateTimeOffset.UtcNow);
        customer.UpdateProfile("Ada", null, "Lovelace", new CustomerAddress("1 Main", null, "Lagos", "Lagos", "NG", null), actor, DateTimeOffset.UtcNow);
        customer.Email.Should().Be("customer@example.com");
        customer.PhoneNumber.Should().Be("+2348012345678");
        customer.FirstName.Should().Be("Ada");
    }
}


