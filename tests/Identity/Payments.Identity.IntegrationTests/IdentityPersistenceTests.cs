using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Payments.Identity.Domain.Users;
using Payments.Identity.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Payments.Identity.IntegrationTests;

public sealed class IdentityPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;

    public IdentityPersistenceTests()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", Environment.GetEnvironmentVariable("DOCKER_API_VERSION") ?? "1.41");
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    }

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrations_create_identity_schema_and_seed_authorization_data()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        (await dbContext.Roles.CountAsync()).Should().BeGreaterThanOrEqualTo(4);
        (await dbContext.Permissions.AnyAsync(permission => permission.Name == "customer.suspend")).Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_by_database_unique_constraint()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var role = await dbContext.Roles.SingleAsync(role => role.Id == IdentitySeed.CustomerRoleId);
        var first = User.Register(Guid.NewGuid(), "same@example.com", "+2348012345678", "hash", DateTimeOffset.UtcNow);
        first.AssignRole(role);
        dbContext.Users.Add(first);
        await dbContext.SaveChangesAsync();

        var duplicate = User.Register(Guid.NewGuid(), "SAME@example.com", "+2348012345679", "hash", DateTimeOffset.UtcNow);
        duplicate.AssignRole(role);
        dbContext.Users.Add(duplicate);
        var act = async () => await dbContext.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private IdentityDbContext CreateContext()
        => new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
