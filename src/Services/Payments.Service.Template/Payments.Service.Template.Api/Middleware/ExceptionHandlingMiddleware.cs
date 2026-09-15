using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Domain.Primitives;
using ValidationException = Payments.BuildingBlocks.Application.Exceptions.ValidationException;

namespace Payments.Service.Template.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, title, type) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "https://errors.example.com/validation"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Domain rule violated", "https://errors.example.com/domain"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "https://errors.example.com/not-found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", "https://errors.example.com/conflict"),
            UnauthorizedApplicationException => (StatusCodes.Status401Unauthorized, "Unauthorized", "https://errors.example.com/unauthorized"),
            ForbiddenApplicationException => (StatusCodes.Status403Forbidden, "Forbidden", "https://errors.example.com/forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "https://errors.example.com/unexpected"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed with {StatusCode} for {Method} {Path}", status, context.Request.Method, context.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = status >= 500 && !_environment.IsDevelopment() ? "An unexpected error occurred." : exception.Message,
            Instance = context.Request.Path,
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        problem.Extensions["correlationId"] = context.Items["CorrelationId"]?.ToString() ?? string.Empty;

        if (exception is ValidationException validationException)
        {
            problem.Extensions["errors"] = validationException.Errors;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = status;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web))).ConfigureAwait(false);
    }
}
