using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Payments.Account.Api.Endpoints;
using Payments.Account.Api.Middleware;
using Payments.Account.Application.Accounts;
using Payments.Account.Infrastructure;
using Payments.Account.Infrastructure.Persistence;
using Payments.BuildingBlocks.Infrastructure.Caching;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.BuildingBlocks.Infrastructure.Security;
using Payments.BuildingBlocks.Observability;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
    builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services).Enrich.FromLogContext().Enrich.WithProperty("ServiceName", "account-api").WriteTo.Console());

    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Account API", Version = "v1" });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    });
    builder.Services.AddOpenApi();
    builder.Services.AddRuntimeContext();
    builder.Services.AddAccountApplication();
    builder.Services.AddAccountInfrastructure(builder.Configuration);
    builder.Services.AddRedisCache(builder.Configuration);
    builder.Services.AddPaymentsObservability(builder.Configuration);
    builder.Services.AddJwtBearerAuthentication(builder.Configuration);
    builder.Services.AddPermissionAuthorization("account.read.self", "account.read.any", "account.create", "account.freeze", "account.unfreeze", "account.close", "account.restrict", "account.unrestrict", "beneficiary.read.self", "beneficiary.create", "beneficiary.remove");
    builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    var app = builder.Build();

    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountDbContext>().Database.MigrateAsync();
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<AccountExceptionMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Account API v1"));
    }

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live"), ResponseWriter = WriteHealthResponseAsync });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = _ => true, ResponseWriter = WriteHealthResponseAsync });
    app.MapAccountEndpoints();

    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Account API terminated unexpectedly");
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
