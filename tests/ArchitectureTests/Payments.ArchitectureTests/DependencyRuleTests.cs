using NetArchTest.Rules;
using Payments.Service.Template.Domain.Examples;

namespace Payments.ArchitectureTests;

public sealed class DependencyRuleTests
{
    [Fact]
    public void Domain_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Example).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Service.Template.Infrastructure", "Payments.Service.Template.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Application_does_not_depend_on_api_or_infrastructure()
    {
        var result = Types.InAssembly(typeof(Payments.Service.Template.Application.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Service.Template.Api", "Payments.Service.Template.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Infrastructure_may_depend_on_application_but_not_api()
    {
        var result = Types.InAssembly(typeof(Payments.Service.Template.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Payments.Service.Template.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Api_is_the_composition_root()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.Contains("Payments.Service.Template.Application", references);
        Assert.Contains("Payments.Service.Template.Infrastructure", references);
    }

    [Fact]
    public void Identity_domain_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Payments.Identity.Domain.Users.User).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Identity.Infrastructure", "Payments.Identity.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Customer_domain_does_not_depend_on_identity_implementation_or_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Payments.Customer.Domain.Customers.Customer).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Identity.Domain", "Payments.Identity.Infrastructure", "Payments.Identity.Api", "Payments.Customer.Infrastructure", "Payments.Customer.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Customer_infrastructure_does_not_reference_identity_database_or_domain()
    {
        var result = Types.InAssembly(typeof(Payments.Customer.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Identity.Infrastructure", "Payments.Identity.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Account_domain_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Payments.Account.Domain.Accounts.Account).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Account.Infrastructure", "Payments.Account.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Account_application_does_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(Payments.Account.Application.Accounts.IAccountService).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Payments.Account.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Account_service_does_not_reference_customer_or_identity_infrastructure()
    {
        var result = Types.InAssembly(typeof(Payments.Account.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Customer.Infrastructure", "Payments.Identity.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Ledger_domain_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Payments.Ledger.Domain.Ledger.LedgerAccount).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Ledger.Infrastructure", "Payments.Ledger.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Ledger_application_does_not_depend_on_api_or_infrastructure()
    {
        var result = Types.InAssembly(typeof(Payments.Ledger.Application.Ledger.ILedgerService).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Ledger.Api", "Payments.Ledger.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }

    [Fact]
    public void Ledger_service_does_not_reference_other_service_infrastructure()
    {
        var result = Types.InAssembly(typeof(Payments.Ledger.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Payments.Account.Infrastructure", "Payments.Customer.Infrastructure", "Payments.Identity.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypes ?? []));
    }
}
