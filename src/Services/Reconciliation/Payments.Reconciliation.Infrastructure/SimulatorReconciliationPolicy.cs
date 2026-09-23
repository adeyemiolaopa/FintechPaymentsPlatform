using Payments.Reconciliation.Application;

namespace Payments.Reconciliation.Infrastructure;

public sealed class SimulatorReconciliationPolicy : IProviderReconciliationPolicy
{
    public bool IsSuccess(string provider, string status) => status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase) || status.Equals("Successful", StringComparison.OrdinalIgnoreCase);
    public bool IsFailure(string provider, string status) => status.Equals("FAILED", StringComparison.OrdinalIgnoreCase) || status.Equals("Failure", StringComparison.OrdinalIgnoreCase);
    public TimeSpan MissingReferenceGrace(string provider) => TimeSpan.FromMinutes(5);
    public TimeSpan SettlementDelay(string provider) => TimeSpan.FromDays(1);
}
