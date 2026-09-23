using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Payments.BuildingBlocks.Observability;

public static class PaymentsTelemetry
{
    public const string ActivitySourceName = "Payments.Platform";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public string ServiceName { get; init; } = "payments-template-api";

    public string ServiceVersion { get; init; } = "0.1.0";

    public bool EnableOtlpExporter { get; init; } = true;
}

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentsObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var resource = ResourceBuilder.CreateDefault()
            .AddService(options.ServiceName, serviceVersion: options.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            });

        services.AddOpenTelemetry()
            .ConfigureResource(builder => builder.AddService(options.ServiceName, serviceVersion: options.ServiceVersion))
            .WithTracing(builder =>
            {
                builder.SetResourceBuilder(resource)
                    .AddSource(PaymentsTelemetry.ActivitySourceName, "Payments.Reconciliation")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (options.EnableOtlpExporter)
                {
                    builder.AddOtlpExporter();
                }
            })
            .WithMetrics(builder =>
            {
                builder.SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Payments.Reconciliation");

                if (options.EnableOtlpExporter)
                {
                    builder.AddOtlpExporter();
                }
            });

        return services;
    }
}
