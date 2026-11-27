using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.Features.Combat;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class CombatHubCommandTests(PostgresFixture postgres) : IAsyncLifetime
{
    [Fact]
    public async Task The_dm_adds_numbered_srd_monsters_named_in_the_connection_language()
    {
        var dmUser = await postgres.Factory.CreateDmAsync();
        var session = await dmUser.Http.CreateSessionAsync("Lyra");
        var dm = await ConnectAsync(session.JoinCode, cookie: dmUser.Cookie, lang: "es");

        var version = Accepted(await dm.Hub.InvokeAsync<CommandResult>(
            "AddMonster", new AddMonsterInput(new MonsterSourceInput(SrdSlug: "goblin-warrior"), Count: 3), dm.Joined.Dm!.Version));

        var view = await dm.Snapshots.WaitForAsync(v => Version(v) == version);
        foreach (var name in new[] { "Guerrero goblin 1", "Guerrero goblin 2", "Guerrero goblin 3" })
        {
            var goblin = Combatant(view, name);
            goblin.GetProperty("maxHp").GetInt32().Should().Be(10);
            goblin.GetProperty("ac").GetInt32().Should().Be(15);
            goblin.GetProperty("initiativeBonus").GetInt32().Should().Be(2);
            goblin.GetProperty("sourceKind").GetString().Should().Be("Srd");
            goblin.GetProperty("sourceId").GetString().Should().Be("goblin-warrior");
        }

        var log = (await dm.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;
        log.Should().ContainSingle(e => e.Type == "MonstersAdded").Which.Parameters["count"].Should().Be("3");
    }

    [Fact]
    public async Task The_dm_adds_monsters_from_their_template_with_rolled_hit_points()
    {
        var dmUser = await postgres.Factory.CreateDmAsync();
        var template = await CreateTemplateAsync(dmUser.Http, new
        {
            name = "Bandido veterano", maxHp = 100, ac = 14, initiativeBonus = 3, hpFormula = "4d8+12",
        });
        var session = await dmUser.Http.CreateSessionAsync("Lyra");
        var dm = await ConnectAsync(session.JoinCode, cookie: dmUser.Cookie);

        var version = Accepted(await dm.Hub.InvokeAsync<CommandResult>(
            "AddMonster",
            new AddMonsterInput(new MonsterSourceInput(TemplateId: template.Id), Count: 2, RollHp: true),
            dm.Joined.Dm!.Version));

        var view = await dm.Snapshots.WaitForAsync(v => Version(v) == version);
        foreach (var name in new[] { "Bandido veterano 1", "Bandido veterano 2" })
        {
            var bandit = Combatant(view, name);
            bandit.GetProperty("maxHp").GetInt32().Should().BeInRange(16, 44);
            bandit.GetProperty("currentHp").GetInt32().Should().Be(bandit.GetProperty("maxHp").GetInt32());
            bandit.GetProperty("sourceKind").GetString().Should().Be("Template");
            bandit.GetProperty("sourceId").GetString().Should().Be(template.Id.ToString());
        }
    }

    [Fact]
    public async Task Bad_monster_sources_are_rejected_and_change_nothing()
    {
        var other = await postgres.Factory.CreateDmAsync();
        var foreign = await CreateTemplateAsync(other.Http, new { name = "Ajeno", maxHp = 5, ac = 10, initiativeBonus = 0 });
        var (dm, code) = await DmWithSessionAsync("Lyra");
        var ana = await PlayerAsync(code, "Ana");
        var version = dm.Joined.Dm!.Version;

        async Task<CommandResult> ResultAsync(Participant who, AddMonsterInput? input) =>
            await who.Hub.InvokeAsync<CommandResult>("AddMonster", input, version);

        async Task<string?> ErrorAsync(Participant who, AddMonsterInput? input) =>
            (await ResultAsync(who, input)).Error;

        (await ErrorAsync(dm, null)).Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(null))).Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput()))).Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "goblin-warrior", TemplateId: foreign.Id))))
            .Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "goblin-warrior"), Count: 21)))
            .Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(Custom: new CustomMonsterInput(null, 7, 15)))))
            .Should().Be("InvalidValue");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(Custom: new CustomMonsterInput("Orc", 15, 13, HpFormula: "2d7")))))
            .Should().Be("InvalidValue");
        var notFound = await ResultAsync(dm, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "kraken-of-nowhere")));
        notFound.Error.Should().Be("MonsterNotFound");
        notFound.Version.Should().Be(version);
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(TemplateId: foreign.Id))))
            .Should().Be("MonsterNotFound");
        var notAllowed = await ResultAsync(ana, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "goblin-warrior")));
        notAllowed.Error.Should().Be("NotAllowed");
        notAllowed.Version.Should().Be(version);

        (await dm.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Dm!.Version.Should().Be(version);
    }

    [Fact]
    public async Task Null_conditions_are_an_invalid_value()
    {
        var (dm, _) = await DmWithSessionAsync("Lyra");
        var lyra = Id(dm.Joined, "Lyra");

        var result = await dm.Hub.InvokeAsync<CommandResult>("SetConditions", lyra, null, dm.Joined.Dm!.Version);

        result.Error.Should().Be("InvalidValue");
    }

    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_dm_and_two_players_run_a_combat_and_each_sees_their_own_view()
    {
        var (dm, code) = await DmWithSessionAsync("Lyra", "Torvald");
        var ana = await PlayerAsync(code, "Ana");
        var bea = await PlayerAsync(code, "Bea");
        var lyra = Id(dm.Joined, "Lyra");
        var torvald = Id(dm.Joined, "Torvald");
        var version = dm.Joined.Dm!.Version;

        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Assassin", 30, 15, isHidden: true), version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Orc", 15, 13), version));
        version = Accepted(await ana.Hub.InvokeAsync<CommandResult>("Claim", lyra, version));
        version = Accepted(await bea.Hub.InvokeAsync<CommandResult>("Claim", torvald, version));
        version = Accepted(await ana.Hub.InvokeAsync<CommandResult>("SetInitiative", lyra, 18, version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("RollMonsterInitiative", version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("Start", version));
        var orc = Id(await dm.Snapshots.WaitForAsync(v => Version(v) == version), "Orc");
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("ApplyDamage", orc, 5, version));

        var dmView = await dm.Snapshots.WaitForAsync(v => Version(v) == version);
        Combatant(dmView, "Orc").GetProperty("currentHp").GetInt32().Should().Be(10);
        Combatant(dmView, "Assassin").GetProperty("isHidden").GetBoolean().Should().BeTrue();

        foreach (var (player, mine) in new[] { (ana, "Lyra"), (bea, "Torvald") })
        {
            var view = await player.Snapshots.WaitForAsync(v => Version(v) == version);
            var orcView = Combatant(view, "Orc");
            orcView.GetProperty("currentHp").ValueKind.Should().Be(JsonValueKind.Null);
            orcView.GetProperty("band").GetString().Should().Be("Wounded");
            Combatant(view, mine).GetProperty("isMine").GetBoolean().Should().BeTrue();
            player.Snapshots.All.Concat(player.Log.All)
                .Should().OnlyContain(message => !message.GetRawText().Contains("Assassin"));
        }
    }

    [Fact]
    public async Task Players_can_only_change_their_own_character()
    {
        var (dm, code) = await DmWithSessionAsync("Lyra", "Torvald");
        var ana = await PlayerAsync(code, "Ana");
        var lyra = Id(dm.Joined, "Lyra");
        var torvald = Id(dm.Joined, "Torvald");
        var version = Accepted(await ana.Hub.InvokeAsync<CommandResult>("Claim", lyra, dm.Joined.Dm!.Version));

        (await ana.Hub.InvokeAsync<CommandResult>("ApplyDamage", torvald, 3, version)).Error.Should().Be("NotYourCharacter");
        (await ana.Hub.InvokeAsync<CommandResult>("Start", version)).Error.Should().Be("NotAllowed");
        version = Accepted(await ana.Hub.InvokeAsync<CommandResult>("ApplyDamage", lyra, 3, version));

        var dmView = await dm.Snapshots.WaitForAsync(v => Version(v) == version);
        Combatant(dmView, "Lyra").GetProperty("currentHp").GetInt32().Should().Be(17);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_that_reports_the_current_version()
    {
        var (dm, _) = await DmWithSessionAsync("Lyra");
        var initial = dm.Joined.Dm!.Version;
        var current = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Orc", 15, 13), initial));

        var stale = await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Goblin", 7, 15), initial);

        stale.Error.Should().Be(CombatErrors.Conflict);
        stale.Version.Should().Be(current);
    }

    [Fact]
    public async Task Of_simultaneous_commands_on_the_same_version_only_one_wins()
    {
        var dmUser = await postgres.Factory.CreateDmAsync();
        var session = await dmUser.Http.CreateSessionAsync("Lyra");
        var first = await ConnectAsync(session.JoinCode, cookie: dmUser.Cookie);
        var second = await ConnectAsync(session.JoinCode, cookie: dmUser.Cookie);
        var version = first.Joined.Dm!.Version;

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? first : second).Hub.InvokeAsync<CommandResult>(
                "AddMonster", Monster($"Goblin {i}", 7, 15), version)));

        results.Count(r => r.Succeeded).Should().Be(1);
        results.Where(r => !r.Succeeded).Should().OnlyContain(r => r.Error == CombatErrors.Conflict);
    }

    [Fact]
    public async Task Reclaiming_your_character_changes_nothing_but_sends_a_fresh_snapshot()
    {
        var (dm, code) = await DmWithSessionAsync("Lyra");
        var ana = await PlayerAsync(code, "Ana");
        var lyra = Id(dm.Joined, "Lyra");
        var version = Accepted(await ana.Hub.InvokeAsync<CommandResult>("Claim", lyra, dm.Joined.Dm!.Version));
        await ana.Snapshots.WaitForAsync(v => Version(v) == version);
        var before = ana.Snapshots.All.Count;

        var again = await ana.Hub.InvokeAsync<CommandResult>("Claim", lyra, version);

        Accepted(again).Should().Be(version);
        await ana.Snapshots.WaitUntilAsync(items => items.Count > before);
    }

    [Fact]
    public async Task The_log_is_saved_and_players_never_get_entries_about_hidden_combatants()
    {
        var (dm, code) = await DmWithSessionAsync("Lyra");
        var ana = await PlayerAsync(code, "Ana");
        var version = dm.Joined.Dm!.Version;
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Assassin", 30, 15, isHidden: true), version));
        var assassin = Id(await dm.Snapshots.WaitForAsync(v => Version(v) == version), "Assassin");
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("Reveal", assassin, version));

        var dmLog = (await dm.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;
        var playerLog = (await ana.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;

        dmLog.Select(e => e.Type).Should().Equal("MonstersAdded", "Revealed");
        playerLog.Select(e => e.Type).Should().Equal("Revealed");
        await ana.Log.WaitForAsync(entries => entries.GetArrayLength() > 0);
        ana.Log.All.SelectMany(m => m.EnumerateArray()).Select(e => e.GetProperty("type").GetString())
            .Should().Equal("Revealed");
        await using var db = postgres.CreateDbContext();
        (await db.CombatLog.CountAsync(e => e.SessionId == dm.Joined.Dm.SessionId)).Should().Be(2);
    }

    [Fact]
    public async Task Players_do_not_get_exact_monster_hp_numbers_in_the_log()
    {
        var (dm, code) = await DmWithSessionAsync("Lyra");
        var ana = await PlayerAsync(code, "Ana");
        var version = dm.Joined.Dm!.Version;
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", Monster("Orc", 15, 13), version));
        var orc = Id(await dm.Snapshots.WaitForAsync(v => Version(v) == version), "Orc");
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("ApplyDamage", orc, 5, version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("Heal", orc, 3, version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("SetTempHp", orc, 4, version));

        await ana.Log.WaitForAsync(entries => entries.EnumerateArray().Any(e => e.GetProperty("type").GetString() == "TempHpSet"));
        var dmSnapshot = (await dm.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;
        var playerSnapshot = (await ana.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;
        await dm.Log.WaitForAsync(entries => entries.EnumerateArray().Any(e => e.GetProperty("type").GetString() == "TempHpSet"));
        var dmPushed = dm.Log.All.SelectMany(m => m.EnumerateArray()).ToList();
        var playerPushed = ana.Log.All.SelectMany(m => m.EnumerateArray()).ToList();

        foreach (var type in new[] { "Healed", "TempHpSet" })
        {
            dmSnapshot.Single(e => e.Type == type).Parameters.Should().ContainKey("amount");
            playerSnapshot.Single(e => e.Type == type).Parameters.Should().NotContainKey("amount");
            Pushed(dmPushed, type).TryGetProperty("amount", out _).Should().BeTrue();
            Pushed(playerPushed, type).TryGetProperty("amount", out _).Should().BeFalse();
        }

        playerSnapshot.Single(e => e.Type == "DamageApplied").Parameters.Should().ContainKey("amount");
        Pushed(playerPushed, "DamageApplied").TryGetProperty("amount", out _).Should().BeTrue();
    }

    private static JsonElement Pushed(IEnumerable<JsonElement> entries, string type) =>
        entries.Single(e => e.GetProperty("type").GetString() == type).GetProperty("parameters");

    [Fact]
    public async Task Unknown_condition_values_are_rejected()
    {
        var (dm, _) = await DmWithSessionAsync("Lyra");
        var lyra = Id(dm.Joined, "Lyra");

        var result = await dm.Hub.InvokeAsync<CommandResult>(
            "SetConditions", lyra, new[] { (Condition)(1 << 20) }, dm.Joined.Dm!.Version);

        result.Error.Should().Be("InvalidValue");
    }

    [Fact]
    public async Task Conditions_are_set_from_a_list_of_names()
    {
        var (dm, _) = await DmWithSessionAsync("Lyra");
        var lyra = Id(dm.Joined, "Lyra");

        var version = Accepted(await dm.Hub.InvokeAsync<CommandResult>(
            "SetConditions", lyra, new[] { Condition.Prone, Condition.Poisoned }, dm.Joined.Dm!.Version));

        var view = await dm.Snapshots.WaitForAsync(v => Version(v) == version);
        Combatant(view, "Lyra").GetProperty("conditions").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("Poisoned", "Prone");
    }

    [Fact]
    public async Task Nothing_can_be_done_after_the_combat_ends()
    {
        var (dm, _) = await DmWithSessionAsync("Lyra");
        var lyra = Id(dm.Joined, "Lyra");
        var version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("End", dm.Joined.Dm!.Version));

        (await dm.Hub.InvokeAsync<CommandResult>("ApplyDamage", lyra, 1, version)).Error.Should().Be("SessionEnded");
    }

    [Fact]
    public async Task Commands_need_a_joined_session()
    {
        var dmUser = await postgres.Factory.CreateDmAsync();
        var hub = await postgres.Factory.ConnectAsync(cookie: dmUser.Cookie);
        _connections.Add(hub);

        (await hub.InvokeAsync<CommandResult>("NextTurn", 0)).Error.Should().Be(CombatErrors.NotJoined);
    }

    private sealed record Participant(
        HubConnection Hub, Inbox<JsonElement> Snapshots, Inbox<JsonElement> Log, SnapshotResult Joined);

    private async Task<(Participant Dm, string Code)> DmWithSessionAsync(params string[] characters)
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync(characters);
        return (await ConnectAsync(session.JoinCode, cookie: dm.Cookie), session.JoinCode);
    }

    private async Task<Participant> PlayerAsync(string code, string name)
    {
        var guest = await postgres.Factory.JoinAsync(code, name);
        return await ConnectAsync(code, guestToken: guest.GuestToken);
    }

    private sealed record CreatedTemplate(Guid Id);

    private static AddMonsterInput Monster(string name, int maxHp, int ac, bool isHidden = false) =>
        new(new MonsterSourceInput(Custom: new CustomMonsterInput(name, maxHp, ac)), IsHidden: isHidden);

    private static async Task<CreatedTemplate> CreateTemplateAsync(HttpClient dm, object template)
    {
        var response = await dm.PostAsJsonAsync("/api/monsters/templates", template);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedTemplate>(TestJson.Options))!;
    }

    private async Task<Participant> ConnectAsync(string code, string? cookie = null, string? guestToken = null, string? lang = null)
    {
        var hub = await postgres.Factory.ConnectAsync(cookie, guestToken, lang);
        _connections.Add(hub);
        var snapshots = new Inbox<JsonElement>(hub, guestToken is null ? "DmSnapshot" : "PlayerSnapshot");
        var log = new Inbox<JsonElement>(hub, "LogAppended");
        var joined = await hub.InvokeAsync<SnapshotResult>("Join", code);
        joined.Succeeded.Should().BeTrue();
        return new Participant(hub, snapshots, log, joined);
    }

    private static int Accepted(CommandResult result)
    {
        result.Error.Should().BeNull();
        return result.Version;
    }

    private static int Version(JsonElement view) => view.GetProperty("version").GetInt32();

    private static JsonElement Combatant(JsonElement view, string name) =>
        view.GetProperty("combatants").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private static Guid Id(JsonElement view, string name) => Combatant(view, name).GetProperty("id").GetGuid();

    private static Guid Id(SnapshotResult joined, string name) =>
        joined.Dm?.Combatants.Single(c => c.Name == name).Id
        ?? joined.Player!.Combatants.Single(c => c.Name == name).Id;
}
