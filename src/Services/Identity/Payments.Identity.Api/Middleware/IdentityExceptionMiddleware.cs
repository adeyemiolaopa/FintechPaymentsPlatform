using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.Identity.Application.Security;

namespace Payments.Identity.Api.Middleware;

public sealed class IdentityExceptionMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<IdentityExceptionMiddleware> _logger;

    public IdentityExceptionMiddleware(RequestDelegate next, ILogger<IdentityExceptionMiddleware> logger)
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
                _logger.LogError(exception, "Unhandled identity exception for {Path}", context.Request.Path);
            }
            else
            {
                _logger.LogWarning("Identity request failed with {Status} for {Path}", problem.Status, context.Request.Path);
            }

            context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions)).ConfigureAwait(false);
        }
    }

    private static ProblemDetails CreateProblem(HttpContext context, Exception exception) => exception switch
    {
        FluentValidation.ValidationException validation => new ValidationProblemDetails(validation.Errors.GroupBy(error => error.PropertyName).ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())) { Status = StatusCodes.Status400BadRequest, Title = "Validation failed", Instance = context.Request.Path },
        ConflictException conflict => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = conflict.Message, Instance = context.Request.Path },
        InvalidCredentialsException invalid => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = invalid.Message, Instance = context.Request.Path },
        LockedIdentityException locked => new ProblemDetails { Status = StatusCodes.Status423Locked, Title = "Locked", Detail = locked.Message, Instance = context.Request.Path },
        UnauthorizedAccessException unauthorized => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = unauthorized.Message, Instance = context.Request.Path },
        _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Unexpected error", Instance = context.Request.Path },
    };
}

