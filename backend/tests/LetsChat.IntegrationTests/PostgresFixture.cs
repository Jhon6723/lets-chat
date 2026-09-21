using LetsChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace LetsChat.IntegrationTests;

/// <summary>
/// One ephemeral Postgres container per test run. Migrations are applied from
/// zero at startup — the schema under test is exactly what production runs.
/// Shared across the suite via the "postgres" collection.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public IDbContextFactory<LetsChatDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<LetsChatDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        Factory = new TestDbContextFactory(options);
        await using var ctx = Factory.CreateDbContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

internal sealed class TestDbContextFactory(DbContextOptions<LetsChatDbContext> options)
    : IDbContextFactory<LetsChatDbContext>
{
    public LetsChatDbContext CreateDbContext() => new(options);

    public Task<LetsChatDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(new LetsChatDbContext(options));
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
