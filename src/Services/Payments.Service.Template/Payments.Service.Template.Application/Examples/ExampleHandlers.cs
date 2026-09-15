using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.Service.Template.Domain.Examples;

namespace Payments.Service.Template.Application.Examples;

public sealed class CreateExampleCommandHandler : ICommandHandler<CreateExampleCommand, CreateExampleResponse>
{
    private readonly IExampleRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ICacheService _cache;

    public CreateExampleCommandHandler(IExampleRepository repository, IUnitOfWork unitOfWork, IClock clock, ICacheService cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _cache = cache;
    }

    public async Task<CreateExampleResponse> HandleAsync(CreateExampleCommand command, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        if (await _repository.ExistsByEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("An example with the supplied email already exists.");
        }

        var example = Example.Create(command.Name, normalizedEmail, _clock.UtcNow);
        await _repository.AddAsync(example, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = new CreateExampleResponse(example.Id.Value, example.Name, example.Email, example.CreatedAtUtc);
        await _cache.SetAsync($"example:{example.Id.Value:D}", response, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class GetExampleByIdQueryHandler : IQueryHandler<GetExampleByIdQuery, ExampleResponse>
{
    private readonly IExampleRepository _repository;
    private readonly ICacheService _cache;

    public GetExampleByIdQueryHandler(IExampleRepository repository, ICacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<ExampleResponse> HandleAsync(GetExampleByIdQuery query, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"example:{query.Id:D}";
        var cached = await _cache.GetAsync<ExampleResponse>(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var example = await _repository.GetByIdAsync(ExampleId.From(query.Id), cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Example", query.Id.ToString("D"));

        var response = new ExampleResponse(example.Id.Value, example.Name, example.Email, example.CreatedAtUtc);
        await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        return response;
    }
}
