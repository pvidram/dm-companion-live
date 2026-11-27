using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DmCompanionLive.Api.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Existing_combatants_keep_their_dex_as_initiative_bonus_and_their_srd_source()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await db.GetService<IRelationalDatabaseCreator>().CreateAsync();
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("RealTimeCombat");
            Guid user = Guid.NewGuid(), table = Guid.NewGuid(), session = Guid.NewGuid();
            Guid goblin = Guid.NewGuid(), orc = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO dm_users ("Id", "ExternalId", "DisplayName", "CreatedAt")
                VALUES ({user}, 'dev:migration', 'Ana', now())
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tables ("Id", "OwnerId", "Name", "CreatedAt") VALUES ({table}, {user}, 'Costa de Sal', now())
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO combat_sessions
                    ("Id", "TableId", "OwnerId", "JoinCode", "Status", "Round", "CurrentTurnId", "Version", "LastActivityAt")
                VALUES ({session}, {table}, {user}, 'ABC234', 'Lobby', 0, NULL, 2, now())
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO combatants
                    ("Id", "Kind", "Name", "Initiative", "DexModifier", "MaxHp", "CurrentHp", "TempHp", "Ac",
                     "Conditions", "IsHidden", "SourceRef", "ClaimedBy", "SortOrder", "SessionId")
                VALUES
                    ({goblin}, 'Monster', 'Goblin', NULL, 2, 7, 7, 0, 15, 0, false, 'srd:goblin', NULL, 0, {session}),
                    ({orc}, 'Monster', 'Orc', NULL, 1, 15, 15, 0, 13, 0, false, NULL, NULL, 1, {session})
                """);

            await db.Database.MigrateAsync();

            var loaded = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == session);
            var migratedGoblin = loaded.Combatants.Single(c => c.Id == goblin);
            (migratedGoblin.InitiativeBonus, migratedGoblin.SourceKind, migratedGoblin.SourceId)
                .Should().Be((2, MonsterSourceKind.Srd, "goblin"));
            var migratedOrc = loaded.Combatants.Single(c => c.Id == orc);
            (migratedOrc.InitiativeBonus, migratedOrc.SourceKind, migratedOrc.SourceId)
                .Should().Be((1, MonsterSourceKind.None, (string?)null));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
