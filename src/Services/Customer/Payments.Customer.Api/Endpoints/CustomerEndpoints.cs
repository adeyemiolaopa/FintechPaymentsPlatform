using Payments.Customer.Application.Customers;

namespace Payments.Customer.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers").WithTags("Customers").RequireAuthorization();

        group.MapGet("/me", async (ICustomerProfileService service, CancellationToken cancellationToken) => Results.Ok(await service.GetMeAsync(cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization("customer.read.self");

        group.MapPatch("/me", async (UpdateCustomerProfileRequest request, ICustomerProfileService service, CancellationToken cancellationToken) =>
        {
            await service.UpdateMeAsync(request, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("customer.update.self");

        group.MapGet("/{customerId:guid}", async (Guid customerId, ICustomerProfileService service, CancellationToken cancellationToken) => Results.Ok(await service.GetByIdAsync(customerId, cancellationToken).ConfigureAwait(false)))
            .RequireAuthorization("customer.read.any");

        group.MapPost("/{customerId:guid}/suspend", async (Guid customerId, SuspendCustomerRequest request, ICustomerProfileService service, CancellationToken cancellationToken) =>
        {
            await service.SuspendAsync(customerId, request, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("customer.suspend");

        group.MapPost("/{customerId:guid}/activate", async (Guid customerId, ICustomerProfileService service, CancellationToken cancellationToken) =>
        {
            await service.ActivateAsync(customerId, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization("customer.activate");

        return endpoints;
    }
}
