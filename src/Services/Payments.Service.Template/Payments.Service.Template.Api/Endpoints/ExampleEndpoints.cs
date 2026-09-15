using FluentValidation;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.Service.Template.Application.Examples;
using ValidationException = Payments.BuildingBlocks.Application.Exceptions.ValidationException;

namespace Payments.Service.Template.Api.Endpoints;

public static class ExampleEndpoints
{
    public static IEndpointRouteBuilder MapExampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/examples")
            .WithTags("Examples");

        group.MapPost("/", CreateExampleAsync)
            .WithName("CreateExample")
            .WithSummary("Create a trivial template example")
            .Produces<CreateExampleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetExampleAsync)
            .WithName("GetExampleById")
            .WithSummary("Get a trivial template example by id")
            .Produces<ExampleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateExampleAsync(
        CreateExampleRequest request,
        IValidator<CreateExampleRequest> validator,
        ICommandHandler<CreateExampleCommand, CreateExampleResponse> handler,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
            throw new ValidationException(errors);
        }

        var response = await handler.HandleAsync(new CreateExampleCommand(request.Name, request.Email, request.ExternalReference), cancellationToken).ConfigureAwait(false);
        return Results.Created($"/api/v1/examples/{response.Id:D}", response);
    }

    private static async Task<IResult> GetExampleAsync(
        Guid id,
        IQueryHandler<GetExampleByIdQuery, ExampleResponse> handler,
        CancellationToken cancellationToken)
    {
        var response = await handler.HandleAsync(new GetExampleByIdQuery(id), cancellationToken).ConfigureAwait(false);
        return Results.Ok(response);
    }
}
