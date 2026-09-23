using NetArchTest.Rules;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.ArchitectureTests;

public sealed class ReconciliationBoundaryTests
{
    [Fact]
    public void Domain_does_not_depend_on_service_implementations()
    {
        var result = Types.InAssembly(typeof(ReconciliationRun).Assembly).ShouldNot()
            .HaveDependencyOnAny("Payments.Reconciliation.Infrastructure", "Payments.Reconciliation.Api",
                "Payments.Payment.Infrastructure", "Payments.Ledger.Infrastructure", "Payments.Account.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Application_uses_only_evidence_contracts_not_service_databases()
    {
        var result = Types.InAssembly(typeof(IReconciliationSources).Assembly).ShouldNot()
            .HaveDependencyOnAny("Payments.Reconciliation.Infrastructure", "Payments.Reconciliation.Api",
                "Payments.Payment.Infrastructure", "Payments.Ledger.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Infrastructure_does_not_reference_payment_or_ledger_implementations()
    {
        var result = Types.InAssembly(typeof(ReconciliationDbContext).Assembly).ShouldNot()
            .HaveDependencyOnAny("Payments.Payment.Infrastructure", "Payments.Ledger.Infrastructure", "Payments.Account.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }
    [Fact]
    public void Reconciliation_live_consumers_use_inbox_consumer_base()
    {
        Assert.True(IsInboxConsumer(typeof(PaymentReferenceConsumer)));
        Assert.True(IsInboxConsumer(typeof(LedgerReferenceConsumer)));
    }

    private static bool IsInboxConsumer(Type type)
    {
        while (type.BaseType is not null)
        {
            if (type.BaseType.IsGenericType && type.BaseType.GetGenericTypeDefinition() == typeof(Payments.BuildingBlocks.Messaging.Events.KafkaInboxConsumer<,,>)) return true;
            type = type.BaseType;
        }
        return false;
    }
}
