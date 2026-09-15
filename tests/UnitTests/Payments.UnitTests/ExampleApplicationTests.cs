using FluentAssertions;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.Service.Template.Application.Examples;
using Payments.Service.Template.Domain.Examples;

namespace Payments.UnitTests;

public sealed class ExampleApplicationTests
{
    [Fact]
    public async Task Create_example_validator_rejects_missing_and_invalid_values()
    {
        var validator = new CreateExampleRequestValidator();

        var result = await validator.ValidateAsync(new CreateExampleRequest("", "not-email", "bad value!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(["Name", "Email", "ExternalReference"]);
    }

    [Fact]
    public async Task Create_handler_persists_and_caches_example()
    {
        var repository = new FakeExampleRepository();
        var cache = new FakeCacheService();
        var handler = new CreateExampleCommandHandler(repository, repository, new FixedClock(), cache);

        var response = await handler.HandleAsync(new CreateExampleCommand("Demo", "demo@example.com", "external-123"));

        response.Id.Should().NotBeEmpty();
        repository.Saved.Should().BeTrue();
        (await cache.ExistsAsync($"example:{response.Id:D}")).Should().BeTrue();
    }

    [Fact]
    public async Task Create_handler_rejects_duplicate_email()
    {
        var repository = new FakeExampleRepository();
        var existing = Example.Create("Demo", "demo@example.com", DateTimeOffset.UtcNow);
        await repository.AddAsync(existing);
        await repository.SaveChangesAsync();
        var handler = new CreateExampleCommandHandler(repository, repository, new FixedClock(), new FakeCacheService());

        var act = () => handler.HandleAsync(new CreateExampleCommand("Again", "demo@example.com", "external-456"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 9, 14, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeExampleRepository : IExampleRepository, IUnitOfWork
    {
        private readonly List<Example> _examples = [];

        public bool Saved { get; private set; }

        public Task AddAsync(Example example, CancellationToken cancellationToken = default)
        {
            _examples.Add(example);
            return Task.CompletedTask;
        }

        public Task<Example?> GetByIdAsync(ExampleId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_examples.SingleOrDefault(example => example.Id == id));
        }

        public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_examples.Any(example => example.Email == email));
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saved = true;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCacheService : ICacheService
    {
        private readonly HashSet<string> _keys = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            _keys.Add(key);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _keys.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_keys.Contains(key));
    }
}
