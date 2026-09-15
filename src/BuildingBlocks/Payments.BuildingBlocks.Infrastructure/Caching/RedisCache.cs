using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;
using StackExchange.Redis;

namespace Payments.BuildingBlocks.Infrastructure.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";

    public string KeyPrefix { get; init; } = "dev:template";
}

public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database;
    private readonly RedisOptions _options;

    public RedisCacheService(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        _database = connection.GetDatabase();
        _options = options.Value;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(FormatKey(key)).WaitAsync(cancellationToken).ConfigureAwait(false);
        return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString(), SerializerOptions) : default;
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        return _database.StringSetAsync(FormatKey(key), json, ttl, When.Always).WaitAsync(cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return _database.KeyDeleteAsync(FormatKey(key)).WaitAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return _database.KeyExistsAsync(FormatKey(key)).WaitAsync(cancellationToken);
    }

    private RedisKey FormatKey(string key) => $"{_options.KeyPrefix}:{key}";
}

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Redis connection string is required")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.ConnectionString);
        });
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }
}
