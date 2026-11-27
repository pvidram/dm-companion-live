using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Domain.Users;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DmCompanionLive.Api.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_table_with_characters_round_trips()
    {
        var user = DmUser.Create($"dev:{Guid.NewGuid()}", "Ana", Now);
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
        loaded.Combatants.Single(c => c.Id == session.CurrentTurnId).LastTurnRound.Should().Be(1);
        var goblin = loaded.Combatants.Single(c => c.Kind == CombatantKind.Monster);
        goblin.CurrentHp.Should().Be(4);
        goblin.Conditions.Should().Be(Condition.Prone | Condition.Poisoned);
        (goblin.SourceKind, goblin.SourceId).Should().Be((MonsterSourceKind.Srd, "goblin"));
        (goblin.InitiativeBonus, goblin.DexModifier).Should().Be((4, 2));
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
        var duplicate = CombatSession.Create(session.TableId, session.OwnerId, session.JoinCode, [], Now);

        await using var db = postgres.CreateDbContext();
        db.Sessions.Add(duplicate);
        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task External_ids_are_unique()
    {
        var externalId = $"dev:{Guid.NewGuid()}";
        await using var db = postgres.CreateDbContext();
        db.Users.Add(DmUser.Create(externalId, "Ana", Now));
        db.Users.Add(DmUser.Create(externalId, "Bea", Now));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_session_must_belong_to_an_existing_table()
    {
        var orphan = CombatSession.Create(Guid.NewGuid(), Guid.NewGuid(), JoinCode.New(), [], Now);

        await using var db = postgres.CreateDbContext();
        db.Sessions.Add(orphan);
        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Combatants_added_and_removed_on_a_loaded_session_are_saved()
    {
        var session = await SavedSessionAsync();
        var dm = new Actor.Dm(session.OwnerId);
        var lyra = session.Combatants.Single(c => c.Kind == CombatantKind.Pc).Id;
        Guid orc;
        await using (var db = postgres.CreateDbContext())
        {
            var loaded = await db.Sessions.SingleAsync(s => s.Id == session.Id);
            orc = loaded.AddMonster(dm, new MonsterSeed("Orc", 15, 13), Now);
            loaded.Remove(dm, lyra, Now);
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateDbContext();
        var reloaded = await read.Sessions.SingleAsync(s => s.Id == session.Id);

        reloaded.Combatants.Select(c => c.Name).Should().BeEquivalentTo("Goblin", "Orc");
        reloaded.Combatants.Should().Contain(c => c.Id == orc);
        reloaded.Version.Should().Be(session.Version + 2);
    }

    [Fact]
    public async Task Log_entries_round_trip_with_their_parameters()
    {
        var session = await SavedSessionAsync();
        await using (var db = postgres.CreateDbContext())
        {
            db.CombatLog.AddRange(session.UncommittedEvents.Select(e => CombatLogEntry.From(session.Id, e)));
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateDbContext();
        var entries = await read.CombatLog.Where(e => e.SessionId == session.Id).OrderBy(e => e.Version).ToListAsync();

        entries.Select(e => e.Type).Should().Equal(session.UncommittedEvents.Select(e => e.Type));
        var damage = entries.Single(e => e.Type == "DamageApplied");
        damage.ReadParameters().Should().Contain("amount", "3");
        damage.At.Should().Be(Now);
        damage.VisibleToPlayers.Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_a_table_deletes_its_sessions_and_their_log()
    {
        var session = await SavedSessionAsync();
        await using (var db = postgres.CreateDbContext())
        {
            db.CombatLog.AddRange(session.UncommittedEvents.Select(e => CombatLogEntry.From(session.Id, e)));
            await db.SaveChangesAsync();
            db.Tables.Remove(await db.Tables.SingleAsync(t => t.Id == session.TableId));
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateDbContext();
        (await read.Sessions.AnyAsync(s => s.Id == session.Id)).Should().BeFalse();
        (await read.CombatLog.AnyAsync(e => e.SessionId == session.Id)).Should().BeFalse();
    }

    private async Task<CombatSession> SavedSessionAsync()
    {
        var user = DmUser.Create($"dev:{Guid.NewGuid()}", "Ana", Now);
        var table = Table.Create(user.Id, "Costa de Sal", Now);
        var dm = new Actor.Dm(user.Id);
        var session = CombatSession.Create(table.Id, user.Id, JoinCode.New(), [new PcSeed("Lyra", 20, 15)], Now);
        var goblin = session.AddMonster(dm, new MonsterSeed("Goblin", 7, 15, DexModifier: 2, Source: MonsterSource.Srd("goblin"), InitiativeBonus: 4), Now);
        session.ApplyDamage(dm, goblin, 3, Now);
        session.SetConditions(dm, goblin, Condition.Prone | Condition.Poisoned, Now);
        session.Start(dm, Now);

        await using var db = postgres.CreateDbContext();
        db.Users.Add(user);
        db.Tables.Add(table);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task Templates_and_favourites_round_trip_are_unique_per_dm_and_go_with_their_dm()
    {
        var user = DmUser.Create($"dev:{Guid.NewGuid()}", "Ana", Now);
        var template = MonsterTemplate.Create(
            user.Id, new MonsterTemplateData("Bandido veterano", 30, 14, 3, "4d8 + 12", "Lleva una ballesta."), Now);
        await using (var db = postgres.CreateDbContext())
        {
            db.Users.Add(user);
            db.MonsterTemplates.Add(template);
            db.FavoriteMonsters.Add(FavoriteMonster.Create(user.Id, "owlbear", Now));
            await db.SaveChangesAsync();
        }

        await using (var read = postgres.CreateDbContext())
        {
            var loaded = await read.MonsterTemplates.SingleAsync(t => t.Id == template.Id);
            (loaded.Name, loaded.HpFormula, loaded.Notes).Should().Be(("Bandido veterano", "4d8+12", "Lleva una ballesta."));
            (await read.FavoriteMonsters.SingleAsync(f => f.OwnerId == user.Id)).SrdSlug.Should().Be("owlbear");
        }

        await using (var db = postgres.CreateDbContext())
        {
            db.FavoriteMonsters.Add(FavoriteMonster.Create(user.Id, "owlbear", Now));
            var duplicate = () => db.SaveChangesAsync();
            (await duplicate.Should().ThrowAsync<DbUpdateException>())
                .Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        }

        await using (var db = postgres.CreateDbContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == user.Id));
            await db.SaveChangesAsync();
        }

        await using var after = postgres.CreateDbContext();
        (await after.MonsterTemplates.AnyAsync(t => t.OwnerId == user.Id)).Should().BeFalse();
        (await after.FavoriteMonsters.AnyAsync(f => f.OwnerId == user.Id)).Should().BeFalse();
    }
}
