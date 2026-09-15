using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Payments.BuildingBlocks.Infrastructure.Caching;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.BuildingBlocks.Infrastructure.Security;
using Payments.BuildingBlocks.Observability;
using Payments.Customer.Api.Endpoints;
using Payments.Customer.Api.Middleware;
using Payments.Customer.Application;
using Payments.Customer.Infrastructure;
using Payments.Customer.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
    builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext().Enrich.WithProperty("ServiceName", "customer-api").WriteTo.Console());

    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Customer API", Version = "v1" });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    });
    builder.Services.AddOpenApi();
    builder.Services.AddRuntimeContext();
    builder.Services.AddCustomerApplication();
    builder.Services.AddCustomerInfrastructure(builder.Configuration);
    builder.Services.AddRedisCache(builder.Configuration);
    builder.Services.AddPaymentsObservability(builder.Configuration);
    builder.Services.AddJwtBearerAuthentication(builder.Configuration);
    builder.Services.AddPermissionAuthorization("customer.read.self", "customer.update.self", "customer.read.any", "customer.suspend", "customer.activate");
    builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    var app = builder.Build();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.MigrateAsync();
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<CustomerExceptionMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer API v1"));
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live"), ResponseWriter = WriteHealthResponseAsync });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true, ResponseWriter = WriteHealthResponseAsync });
    app.MapCustomerEndpoints();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Customer API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString(), checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() }) }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}

public partial class Program;

