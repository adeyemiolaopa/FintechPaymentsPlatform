using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Domain;
using Payments.RailSimulator.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
    builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext().Enrich.WithProperty("ServiceName", "rail-simulator-api").WriteTo.Console());

    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo { Title = "Payment Rail Simulator API", Version = "v1" }));
    builder.Services.AddOpenApi();
    builder.Services.AddRailSimulatorInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    var app = builder.Build();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        await app.Services.InitializeRailSimulatorAsync();
    }

    app.UseSerilogRequestLogging();
    app.Use(async (context, next) =>
    {
        try
        {
            await next().ConfigureAwait(false);
        }
        catch (RailSimulatorException exception)
        {
            await Results.Problem(exception.Message, statusCode: exception.StatusCode, extensions: new Dictionary<string, object?> { ["code"] = exception.Code }).ExecuteAsync(context).ConfigureAwait(false);
        }
        catch (RailDomainException exception)
        {
            await Results.Problem(exception.Message, statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = exception.Code }).ExecuteAsync(context).ConfigureAwait(false);
        }
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Rail Simulator API v1"));
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live"), ResponseWriter = WriteHealthResponseAsync });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true, ResponseWriter = WriteHealthResponseAsync });

    var transfers = app.MapGroup("/api/v1/transfers");
    transfers.MapPost("/", async (HttpContext httpContext, IRailRequestAuthenticator authenticator, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var rawBody = await ReadBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);
        var requestContext = await AuthenticateAsync(httpContext, authenticator, rawBody, cancellationToken).ConfigureAwait(false);
        if (requestContext.ScenarioOverride is RailScenarioMode.MalformedResponse)
        {
            return Results.Text("{\"providerReference\":", "application/json", statusCode: 200);
        }

        var request = JsonSerializer.Deserialize<SubmitRailTransferRequest>(rawBody, CreateJsonOptions()) ?? throw new RailSimulatorException("rail.invalid_json", "Transfer request body is required.", 400);
        return Results.Ok(await service.SubmitAsync(request, requestContext, cancellationToken).ConfigureAwait(false));
    });
    transfers.MapGet("/{providerReference}", async (string providerReference, IRailSimulatorService service, CancellationToken cancellationToken) => Results.Ok(await service.GetByProviderReferenceAsync(providerReference, cancellationToken).ConfigureAwait(false)));
    transfers.MapGet("/status", async (HttpContext httpContext, string clientReference, IRailRequestAuthenticator authenticator, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var requestContext = await AuthenticateAsync(httpContext, authenticator, string.Empty, cancellationToken).ConfigureAwait(false);
        return Results.Ok(await service.GetByClientReferenceAsync(requestContext.ClientId, clientReference, cancellationToken).ConfigureAwait(false));
    });
    transfers.MapGet("/{providerReference}/callbacks", async (HttpContext httpContext, string providerReference, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        return denied ?? Results.Ok(await service.GetCallbackAttemptsAsync(providerReference, cancellationToken).ConfigureAwait(false));
    });

    var admin = app.MapGroup("/api/v1/admin");
    admin.MapGet("/scenario", async (HttpContext httpContext, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        return denied ?? Results.Ok(await service.GetScenarioAsync(cancellationToken).ConfigureAwait(false));
    });
    admin.MapPut("/scenario", async (HttpContext httpContext, ProviderScenarioRequest request, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        return denied ?? Results.Ok(await service.ConfigureScenarioAsync(request, cancellationToken).ConfigureAwait(false));
    });
    admin.MapPost("/reset", async (HttpContext httpContext, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        if (denied is not null) return denied;
        await service.ResetAsync(cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    });
    admin.MapPost("/settlements/{settlementDate}", async (HttpContext httpContext, DateOnly settlementDate, string? anomaly, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        return denied ?? Results.Ok(await service.GenerateSettlementAsync(settlementDate, anomaly ?? "normal", cancellationToken).ConfigureAwait(false));
    });

    admin.MapPost("/settlements/{settlementDate}/csv", async (HttpContext httpContext, DateOnly settlementDate, string? anomaly, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        if (denied is not null) return denied;
        var records = await service.GenerateSettlementAsync(settlementDate, anomaly ?? "normal", cancellationToken).ConfigureAwait(false);
        var csv = new StringBuilder("provider_reference,client_reference,amount,currency,status,settlement_date\n");
        foreach (var record in records)
            csv.Append(Csv(record.ProviderReference)).Append(',').Append(Csv(record.ClientReference)).Append(',').Append(record.Amount.ToString("0.####", CultureInfo.InvariantCulture)).Append(',').Append(record.Currency).Append(',').Append(record.Status.ToUpperInvariant()).Append(',').Append(record.SettlementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"simulator-rail-{settlementDate:yyyy-MM-dd}.csv");
    });
    app.MapPut("/api/v1/config/callback", async (HttpContext httpContext, CallbackConfigurationRequest request, IOptions<RailSimulatorOptions> options, IRailSimulatorService service, CancellationToken cancellationToken) =>
    {
        var denied = RequireAdmin(httpContext, options.Value);
        return denied ?? Results.Ok(await service.ConfigureCallbackAsync(request, cancellationToken).ConfigureAwait(false));
    });
    app.MapGet("/api/v1/provider/status", async (IRailSimulatorService service, CancellationToken cancellationToken) => Results.Ok(await service.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false)));
    app.MapPost("/api/v1/name-enquiry", async (NameEnquiryRequest request, IRailSimulatorService service, CancellationToken cancellationToken) => Results.Ok(await service.NameEnquiryAsync(request, cancellationToken).ConfigureAwait(false)));
    app.MapGet("/api/v1/banks", async (IRailSimulatorService service, CancellationToken cancellationToken) => Results.Ok(await service.GetBanksAsync(cancellationToken).ConfigureAwait(false)));

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Rail simulator API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web);

static async Task<string> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
{
    using var reader = new StreamReader(context.Request.Body);
    return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
}

static Task<RailRequestContext> AuthenticateAsync(HttpContext context, IRailRequestAuthenticator authenticator, string rawBody, CancellationToken cancellationToken)
    => authenticator.AuthenticateAsync(new RailAuthenticationInput(
        context.Request.Headers["X-Rail-Client-Id"],
        context.Request.Headers["X-Rail-Api-Key"],
        context.Request.Headers["X-Rail-Timestamp"],
        context.Request.Headers["X-Rail-Signature"],
        context.Request.Method,
        context.Request.Path + context.Request.QueryString.ToString(),
        rawBody,
        context.Request.Headers["X-Rail-Scenario"]), cancellationToken);

static string Csv(string value)
{
    if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) return value;
    return $"\"{value.Replace("\"", "\"\"")}\"";
}
static IResult? RequireAdmin(HttpContext context, RailSimulatorOptions options)
{
    var provided = context.Request.Headers["X-Rail-Admin-Key"].ToString();
    return string.Equals(provided, options.AdminApiKey, StringComparison.Ordinal)
        ? null
        : Results.Problem("Rail simulator admin key is required.", statusCode: 401, extensions: new Dictionary<string, object?> { ["code"] = "rail.admin_authentication" });
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString(), checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() }) }, CreateJsonOptions()));
}

public partial class Program;
