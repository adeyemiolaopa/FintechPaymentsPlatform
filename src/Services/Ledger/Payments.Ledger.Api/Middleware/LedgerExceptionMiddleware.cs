using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Ledger.Api.Middleware;

public sealed class LedgerExceptionMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<LedgerExceptionMiddleware> _logger;

    public LedgerExceptionMiddleware(RequestDelegate next, ILogger<LedgerExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var problem = CreateProblem(context, exception);
            if (problem.Status >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(exception, "Unhandled ledger exception for {Path}", context.Request.Path);
            }
            else
            {
                _logger.LogWarning("Ledger request failed with {Status} for {Path}", problem.Status, context.Request.Path);
            }

            context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions)).ConfigureAwait(false);
        }
    }

    private static ProblemDetails CreateProblem(HttpContext context, Exception exception) => exception switch
    {
        FluentValidation.ValidationException validation => new ValidationProblemDetails(validation.Errors.GroupBy(error => error.PropertyName).ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())) { Status = StatusCodes.Status400BadRequest, Title = "Validation failed", Instance = context.Request.Path },
        NotFoundException notFound => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = notFound.Message, Instance = context.Request.Path },
        ForbiddenApplicationException forbidden => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden", Detail = forbidden.Message, Instance = context.Request.Path },
        UnauthorizedApplicationException unauthorized => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = unauthorized.Message, Instance = context.Request.Path },
        ConflictException conflict => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = conflict.Message, Instance = context.Request.Path },
        DomainException domain => new ProblemDetails { Status = domain.Code.Contains("unbalanced", StringComparison.OrdinalIgnoreCase) || domain.Code.Contains("currency", StringComparison.OrdinalIgnoreCase) || domain.Code.Contains("minimum", StringComparison.OrdinalIgnoreCase) || domain.Code.Contains("positive", StringComparison.OrdinalIgnoreCase) ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict, Title = "Invalid ledger state", Detail = domain.Message, Instance = context.Request.Path },
        UnauthorizedAccessException unauthorized => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = unauthorized.Message, Instance = context.Request.Path },
        _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Unexpected error", Instance = context.Request.Path },
    };
}
