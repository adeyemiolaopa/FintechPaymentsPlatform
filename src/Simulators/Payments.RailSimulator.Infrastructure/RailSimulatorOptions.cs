namespace Payments.RailSimulator.Infrastructure;

public sealed class RailSimulatorDatabaseOptions
{
    public const string SectionName = "RailSimulatorDatabase";
    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_rail_simulator;Username=payments;Password=change-me-local-only";
}

public sealed class RailSimulatorOptions
{
    public const string SectionName = "RailSimulator";
    public string DefaultClientId { get; init; } = "fintech-platform";
    public string DefaultApiKey { get; init; } = "local-rail-api-key";
    public string DefaultClientSecret { get; init; } = "local-rail-client-secret";
    public bool RequireRequestSignature { get; init; } = true;
    public string AdminApiKey { get; init; } = "local-rail-admin-key";
    public int WorkerIntervalMs { get; init; } = 500;
    public int CallbackTimeoutSeconds { get; init; } = 5;
}
