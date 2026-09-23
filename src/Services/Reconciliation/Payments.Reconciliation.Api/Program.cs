using Payments.BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.BuildingBlocks.Infrastructure.Security;
using Payments.Reconciliation.Api;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 150_000_000);
builder.Services.AddProblemDetails();
builder.Services.AddRuntimeContext();
builder.Services.AddPaymentsObservability(builder.Configuration);
builder.Services.AddJwtBearerAuthentication(builder.Configuration);
builder.Services.AddPermissionAuthorization("reconciliation.read", "reconciliation.run", "reconciliation.exception.assign", "reconciliation.exception.resolve", "reconciliation.file.upload", "reconciliation.audit.read");
builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection("Reconciliation"));
builder.Services.Configure<ReconciliationSourceOptions>(builder.Configuration.GetSection("ReconciliationSources"));
builder.Services.Configure<Payments.BuildingBlocks.Messaging.Events.KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<ReconciliationOutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.Configure<ReconciliationControlOptions>(builder.Configuration.GetSection("Controls"));
builder.Services.Configure<ReconciliationPaymentConsumerOptions>(builder.Configuration.GetSection("PaymentConsumer"));
builder.Services.Configure<ReconciliationLedgerConsumerOptions>(builder.Configuration.GetSection("LedgerConsumer"));
var settings = builder.Configuration.GetSection("Reconciliation").Get<ReconciliationOptions>() ?? new ReconciliationOptions();
builder.Services.AddDbContext<ReconciliationDbContext>(options => options.UseNpgsql(settings.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
builder.Services.AddSingleton<ISettlementFileStore>(new LocalSettlementFileStore(settings.FileStorePath));
builder.Services.AddScoped<IProviderReconciliationPolicy, SimulatorReconciliationPolicy>();
builder.Services.AddScoped<IReconciliationSources, HttpReconciliationSources>();
builder.Services.AddScoped<ReconciliationProcessor>();
builder.Services.AddScoped<ReconciliationExceptionWorkflow>();
builder.Services.AddScoped<PaymentReferenceHandler>();
builder.Services.AddScoped<LedgerReferenceHandler>();
builder.Services.AddHttpClient("payment");
builder.Services.AddHttpClient("ledger");
builder.Services.AddHostedService<SettlementProcessingWorker>();
builder.Services.AddHostedService<ActiveReconciliationWorker>();
builder.Services.AddHostedService<ReconciliationOutboxPublisher>();
builder.Services.AddHostedService<ReconciliationControlMonitor>();
builder.Services.AddHostedService<PaymentReferenceConsumer>();
builder.Services.AddHostedService<LedgerReferenceConsumer>();
builder.Services.AddHealthChecks().AddCheck<ReconciliationDbHealthCheck>("postgres");

var app = builder.Build();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>().Database.MigrateAsync();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready");
app.MapReconciliationEndpoints();
app.Run();

public partial class Program;
