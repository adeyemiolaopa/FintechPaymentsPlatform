using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Payments.BuildingBlocks.Infrastructure.Caching;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.BuildingBlocks.Observability;
using Payments.Service.Template.Api.Endpoints;
using Payments.Service.Template.Api.Health;
using Payments.Service.Template.Api.Middleware;
using Payments.Service.Template.Api.OpenApi;
using Payments.Service.Template.Application;
using Payments.Service.Template.Infrastructure;
using Payments.Service.Template.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", "payments-template-api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
        .WriteTo.Console());

    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Payments Template API",
            Version = "v1",
            Description = "Week 1 template API proving platform building blocks.",
        });
        options.OperationFilter<CorrelationHeaderOperationFilter>();
    });
    builder.Services.AddOpenApi();

    builder.Services.AddRuntimeContext();
    builder.Services.AddTemplateApplication();
    builder.Services.AddTemplateInfrastructure(builder.Configuration);
    builder.Services.AddRedisCache(builder.Configuration);
    builder.Services.AddKafkaMessaging(builder.Configuration);
    builder.Services.AddPaymentsObservability(builder.Configuration);

    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
        .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
        .AddCheck<KafkaHealthCheck>("kafka", tags: ["ready"]);

    var app = builder.Build();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Database.EnsureCreatedAsync();
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]?.ToString());
            diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
            diagnosticContext.Set("HttpMethod", httpContext.Request.Method);
            diagnosticContext.Set("TraceId", Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);
            diagnosticContext.Set("SpanId", Activity.Current?.SpanId.ToString() ?? string.Empty);
        };
    });
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseHttpsRedirection();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payments Template API v1"));
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
        ResponseWriter = WriteHealthResponseAsync,
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponseAsync,
    });
    app.MapExampleEndpoints();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Template API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            duration = entry.Value.Duration.TotalMilliseconds,
        }),
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}

public partial class Program;
