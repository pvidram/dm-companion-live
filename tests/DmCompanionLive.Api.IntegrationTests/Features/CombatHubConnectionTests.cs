using System.Text.Json;
using DmCompanionLive.Api.Features.Combat;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR.Client;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class CombatHubConnectionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Anonymous_connections_are_rejected()
    {
        var act = () => postgres.Factory.ConnectAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task An_invalid_guest_token_is_rejected()
    {
        var act = () => postgres.Factory.ConnectAsync(guestToken: "not-a-token");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task The_dm_joins_their_session_and_receives_the_full_view()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync("Lyra");
        await using var hub = await postgres.Factory.ConnectAsync(cookie: dm.Cookie);

        var result = await hub.InvokeAsync<SnapshotResult>("Join", session.JoinCode.ToLowerInvariant());

        result.Succeeded.Should().BeTrue();
        result.Player.Should().BeNull();
        result.Dm!.SessionId.Should().Be(session.SessionId);
        result.Dm.Combatants.Should().ContainSingle().Which.MaxHp.Should().Be(20);
    }

    [Fact]
    public async Task A_guest_never_sees_hidden_combatants_or_exact_monster_hp()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync("Lyra");
        await postgres.ChangeSessionAsync(session.SessionId, s =>
        {
            var owner = new Actor.Dm(s.OwnerId);
            s.AddMonster(owner, new MonsterSeed("Assassin", 30, 15, IsHidden: true), DateTimeOffset.UtcNow);
            s.AddMonster(owner, new MonsterSeed("Goblin", 7, 15), DateTimeOffset.UtcNow);
        });
        var guest = await postgres.Factory.JoinAsync(session.JoinCode, "Ana");
        await using var hub = await postgres.Factory.ConnectAsync(guestToken: guest.GuestToken);

        var raw = await hub.InvokeAsync<JsonElement>("Join", session.JoinCode);

        raw.GetRawText().Should().NotContain("Assassin");
        var result = raw.Deserialize<SnapshotResult>(TestJson.Options)!;
        result.Dm.Should().BeNull();
        result.Player!.Combatants.Select(c => c.Name).Should().BeEquivalentTo("Lyra", "Goblin");
        var goblin = result.Player.Combatants.Single(c => c.Name == "Goblin");
        goblin.CurrentHp.Should().BeNull();
        goblin.MaxHp.Should().BeNull();
        goblin.Band.Should().Be(HpBand.Unhurt);
    }

    [Fact]
    public async Task Another_dm_cannot_join_my_session()
    {
        var owner = await postgres.Factory.CreateDmAsync();
        var intruder = await postgres.Factory.CreateDmAsync();
        var session = await owner.Http.CreateSessionAsync("Lyra");
        await using var hub = await postgres.Factory.ConnectAsync(cookie: intruder.Cookie);

        var result = await hub.InvokeAsync<SnapshotResult>("Join", session.JoinCode);

        result.Error.Should().Be(CombatErrors.SessionNotFound);
    }

    [Fact]
    public async Task A_guest_token_only_opens_the_session_it_was_issued_for()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var mine = await dm.Http.CreateSessionAsync("Lyra");
        var other = await dm.Http.CreateSessionAsync("Torvald");
        var guest = await postgres.Factory.JoinAsync(mine.JoinCode, "Ana");
        await using var hub = await postgres.Factory.ConnectAsync(guestToken: guest.GuestToken);

        (await hub.InvokeAsync<SnapshotResult>("Join", other.JoinCode)).Error.Should().Be(CombatErrors.SessionNotFound);
        (await hub.InvokeAsync<SnapshotResult>("Join", mine.JoinCode)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Snapshots_require_joining_first()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        await using var hub = await postgres.Factory.ConnectAsync(cookie: dm.Cookie);

        (await hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Error.Should().Be(CombatErrors.NotJoined);
    }

    [Fact]
    public async Task Enums_travel_as_names_over_the_hub()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync("Lyra");
        await using var hub = await postgres.Factory.ConnectAsync(cookie: dm.Cookie);

        var raw = await hub.InvokeAsync<JsonElement>("Join", session.JoinCode);

        raw.GetProperty("dm").GetProperty("status").GetString().Should().Be("Lobby");
        raw.GetProperty("dm").GetProperty("combatants")[0].GetProperty("kind").GetString().Should().Be("Pc");
    }
}
