using FluentAssertions;
using Payments.RailSimulator.Domain;

namespace Payments.RailSimulator.UnitTests;

public sealed class RailDomainTests
{
    [Fact]
    public void RequestHash_ShouldBeCanonical_ForEquivalentInstructions()
    {
        var first = RailRequestHasher.Compute(" REF-1 ", "058", "0123456789", " Ada Lovelace ", 100.00001m, "ngn", " Rent ");
        var second = RailRequestHasher.Compute("REF-1", "058", "0123456789", "Ada Lovelace", 100m, "NGN", "Rent");

        first.Should().Be(second);
    }

    [Fact]
    public void RequestHash_ShouldChange_WhenFinancialInstructionChanges()
    {
        var first = RailRequestHasher.Compute("REF-1", "058", "0123456789", "Ada Lovelace", 100m, "NGN", "Rent");
        var second = RailRequestHasher.Compute("REF-1", "058", "0123456789", "Ada Lovelace", 101m, "NGN", "Rent");

        first.Should().NotBe(second);
    }

    [Fact]
    public void ProviderReference_ShouldCarryRailPrefixAndBusinessDate()
    {
        var reference = RailProviderReference.Generate(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));

        reference.Should().StartWith("RAIL-NG-20260916-");
        reference.Length.Should().Be("RAIL-NG-20260916-".Length + 12);
    }
}
