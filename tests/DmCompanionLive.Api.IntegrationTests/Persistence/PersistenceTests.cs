using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_table_with_characters_round_trips()
    {
        var user = DmUser.Create($"dev:{Guid.NewGuid()}", "Pau", Now);
        var table = Table.Create(user.Id, "Costa de Sal", Now);
        table.AddCharacter("Lyra", 20, 15, 2);
        await using (var db = postgres.CreateDbContext())
        {
            db.Users.Add(user);
            db.Tables.Add(table);
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateDbContext();
        var loaded = await read.Tables.SingleAsync(t => t.Id == table.Id);

        loaded.Name.Should().Be("Costa de Sal");
        loaded.Characters.Should().ContainSingle().Which.Name.Should().Be("Lyra");
    }

    [Fact]
    public async Task A_combat_session_round_trips_with_its_combatants()
    {
        var session = await SavedSessionAsync();

        await using var read = postgres.CreateDbContext();
        var loaded = await read.Sessions.SingleAsync(s => s.Id == session.Id);

        loaded.Status.Should().Be(SessionStatus.Active);
        loaded.Round.Should().Be(1);
        loaded.CurrentTurnId.Should().Be(session.CurrentTurnId);
        loaded.Version.Should().Be(session.Version);
        loaded.Combatants.Should().HaveCount(2);
        var goblin = loaded.Combatants.Single(c => c.Kind == CombatantKind.Monster);
        goblin.CurrentHp.Should().Be(4);
        goblin.Conditions.Should().Be(Condition.Prone | Condition.Poisoned);
        goblin.SourceRef.Should().Be("srd:goblin");
    }

    [Fact]
    public async Task Concurrent_changes_to_the_same_version_conflict()
    {
        var session = await SavedSessionAsync();
        var dm = new Actor.Dm(session.OwnerId);

        await using var first = postgres.CreateDbContext();
        await using var second = postgres.CreateDbContext();
        var a = await first.Sessions.SingleAsync(s => s.Id == session.Id);
        var b = await second.Sessions.SingleAsync(s => s.Id == session.Id);

        a.NextTurn(dm, Now);
        await first.SaveChangesAsync();
        b.NextTurn(dm, Now);
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Join_codes_are_unique()
    {
        var session = await SavedSessionAsync();
        var duplicate = CombatSession.Create(Guid.NewGuid(), Guid.NewGuid(), session.JoinCode, [], Now);

        await using var db = postgres.CreateDbContext();
        db.Sessions.Add(duplicate);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<CombatSession> SavedSessionAsync()
    {
        var ownerId = Guid.NewGuid();
        var dm = new Actor.Dm(ownerId);
        var session = CombatSession.Create(Guid.NewGuid(), ownerId, JoinCode.New(), [new PcSeed("Lyra", 20, 15)], Now);
        var goblin = session.AddMonster(dm, new MonsterSeed("Goblin", 7, 15, SourceRef: "srd:goblin"), Now);
        session.ApplyDamage(dm, goblin, 3, Now);
        session.SetConditions(dm, goblin, Condition.Prone | Condition.Poisoned, Now);
        session.Start(dm, Now);

        await using var db = postgres.CreateDbContext();
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }
}
