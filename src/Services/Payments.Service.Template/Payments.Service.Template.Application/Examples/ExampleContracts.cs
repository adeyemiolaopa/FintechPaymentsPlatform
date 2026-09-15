using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Service.Template.Domain.Examples;

namespace Payments.Service.Template.Application.Examples;

public sealed record CreateExampleRequest(string Name, string Email, string ExternalReference);

public sealed record CreateExampleResponse(Guid Id, string Name, string Email, DateTimeOffset CreatedAtUtc);

public sealed record ExampleResponse(Guid Id, string Name, string Email, DateTimeOffset CreatedAtUtc);

public sealed record CreateExampleCommand(string Name, string Email, string ExternalReference) : ICommand<CreateExampleResponse>;

public sealed record GetExampleByIdQuery(Guid Id) : IQuery<ExampleResponse>;

public interface IExampleRepository
{
    Task AddAsync(Example example, CancellationToken cancellationToken = default);

    Task<Example?> GetByIdAsync(ExampleId id, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
}
