using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public ApiFactory Factory { get; private set; } = null!;

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Changes a stored session directly through the domain, bypassing the API.</summary>
    public async Task ChangeSessionAsync(Guid sessionId, Action<CombatSession> change)
    {
        await using var db = CreateDbContext();
        var session = await db.Sessions.SingleAsync(s => s.Id == sessionId);
        change(session);
        await db.SaveChangesAsync();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        Factory = new ApiFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
