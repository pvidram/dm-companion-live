# Plan 2 — Real-time combat

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Players join a combat with the room code and a name, the DM and the players connect to a SignalR hub, every combat command runs through the domain with optimistic concurrency, the combat log is persisted, and each participant receives live views filtered for them — with the Plan 1 review carry-over fixed and Google sign-in wired (inactive without credentials).

**Architecture:** The `CombatSession` aggregate stays the only place with combat rules (Tasks 1–2 harden it). Infrastructure gains foreign keys and a `combat_log` table (Task 3). The API gains a guest join slice with a Data Protection–signed `GuestToken` and a rate limit (Task 5), and a `Features/Combat` slice: `CombatHub` (connect/join/snapshot, Task 6) plus `CombatCommandRunner` and `CombatBroadcaster` (load → domain → save state + log in one transaction → broadcast a `DmView` to the DM group and a per-guest `PlayerView`, Task 7). Task 8 adds session expiry end to end, cookie revalidation and Google sign-in.

**Tech Stack:** As Plan 1 (.NET 10, ASP.NET Core 10, EF Core 10.0.15, Npgsql 10.0.4, xUnit 2.9.3, AwesomeAssertions, Testcontainers 4.17.0) plus ASP.NET Core SignalR (in the shared framework), `Microsoft.AspNetCore.SignalR.Client` 10.0.15 (tests), `Microsoft.AspNetCore.Authentication.Google` 10.0.15, `Microsoft.Extensions.TimeProvider.Testing` 10.11.1 (tests).

**Spec:** `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md` (§3 domain rules, §4 API and real time, §6 authentication). Carry-over from Plan 1: `docs/superpowers/plans/2026-11-24-plan-1-carryover.md` — every "Plan 2" item is assigned to a task below (see the coverage table at the end).

## Global Constraints

- Target framework `net10.0`; `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` stay on (from `Directory.Build.props`).
- Package versions exactly as listed in each task. Do not move to .NET 11 packages.
- Domain project has **no** package or project references (`System.Collections.Immutable` is part of the shared framework).
- Code, identifiers, commit messages and docs in English. Conventional commits; every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on a branch `feature/plan-2-real-time-combat` created from `main`; never commit to `main` directly, never push.
- Integration tests need Docker. No cloud accounts or external services are used; Google sign-in is only exercised up to the redirect.
- Run commands from the repository root.

## Decisions taken in this plan

- **Turns are tracked per round, not as a pointer into the order.** Each combatant records the round in which it last began a turn (`LastTurnRound`); the next turn goes to the first combatant in initiative order that can act and has not acted this round. Changing initiative mid-combat therefore never skips nor repeats a turn (carry-over item).
- **One hub method per command for both roles.** The spec lists `ApplyDamageToMe`, `HealMe`, … for players; instead players call the same `ApplyDamage`, `Heal`, `SetInitiative`, `SetConditions` as the DM and the domain authorizes (a guest may only touch the character they claimed). Task 9 updates the spec.
- **Per-guest player views.** `PlayerView.IsMine` depends on the guest, so each guest connection joins a group `session:{id}:guest:{guestId}`; an in-memory `SessionPresence` lists the connected guests of a session. Single replica only (already a documented limitation).
- **`PlayerView.Version` is exposed** although it also moves when the DM acts on hidden combatants: players need it as `expectedVersion`. The side channel only reveals "something happened", never what. Documented on the type.
- **Log visibility is decided when the event happens:** an event about a combatant that is hidden at that moment is never shown to players, even after a later reveal; events without a subject are visible.
- **Several sessions per table are allowed** (e.g. a new fight after an ended one).
- **Expired sessions** (12 h without activity) stay readable but reject commands (`SessionExpired`) and joins (HTTP 410). A cleanup job is deferred.
- **Error codes:** domain codes are sent as their enum names; the hub adds `SessionNotFound` and `NotJoined`; a lost optimistic-concurrency race is `Conflict` (hub) / HTTP 409 with `code: "Conflict"` (REST).

---

### Task 1: Combat domain — turns tracked per round

**Files:**
- Modify: `src/DmCompanionLive.Domain/Combat/Combatant.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/CombatSession.cs` (`TryAdvance` and its doc comment)
- Test: `tests/DmCompanionLive.Domain.Tests/Combat/TurnOrderTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/CombatCommandsTests.cs`

**Interfaces:**
- Produces: `Combatant.LastTurnRound` (`int?`, public getter, private setter) — persisted in Task 3.

- [ ] **Step 1: Create the branch**

```bash
git switch main
git switch -c feature/plan-2-real-time-combat
```

- [ ] **Step 2: Write the failing tests**

Add to `TurnOrderTests` (inside the class, keep the existing tests and helpers):

```csharp
    [Fact]
    public void Lowering_the_initiative_of_who_just_acted_neither_skips_nor_repeats_turns()
    {
        var session = StartedSessionWithThreePcs(out var a, out var b, out var c);
        session.NextTurn(Dm, Now);
        session.SetInitiative(Dm, b, 5, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(a);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Raising_the_initiative_of_who_has_not_acted_keeps_their_turn_this_round()
    {
        var session = StartedSessionWithThreePcs(out _, out _, out var c);
        session.NextTurn(Dm, Now);
        session.SetInitiative(Dm, c, 25, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(c);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void A_monster_added_mid_round_acts_in_the_same_round()
    {
        var session = StartedSessionWithTwoPcs(out var first, out var second);
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        session.SetInitiative(Dm, goblin, 15, Now);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(goblin);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(second);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(first);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Next_turn_fails_when_every_combatant_is_defeated()
    {
        var session = NewSession();
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13), Now);
        session.Start(Dm, Now);
        session.ApplyDamage(Dm, goblin, 7, Now);
        session.ApplyDamage(Dm, orc, 15, Now);

        var act = () => session.NextTurn(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NoCombatants);
    }

    [Fact]
    public void A_single_combatant_gets_a_turn_every_round()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        session.Start(Dm, Now);

        session.NextTurn(Dm, Now);
        session.NextTurn(Dm, Now);

        session.CurrentTurnId.Should().Be(session.Combatants.Single().Id);
        session.Round.Should().Be(3);
    }

    private static CombatSession StartedSessionWithThreePcs(out Guid a, out Guid b, out Guid c)
    {
        var session = NewSession(new PcSeed("Ana", 20, 15), new PcSeed("Bo", 20, 15), new PcSeed("Cy", 20, 15));
        (a, b, c, _) = Ids(session);
        session.SetInitiative(Dm, a, 20, Now);
        session.SetInitiative(Dm, b, 15, Now);
        session.SetInitiative(Dm, c, 10, Now);
        session.Start(Dm, Now);
        return session;
    }
```

Add to `CombatCommandsTests`:

```csharp
    [Fact]
    public void Removing_someone_else_keeps_the_current_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var lyra = session.Combatants[0].Id;
        var torvald = session.Combatants[1].Id;
        session.SetInitiative(Dm, lyra, 18, Now);
        session.SetInitiative(Dm, torvald, 12, Now);
        session.Start(Dm, Now);

        session.Remove(Dm, torvald, Now);

        session.CurrentTurnId.Should().Be(lyra);
        session.Round.Should().Be(1);
        session.Combatants.Should().ContainSingle();
    }

    [Fact]
    public void Removing_in_the_lobby_does_not_start_any_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));

        session.Remove(Dm, session.Combatants[0].Id, Now);

        session.CurrentTurnId.Should().BeNull();
        session.Round.Should().Be(0);
        session.Combatants.Should().ContainSingle().Which.Name.Should().Be("Torvald");
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: FAIL — `Lowering_the_initiative…` (turn goes to `a` in round 2 instead of `c`) and `Raising_the_initiative…` (round 2 instead of 1). The other new tests already pass; they pin behaviour the new turn tracking must keep.

- [ ] **Step 4: Implement**

In `Combatant.cs`, after `SortOrder`:

```csharp
    /// <summary>Round in which this combatant last began a turn; null before its first turn.</summary>
    public int? LastTurnRound { get; private set; }
```

and next to the other internal mutators:

```csharp
    internal void BeginTurn(int round) => LastTurnRound = round;
```

In `CombatSession.cs`, replace `TryAdvance` (and its doc comment) with:

```csharp
    /// <summary>
    /// Gives the turn to the first combatant in turn order that can act and has not begun a
    /// turn this round, skipping defeated ones and <paramref name="excluded"/>. When everybody
    /// has acted, a new round starts from the top of the order. Tracking turns per round instead
    /// of a position in the order keeps initiative changes during combat from skipping or
    /// repeating anyone's turn.
    /// </summary>
    private bool TryAdvance(Guid? excluded)
    {
        var able = TurnOrder.Where(c => !c.IsDefeated && c.Id != excluded).ToList();
        if (able.Count == 0)
        {
            return false;
        }

        var next = able.FirstOrDefault(c => c.LastTurnRound != Round);
        if (next is null)
        {
            Round++;
            next = able[0];
        }

        next.BeginTurn(Round);
        CurrentTurnId = next.Id;
        return true;
    }
```

`Start` already sets `Round = 1` and `CurrentTurnId = null` before calling `TryAdvance(excluded: null)`; leave it as is.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, no failures (existing turn tests included).

- [ ] **Step 6: Commit**

```bash
git add src/DmCompanionLive.Domain tests/DmCompanionLive.Domain.Tests
git commit -m "fix: track turns per round so initiative changes never skip or repeat a turn

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Combat domain — authorization, validation, expiry and log visibility

**Files:**
- Modify: `src/DmCompanionLive.Domain/DomainException.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/Condition.cs`, `CombatEvent.cs`, `CombatSession.cs` (full file below), `JoinCode.cs`, `Views.cs`
- Create: `src/DmCompanionLive.Domain/Combat/GuestName.cs`
- Modify: `src/DmCompanionLive.Domain/Users/DmUser.cs`, `src/DmCompanionLive.Domain/Tables/Table.cs`
- Test: `tests/DmCompanionLive.Domain.Tests/Combat/PermissionsTests.cs`, `HitPointsTests.cs`, `JoinCodeTests.cs`; create `tests/DmCompanionLive.Domain.Tests/Combat/SessionLifecycleTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/CombatEventTests.cs`; modify `tests/DmCompanionLive.Domain.Tests/Users/DmUserTests.cs`

**Interfaces:**
- Consumes: `Combatant.LastTurnRound`, `TryAdvance` from Task 1.
- Produces:
  - `DomainErrorCode.SessionExpired`, `DomainErrorCode.Conflict`
  - `CombatSession.InactivityLimit` (`static readonly TimeSpan`, 12 h), `bool CombatSession.IsExpiredAt(DateTimeOffset now)`
  - `CombatEvent(int Version, DateTimeOffset At, string ActorName, string Type, IReadOnlyDictionary<string, string> Parameters, bool VisibleToPlayers)`
  - `ConditionSet.All`, `bool ConditionSet.IsValid(Condition)`
  - `bool JoinCode.TryNormalize(string? input, out string code)`
  - `string GuestName.Validate(string? name)`, `GuestName.MaxLength` (40)
  - `DmUser.MaxExternalIdLength` (200)

- [ ] **Step 1: Write the failing tests**

In `PermissionsTests`, change the expected code of `Monsters_cannot_be_claimed` from `DomainErrorCode.NotAllowed` to `DomainErrorCode.CharacterNotFound`, and add:

```csharp
    [Fact]
    public void Errors_never_tell_a_guest_whether_a_hidden_combatant_exists()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var hidden = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        var player = Guest();

        var onHidden = () => session.ApplyDamage(player, hidden, 1, Now);
        var onMissing = () => session.ApplyDamage(player, Guid.NewGuid(), 1, Now);
        var claimHidden = () => session.Claim(player, hidden, Now);
        var claimMissing = () => session.Claim(player, Guid.NewGuid(), Now);

        onHidden.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
        onMissing.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
        claimHidden.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
        claimMissing.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CharacterNotFound);
    }

    [Fact]
    public void Guests_cannot_run_any_dm_command()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);
        session.Start(Dm, Now);

        var commands = new Dictionary<string, Action>
        {
            ["AddMonster"] = () => session.AddMonster(player, new MonsterSeed("Goblin", 7, 15), Now),
            ["RollMonsterInitiative"] = () => session.RollMonsterInitiative(player, new FixedDice(10), Now),
            ["Start"] = () => session.Start(player, Now),
            ["NextTurn"] = () => session.NextTurn(player, Now),
            ["Reveal"] = () => session.Reveal(player, lyra, Now),
            ["Remove"] = () => session.Remove(player, lyra, Now),
            ["End"] = () => session.End(player, Now),
        };

        foreach (var (name, command) in commands)
        {
            command.Should().Throw<DomainException>(name).Which.Code.Should().Be(DomainErrorCode.NotAllowed, name);
        }
    }

    [Fact]
    public void Undefined_condition_values_are_rejected()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var act = () => session.SetConditions(Dm, lyra, (Condition)(1 << 15), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        ConditionSet.All.Should().Be(Enum.GetValues<Condition>().Aggregate((all, c) => all | c));
    }
```

In `HitPointsTests` add:

```csharp
    [Fact]
    public void Temporary_hp_cannot_be_negative_and_replaces_rather_than_stacks()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var negative = () => session.SetTempHp(Dm, lyra, -1, Now);
        session.SetTempHp(Dm, lyra, 5, Now);
        session.SetTempHp(Dm, lyra, 3, Now);

        negative.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        session.Combatants.Single().TempHp.Should().Be(3);
    }
```

In `JoinCodeTests` add:

```csharp
    [Theory]
    [InlineData(" abc234 ", true, "ABC234")]
    [InlineData(null, false, "")]
    [InlineData("ABC23", false, "ABC23")]
    [InlineData("ABC2345", false, "ABC2345")]
    [InlineData("ABCO23", false, "ABCO23")]
    public void Try_normalize_accepts_only_well_formed_codes(string? input, bool valid, string normalized)
    {
        JoinCode.TryNormalize(input, out var code).Should().Be(valid);
        code.Should().Be(normalized);
    }
```

Create `tests/DmCompanionLive.Domain.Tests/Combat/SessionLifecycleTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class SessionLifecycleTests
{
    [Fact]
    public void Every_command_is_rejected_once_the_combat_has_ended()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);
        session.End(Dm, Now);

        var commands = new Dictionary<string, Action>
        {
            ["AddMonster"] = () => session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now),
            ["ApplyDamage"] = () => session.ApplyDamage(Dm, lyra, 1, Now),
            ["Heal"] = () => session.Heal(Dm, lyra, 1, Now),
            ["SetTempHp"] = () => session.SetTempHp(Dm, lyra, 1, Now),
            ["SetInitiative"] = () => session.SetInitiative(player, lyra, 10, Now),
            ["RollMonsterInitiative"] = () => session.RollMonsterInitiative(Dm, new FixedDice(10), Now),
            ["Start"] = () => session.Start(Dm, Now),
            ["NextTurn"] = () => session.NextTurn(Dm, Now),
            ["Claim"] = () => session.Claim(Guest("Bea"), lyra, Now),
            ["SetConditions"] = () => session.SetConditions(Dm, lyra, Condition.Prone, Now),
            ["Reveal"] = () => session.Reveal(Dm, lyra, Now),
            ["Remove"] = () => session.Remove(Dm, lyra, Now),
            ["End"] = () => session.End(Dm, Now),
        };

        foreach (var (name, command) in commands)
        {
            command.Should().Throw<DomainException>(name).Which.Code.Should().Be(DomainErrorCode.SessionEnded, name);
        }
    }

    [Fact]
    public void A_session_expires_after_twelve_hours_without_activity()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var later = Now + CombatSession.InactivityLimit + TimeSpan.FromSeconds(1);

        var act = () => session.ApplyDamage(Dm, lyra, 1, later);

        session.IsExpiredAt(Now + CombatSession.InactivityLimit).Should().BeFalse();
        session.IsExpiredAt(later).Should().BeTrue();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionExpired);
    }

    [Fact]
    public void Activity_keeps_a_session_alive_and_an_ended_session_never_expires()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        session.ApplyDamage(Dm, lyra, 1, Now + TimeSpan.FromHours(11));
        session.IsExpiredAt(Now + TimeSpan.FromHours(20)).Should().BeFalse();

        session.End(Dm, Now + TimeSpan.FromHours(20));
        session.IsExpiredAt(Now + TimeSpan.FromDays(10)).Should().BeFalse();
    }

    [Fact]
    public void Times_are_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.FromHours(1));

        var session = CombatSession.Create(Guid.NewGuid(), DmId, "ABC234", [new PcSeed("Lyra", 20, 15)], local);
        session.Start(Dm, local);

        session.LastActivityAt.Offset.Should().Be(TimeSpan.Zero);
        session.LastActivityAt.Should().Be(local);
        session.UncommittedEvents.Single().At.Offset.Should().Be(TimeSpan.Zero);
    }
}
```

Create `tests/DmCompanionLive.Domain.Tests/Combat/CombatEventTests.cs`:

```csharp
using System.Collections.Immutable;
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class CombatEventTests
{
    [Fact]
    public void Events_about_a_combatant_hidden_at_that_moment_are_not_visible_to_players()
    {
        var session = NewSession();
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.ApplyDamage(Dm, assassin, 5, Now);
        session.Reveal(Dm, assassin, Now);
        session.ApplyDamage(Dm, assassin, 5, Now);
        session.RollMonsterInitiative(Dm, new FixedDice(10), Now);

        session.UncommittedEvents.Select(e => (e.Type, e.VisibleToPlayers)).Should().Equal(
            ("MonsterAdded", false),
            ("DamageApplied", false),
            ("Revealed", true),
            ("DamageApplied", true),
            ("MonsterInitiativeRolled", true));
    }

    [Fact]
    public void Event_parameters_are_immutable()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        session.ApplyDamage(Dm, session.Combatants.Single().Id, 3, Now);

        session.UncommittedEvents.Single().Parameters.Should().BeAssignableTo<IImmutableDictionary<string, string>>();
    }
}
```

In `DmUserTests` add:

```csharp
    [Theory]
    [InlineData("")]
    [InlineData("google")]
    [InlineData(":123")]
    [InlineData("google:")]
    public void Rejects_an_external_id_that_is_not_provider_colon_subject(string externalId)
    {
        var act = () => DmUser.Create(externalId, "Ana", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Rejects_an_overlong_external_id()
    {
        var act = () => DmUser.Create("google:" + new string('1', DmUser.MaxExternalIdLength), "Ana", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Creation_time_is_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 24, 19, 0, 0, TimeSpan.FromHours(1));

        DmUser.Create("dev:ana", "Ana", local).CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }
```

Add a test file `tests/DmCompanionLive.Domain.Tests/Combat/GuestNameTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class GuestNameTests
{
    [Fact]
    public void Names_are_trimmed()
    {
        GuestName.Validate("  Ana ").Should().Be("Ana");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_are_rejected(string? name)
    {
        var act = () => GuestName.Validate(name);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Names_longer_than_the_limit_are_rejected()
    {
        var act = () => GuestName.Validate(new string('a', GuestName.MaxLength + 1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS (`ConditionSet`, `GuestName`, `JoinCode.TryNormalize`, `IsExpiredAt`, `InactivityLimit`, `VisibleToPlayers`, `SessionExpired`, `MaxExternalIdLength` do not exist).

- [ ] **Step 3: Implement**

`DomainException.cs` — add two codes at the end of the enum:

```csharp
    NoCombatants,
    SessionExpired,
    Conflict,
}
```

`Condition.cs` — append after the enum:

```csharp
public static class ConditionSet
{
    /// <summary>Every defined condition flag.</summary>
    public const Condition All = (Condition)((1 << 15) - 1);

    /// <summary>False when <paramref name="conditions"/> carries bits that are not a defined condition.</summary>
    public static bool IsValid(Condition conditions) => (conditions & ~All) == 0;
}
```

`CombatEvent.cs`:

```csharp
namespace DmCompanionLive.Domain.Combat;

/// <summary>
/// One entry of the combat log. Stores a type and parameters, never rendered text,
/// so each client can translate it into its own language. <see cref="VisibleToPlayers"/>
/// is false when the event is about a combatant that was hidden when it happened.
/// </summary>
public sealed record CombatEvent(
    int Version,
    DateTimeOffset At,
    string ActorName,
    string Type,
    IReadOnlyDictionary<string, string> Parameters,
    bool VisibleToPlayers);
```

`JoinCode.cs` — add:

```csharp
    /// <summary>Normalizes <paramref name="input"/> and tells whether it can be a join code at all.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = (input ?? string.Empty).Trim().ToUpperInvariant();
        return code.Length == Length && code.All(Alphabet.Contains);
    }
```

Create `src/DmCompanionLive.Domain/Combat/GuestName.cs`:

```csharp
namespace DmCompanionLive.Domain.Combat;

/// <summary>The display name a player types when joining a combat.</summary>
public static class GuestName
{
    public const int MaxLength = 40;

    public static string Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxLength} characters.");
        }

        return trimmed;
    }
}
```

`Views.cs` — add a doc comment on `PlayerView`:

```csharp
/// <remarks>
/// <see cref="Version"/> also moves when the DM changes hidden combatants. Players need it as the
/// <c>expectedVersion</c> of their commands; it reveals that something happened, never what.
/// </remarks>
public sealed record PlayerView(
```

`DmUser.cs` — add the constant and validate the external id; store UTC:

```csharp
    public const int MaxDisplayNameLength = 80;
    public const int MaxExternalIdLength = 200;
```

```csharp
    public static DmUser Create(string externalId, string displayName, DateTimeOffset now)
    {
        var colon = externalId.IndexOf(':');
        if (colon <= 0 || colon == externalId.Length - 1 || externalId.Length > MaxExternalIdLength)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"External id must look like 'provider:subject' (max {MaxExternalIdLength}).");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > MaxDisplayNameLength)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"Display name must have 1 to {MaxDisplayNameLength} characters.");
        }

        return new DmUser
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            DisplayName = displayName.Trim(),
            CreatedAt = now.ToUniversalTime(),
        };
    }
```

`Table.cs` — in `Create`, store `CreatedAt = now.ToUniversalTime()`.

Replace `CombatSession.cs` with:

```csharp
using System.Collections.Immutable;
using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

/// <summary>Aggregate root of a live combat. Every rule of the combat lives here.</summary>
public sealed class CombatSession
{
    /// <summary>A session nobody touches for this long expires and accepts no more commands.</summary>
    public static readonly TimeSpan InactivityLimit = TimeSpan.FromHours(12);

    private readonly List<Combatant> _combatants = [];
    private readonly List<CombatEvent> _uncommittedEvents = [];

    private CombatSession()
    {
        JoinCode = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid TableId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string JoinCode { get; private set; }
    public SessionStatus Status { get; private set; }
    public int Round { get; private set; }
    public Guid? CurrentTurnId { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public IReadOnlyList<Combatant> Combatants => _combatants;
    public IReadOnlyList<CombatEvent> UncommittedEvents => _uncommittedEvents;
    public IReadOnlyList<Combatant> TurnOrder => _combatants
        .OrderByDescending(c => c.Initiative ?? int.MinValue)
        .ThenByDescending(c => c.DexModifier)
        .ThenBy(c => c.SortOrder)
        .ToList();

    public static CombatSession Create(
        Guid tableId,
        Guid ownerId,
        string joinCode,
        IEnumerable<PcSeed> characters,
        DateTimeOffset now)
    {
        var session = new CombatSession
        {
            Id = Guid.NewGuid(),
            TableId = tableId,
            OwnerId = ownerId,
            JoinCode = joinCode,
            Status = SessionStatus.Lobby,
            LastActivityAt = now.ToUniversalTime(),
        };

        foreach (var character in characters)
        {
            session._combatants.Add(Combatant.CreatePc(character, session.NextSortOrder()));
        }

        return session;
    }

    /// <summary>True when the session is not ended and nobody has acted for longer than <see cref="InactivityLimit"/>.</summary>
    public bool IsExpiredAt(DateTimeOffset now) =>
        Status != SessionStatus.Ended && now - LastActivityAt > InactivityLimit;

    public Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var monster = Combatant.CreateMonster(seed, NextSortOrder());
        _combatants.Add(monster);
        Record(actor, now, "MonsterAdded", monster);
        return monster.Id;
    }

    public void ApplyDamage(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        EnsurePositive(amount);
        target.TakeDamage(amount);
        Record(actor, now, "DamageApplied", target, ("amount", amount));
    }

    public void Heal(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        EnsurePositive(amount);
        target.Heal(amount);
        Record(actor, now, "Healed", target, ("amount", amount));
    }

    public void SetTempHp(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        if (amount < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Temporary HP cannot be negative.");
        }

        target.SetTempHp(amount);
        Record(actor, now, "TempHpSet", target, ("amount", amount));
    }

    public void SetInitiative(Actor actor, Guid combatantId, int value, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        target.SetInitiative(value);
        Record(actor, now, "InitiativeSet", target, ("value", value));
    }

    public void RollMonsterInitiative(Actor actor, IDiceRoller dice, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        foreach (var monster in _combatants.Where(c => c.Kind == CombatantKind.Monster && c.Initiative is null))
        {
            monster.SetInitiative(dice.D20() + monster.DexModifier);
        }

        Record(actor, now, "MonsterInitiativeRolled");
    }

    public void Start(Actor actor, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        if (Status != SessionStatus.Lobby)
        {
            throw new DomainException(DomainErrorCode.InvalidState, "The combat has already started.");
        }

        if (!_combatants.Any(c => !c.IsDefeated))
        {
            throw new DomainException(DomainErrorCode.NoCombatants, "Nobody can act in this combat.");
        }

        Status = SessionStatus.Active;
        Round = 1;
        CurrentTurnId = null;
        TryAdvance(excluded: null);
        Record(actor, now, "CombatStarted", Current());
    }

    public void NextTurn(Actor actor, DateTimeOffset now)
    {
        EnsureActive(now);
        EnsureDm(actor);
        if (!TryAdvance(excluded: null))
        {
            throw new DomainException(DomainErrorCode.NoCombatants, "Nobody can act in this combat.");
        }

        Record(actor, now, "TurnAdvanced", Current(), ("round", Round));
    }

    public void Claim(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        if (actor is not Actor.Guest guest)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only players claim characters.");
        }

        // Monsters, hidden or not, answer exactly like unknown ids: a player learns nothing about them.
        var character = _combatants.SingleOrDefault(c => c.Id == combatantId && c.Kind == CombatantKind.Pc)
            ?? throw new DomainException(DomainErrorCode.CharacterNotFound, "There is no player character with that id.");

        if (character.ClaimedBy == guest.GuestId)
        {
            return;
        }

        if (character.ClaimedBy is not null)
        {
            throw new DomainException(DomainErrorCode.AlreadyClaimed, "Another player already controls this character.");
        }

        if (_combatants.Any(c => c.ClaimedBy == guest.GuestId))
        {
            throw new DomainException(DomainErrorCode.AlreadyHasCharacter, "You already control a character.");
        }

        character.ClaimBy(guest.GuestId);
        Record(actor, now, "CharacterClaimed", character);
    }

    public void SetConditions(Actor actor, Guid combatantId, Condition conditions, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        if (!ConditionSet.IsValid(conditions))
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Unknown condition.");
        }

        target.SetConditions(conditions);
        Record(actor, now, "ConditionsSet", target, ("conditions", conditions));
    }

    public void Reveal(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var target = Find(combatantId);
        target.Reveal();
        Record(actor, now, "Revealed", target);
    }

    public void Remove(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var target = Find(combatantId);
        if (Status == SessionStatus.Active && CurrentTurnId == target.Id && !TryAdvance(excluded: target.Id))
        {
            CurrentTurnId = null;
        }

        _combatants.Remove(target);
        Record(actor, now, "Removed", target);
    }

    public void End(Actor actor, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        Status = SessionStatus.Ended;
        CurrentTurnId = null;
        Record(actor, now, "CombatEnded");
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>
    /// The combatant <paramref name="actor"/> may change. Guests are authorized before the lookup,
    /// so an error never tells them whether a (hidden) id exists.
    /// </summary>
    private Combatant Controllable(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        return actor switch
        {
            Actor.Dm dm when dm.UserId == OwnerId => Find(combatantId),
            Actor.Guest guest => _combatants.SingleOrDefault(c => c.Id == combatantId && c.ClaimedBy == guest.GuestId)
                ?? throw new DomainException(
                    DomainErrorCode.NotYourCharacter, "You can only act on your own character."),
            _ => throw NotAllowed(),
        };
    }

    private Combatant Find(Guid combatantId) =>
        _combatants.SingleOrDefault(c => c.Id == combatantId)
        ?? throw new DomainException(DomainErrorCode.CombatantNotFound, $"Combatant {combatantId} not found.");

    private void EnsureDm(Actor actor)
    {
        if (actor is not Actor.Dm dm || dm.UserId != OwnerId)
        {
            throw NotAllowed();
        }
    }

    private void EnsureOpen(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }

        if (IsExpiredAt(now))
        {
            throw new DomainException(
                DomainErrorCode.SessionExpired, "The combat expired after 12 hours without activity.");
        }
    }

    private void EnsureActive(DateTimeOffset now)
    {
        EnsureOpen(now);
        if (Status != SessionStatus.Active)
        {
            throw new DomainException(DomainErrorCode.SessionNotActive, "The combat has not started.");
        }
    }

    private Combatant? Current() => _combatants.SingleOrDefault(c => c.Id == CurrentTurnId);

    /// <summary>
    /// Gives the turn to the first combatant in turn order that can act and has not begun a
    /// turn this round, skipping defeated ones and <paramref name="excluded"/>. When everybody
    /// has acted, a new round starts from the top of the order. Tracking turns per round instead
    /// of a position in the order keeps initiative changes during combat from skipping or
    /// repeating anyone's turn.
    /// </summary>
    private bool TryAdvance(Guid? excluded)
    {
        var able = TurnOrder.Where(c => !c.IsDefeated && c.Id != excluded).ToList();
        if (able.Count == 0)
        {
            return false;
        }

        var next = able.FirstOrDefault(c => c.LastTurnRound != Round);
        if (next is null)
        {
            Round++;
            next = able[0];
        }

        next.BeginTurn(Round);
        CurrentTurnId = next.Id;
        return true;
    }

    private static void EnsurePositive(int amount)
    {
        if (amount <= 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Amount must be greater than zero.");
        }
    }

    private static DomainException NotAllowed() =>
        new(DomainErrorCode.NotAllowed, "Only the DM of this session can do this.");

    private int NextSortOrder() => _combatants.Count == 0 ? 0 : _combatants.Max(c => c.SortOrder) + 1;

    private void Record(
        Actor actor,
        DateTimeOffset now,
        string type,
        Combatant? subject = null,
        params (string Key, object Value)[] data)
    {
        var at = now.ToUniversalTime();
        Version++;
        LastActivityAt = at;

        var parameters = new Dictionary<string, string>();
        if (subject is not null)
        {
            parameters["combatantId"] = subject.Id.ToString();
            parameters["name"] = subject.Name;
        }

        foreach (var (key, value) in data)
        {
            parameters[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _uncommittedEvents.Add(new CombatEvent(
            Version,
            at,
            ActorName(actor),
            type,
            parameters.ToImmutableDictionary(),
            VisibleToPlayers: subject is null || !subject.IsHidden));
    }

    private static string ActorName(Actor actor) => actor is Actor.Guest guest ? guest.Name : "DM";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, no failures.

Run: `dotnet build DmCompanionLive.slnx`
Expected: build succeeds. (`SessionsEndpoints` still uses `JoinCode.Normalize`, which stays.)

- [ ] **Step 5: Commit**

```bash
git add src/DmCompanionLive.Domain tests/DmCompanionLive.Domain.Tests
git commit -m "feat: harden combat authorization, validation, expiry and log visibility

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Persistence — foreign keys, turn tracking and the combat log

**Files:**
- Create: `src/DmCompanionLive.Infrastructure/Persistence/CombatLogEntry.cs`
- Create: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/CombatLogEntryConfiguration.cs`
- Modify: `src/DmCompanionLive.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/CombatSessionConfiguration.cs`, `TableConfiguration.cs`, `DmUserConfiguration.cs`
- Create: migration `RealTimeCombat` under `src/DmCompanionLive.Infrastructure/Persistence/Migrations/`
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs` (full file below)

**Interfaces:**
- Consumes: `CombatEvent` (with `VisibleToPlayers`), `Combatant.LastTurnRound`, `DmUser.MaxExternalIdLength`.
- Produces:
  - `CombatLogEntry` (Infrastructure.Persistence): `long Id`, `Guid SessionId`, `int Version`, `DateTimeOffset At`, `string ActorName`, `string Type`, `string Parameters` (JSON, `jsonb`), `bool VisibleToPlayers`; `static CombatLogEntry From(Guid sessionId, CombatEvent e)`; `IReadOnlyDictionary<string, string> ReadParameters()`
  - `AppDbContext.CombatLog` (`DbSet<CombatLogEntry>`)
  - FKs with cascade delete: `tables.OwnerId → dm_users`, `combat_sessions.TableId → tables`, `combat_sessions.OwnerId → dm_users`, `combat_log.SessionId → combat_sessions`; unique index `(SessionId, Version)` on `combat_log`.

- [ ] **Step 1: Write the failing tests**

Replace `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs` with:

```csharp
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
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
        var goblin = session.AddMonster(dm, new MonsterSeed("Goblin", 7, 15, SourceRef: "srd:goblin"), Now);
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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~PersistenceTests"`
Expected: build FAILS (`CombatLogEntry` and `AppDbContext.CombatLog` do not exist).

- [ ] **Step 3: Implement**

Create `src/DmCompanionLive.Infrastructure/Persistence/CombatLogEntry.cs`:

```csharp
using System.Text.Json;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Infrastructure.Persistence;

/// <summary>A persisted <see cref="CombatEvent"/>. Append-only: rows are never updated.</summary>
public sealed class CombatLogEntry
{
    private CombatLogEntry()
    {
        ActorName = string.Empty;
        Type = string.Empty;
        Parameters = "{}";
    }

    public long Id { get; private set; }
    public Guid SessionId { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string ActorName { get; private set; }
    public string Type { get; private set; }

    /// <summary>The event parameters as a JSON object of strings (a <c>jsonb</c> column).</summary>
    public string Parameters { get; private set; }

    public bool VisibleToPlayers { get; private set; }

    public static CombatLogEntry From(Guid sessionId, CombatEvent e) => new()
    {
        SessionId = sessionId,
        Version = e.Version,
        At = e.At,
        ActorName = e.ActorName,
        Type = e.Type,
        Parameters = JsonSerializer.Serialize(e.Parameters),
        VisibleToPlayers = e.VisibleToPlayers,
    };

    public IReadOnlyDictionary<string, string> ReadParameters() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Parameters) ?? [];
}
```

Create `src/DmCompanionLive.Infrastructure/Persistence/Configurations/CombatLogEntryConfiguration.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class CombatLogEntryConfiguration : IEntityTypeConfiguration<CombatLogEntry>
{
    public void Configure(EntityTypeBuilder<CombatLogEntry> builder)
    {
        builder.ToTable("combat_log");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.SessionId, e.Version }).IsUnique();
        builder.Property(e => e.ActorName).HasMaxLength(Combatant.MaxNameLength).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Parameters).HasColumnType("jsonb").IsRequired();
        builder.HasOne<CombatSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`AppDbContext.cs` — add:

```csharp
    public DbSet<CombatLogEntry> CombatLog => Set<CombatLogEntry>();
```

`CombatSessionConfiguration.cs` — add at the top `using DmCompanionLive.Domain.Tables;` and `using DmCompanionLive.Domain.Users;`, and after `builder.HasIndex(s => s.OwnerId);`:

```csharp
        builder.HasOne<Table>().WithMany().HasForeignKey(s => s.TableId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
```

(`Combatant.LastTurnRound` is mapped by convention; no configuration needed.)

`TableConfiguration.cs` — add `using DmCompanionLive.Domain.Users;` and after `builder.HasIndex(t => t.OwnerId);`:

```csharp
        builder.HasOne<DmUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
```

`DmUserConfiguration.cs` — replace `HasMaxLength(200)` on `ExternalId` with `HasMaxLength(DmUser.MaxExternalIdLength)`.

- [ ] **Step 4: Create the migration**

```bash
dotnet tool restore
dotnet ef migrations add RealTimeCombat \
  --project src/DmCompanionLive.Infrastructure \
  --startup-project src/DmCompanionLive.Infrastructure \
  --output-dir Persistence/Migrations
```

Open `*_RealTimeCombat.cs` and check it: adds `LastTurnRound` (nullable int) to `combatants`; creates `combat_log` with a `jsonb` `Parameters` column, the unique `(SessionId, Version)` index and an FK to `combat_sessions`; adds the FKs `tables.OwnerId`, `combat_sessions.TableId`, `combat_sessions.OwnerId` with `ReferentialAction.Cascade`. Nothing else (no column renames or drops).

Note: a local development database with rows from Plan 1 smoke tests may hold orphan sessions and fail to add the FKs; recreate it with `docker compose down -v && docker compose up -d`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures (all integration tests, not only persistence).

- [ ] **Step 6: Commit**

```bash
git add src/DmCompanionLive.Infrastructure tests/DmCompanionLive.Api.IntegrationTests
git commit -m "feat: persist the combat log, turn tracking and foreign keys

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: API hardening — strict JSON, conflicts, join-code retries and claim constants

**Files:**
- Create: `src/DmCompanionLive.Api/Auth/AppClaims.cs`
- Modify: `src/DmCompanionLive.Api/Auth/AuthSetup.cs`, `src/DmCompanionLive.Api/Auth/DmPrincipal.cs`
- Modify: `src/DmCompanionLive.Api/Errors/DomainExceptionHandler.cs` (full file below)
- Create: `src/DmCompanionLive.Api/Features/Sessions/JoinCodeGenerator.cs`
- Modify: `src/DmCompanionLive.Api/Features/Sessions/SessionsEndpoints.cs` (full file below)
- Modify: `src/DmCompanionLive.Api/Program.cs`
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/ApiFactory.cs`, `ClientExtensions.cs`
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Features/AuthTests.cs`, `SessionsTests.cs`; create `tests/DmCompanionLive.Api.IntegrationTests/Errors/ExceptionMappingTests.cs`

**Interfaces:**
- Consumes: `DomainErrorCode.Conflict`, `DomainErrorCode.SessionExpired`, `JoinCode.TryNormalize` (Task 2).
- Produces:
  - `AppClaims.Role` (`"role"`), `AppClaims.DmRole` (`"dm"`), `AppClaims.GuestRole` (`"guest"`), `AppClaims.GuestId` (`"guest_id"`), `AppClaims.SessionId` (`"session_id"`)
  - `public sealed class DomainExceptionHandler` with `static DomainErrorCode? CodeFor(Exception)` and `static int StatusFor(DomainErrorCode)`
  - `IJoinCodeGenerator { string New(); }`, `RandomJoinCodeGenerator`
  - Test helpers as extensions on `WebApplicationFactory<Program>`: `CreateHttpsClient()`, `CreateDmClientAsync(string? name = null)`

- [ ] **Step 1: Move the test client helpers to extensions**

The later tasks build derived factories with `WithWebHostBuilder`, which returns a `WebApplicationFactory<Program>`, so the helpers must work on that type.

`ApiFactory.cs` — remove the `CreateHttpsClient` method and set a generous join rate limit for the shared factory (the limiter itself arrives in Task 5; the setting is harmless before):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        // All test requests share one (null) remote address: keep the join limit out of the way.
        builder.UseSetting("RateLimiting:JoinPermitsPerMinute", "100000");
    }
}
```

`ClientExtensions.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class ClientExtensions
{
    /// <summary>HTTPS base address so the Secure auth cookie is sent back.</summary>
    public static HttpClient CreateHttpsClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    public static async Task<HttpClient> CreateDmClientAsync(this WebApplicationFactory<Program> factory, string? name = null)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
```

Run: `dotnet build DmCompanionLive.slnx`
Expected: build succeeds (call sites use the same names).

- [ ] **Step 2: Write the failing tests**

Create `tests/DmCompanionLive.Api.IntegrationTests/Errors/ExceptionMappingTests.cs`:

```csharp
using DmCompanionLive.Api.Errors;
using DmCompanionLive.Domain;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.IntegrationTests.Errors;

public sealed class ExceptionMappingTests
{
    [Fact]
    public void A_lost_concurrency_race_is_a_409_conflict()
    {
        var code = DomainExceptionHandler.CodeFor(new DbUpdateConcurrencyException());

        code.Should().Be(DomainErrorCode.Conflict);
        DomainExceptionHandler.StatusFor(code!.Value).Should().Be(409);
    }

    [Fact]
    public void An_expired_session_is_gone() =>
        DomainExceptionHandler.StatusFor(DomainErrorCode.SessionExpired).Should().Be(410);

    [Fact]
    public void Other_exceptions_are_left_to_the_default_handler() =>
        DomainExceptionHandler.CodeFor(new InvalidOperationException()).Should().BeNull();
}
```

In `AuthTests` add:

```csharp
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"displayName\":null}")]
    public async Task Dev_login_without_a_name_is_a_bad_request(string body)
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.PostAsync(
            "/dev/login", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

(Use the class's existing fixture parameter name if it differs from `postgres`; add `using System.Net;` if missing.)

In `SessionsTests` add (plus `using System.Collections.Concurrent;`, `using DmCompanionLive.Api.Features.Sessions;`, `using Microsoft.AspNetCore.TestHost;`, `using Microsoft.Extensions.DependencyInjection;`):

```csharp
    [Theory]
    [InlineData("ZZZZZ2")]
    [InlineData("bad")]
    public async Task Unknown_or_malformed_codes_are_not_found(string code)
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        (await client.GetAsync($"/api/sessions/{code}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_table_can_run_several_sessions()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);

        var first = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);
        var second = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Location.Should().NotBe(second.Headers.Location);
    }

    [Fact]
    public async Task A_join_code_collision_is_retried_with_a_new_code()
    {
        var codes = new QueuedCodes();
        await using var factory = postgres.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IJoinCodeGenerator>(codes)));
        var client = await factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);
        var taken = (await (await client.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!.JoinCode;
        var fresh = JoinCode.New();
        codes.Enqueue(taken, fresh);

        var response = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!.JoinCode.Should().Be(fresh);
    }

    private sealed class QueuedCodes : IJoinCodeGenerator
    {
        private readonly ConcurrentQueue<string> _codes = new();

        public void Enqueue(params string[] codes)
        {
            foreach (var code in codes)
            {
                _codes.Enqueue(code);
            }
        }

        public string New() => _codes.TryDequeue(out var code) ? code : JoinCode.New();
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: build FAILS (`IJoinCodeGenerator`, `DomainExceptionHandler.CodeFor` missing, `DomainExceptionHandler` is internal).

- [ ] **Step 4: Implement**

Create `src/DmCompanionLive.Api/Auth/AppClaims.cs`:

```csharp
namespace DmCompanionLive.Api.Auth;

/// <summary>Claim types and values this API issues.</summary>
public static class AppClaims
{
    public const string Role = "role";
    public const string DmRole = "dm";
    public const string GuestRole = "guest";
    public const string GuestId = "guest_id";
    public const string SessionId = "session_id";
}
```

`DmPrincipal.cs` — replace `new Claim("role", "dm")` with `new Claim(AppClaims.Role, AppClaims.DmRole)`.
`AuthSetup.cs` — replace `.RequireClaim("role", "dm")` with `.RequireClaim(AppClaims.Role, AppClaims.DmRole)`.

Replace `src/DmCompanionLive.Api/Errors/DomainExceptionHandler.cs` with:

```csharp
using DmCompanionLive.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Errors;

/// <summary>Maps domain errors to ProblemDetails with a stable, localizable "code".</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (CodeFor(exception) is not { } code)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusFor(code);
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = code.ToString(),
                Detail = exception is DomainException
                    ? exception.Message
                    : "Somebody else changed this first. Reload and try again.",
                Status = httpContext.Response.StatusCode,
                Extensions = { ["code"] = code.ToString() },
            },
        });
    }

    /// <summary>Domain errors carry their own code; a lost optimistic-concurrency race is a conflict.</summary>
    public static DomainErrorCode? CodeFor(Exception exception) => exception switch
    {
        DomainException domain => domain.Code,
        DbUpdateConcurrencyException => DomainErrorCode.Conflict,
        _ => null,
    };

    public static int StatusFor(DomainErrorCode code) => code switch
    {
        DomainErrorCode.NotAllowed or DomainErrorCode.NotYourCharacter => StatusCodes.Status403Forbidden,
        DomainErrorCode.CombatantNotFound or DomainErrorCode.CharacterNotFound => StatusCodes.Status404NotFound,
        DomainErrorCode.InvalidValue => StatusCodes.Status400BadRequest,
        DomainErrorCode.SessionExpired => StatusCodes.Status410Gone,
        _ => StatusCodes.Status409Conflict,
    };
}
```

Create `src/DmCompanionLive.Api/Features/Sessions/JoinCodeGenerator.cs`:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Sessions;

/// <summary>Source of new join codes; replaced in tests to force collisions.</summary>
public interface IJoinCodeGenerator
{
    string New();
}

public sealed class RandomJoinCodeGenerator : IJoinCodeGenerator
{
    public string New() => JoinCode.New();
}
```

Replace `src/DmCompanionLive.Api/Features/Sessions/SessionsEndpoints.cs` with:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DmCompanionLive.Api.Features.Sessions;

public static class SessionsEndpoints
{
    private const int MaxJoinCodeAttempts = 5;

    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tables/{tableId:guid}/sessions", Create).RequireAuthorization(AuthSetup.DmPolicy).WithTags("Sessions");
        app.MapGet("/api/sessions/{code}", GetDmView).RequireAuthorization(AuthSetup.DmPolicy).WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Created<SessionCreatedDto>, NotFound>> Create(
        Guid tableId,
        ClaimsPrincipal user,
        AppDbContext db,
        TimeProvider clock,
        IJoinCodeGenerator codes,
        CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == tableId && t.OwnerId == ownerId, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        for (var attempt = 1; ; attempt++)
        {
            var session = table.StartSession(codes.New(), clock.GetUtcNow());
            db.Sessions.Add(session);
            try
            {
                await db.SaveChangesAsync(ct);
                return TypedResults.Created(
                    $"/api/sessions/{session.JoinCode}", new SessionCreatedDto(session.Id, session.JoinCode));
            }
            catch (DbUpdateException e) when (
                attempt < MaxJoinCodeAttempts
                && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The unique index on the join code is the source of truth: on a collision, try a fresh code.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task<Results<Ok<DmView>, NotFound>> GetDmView(
        string code, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        if (!JoinCode.TryNormalize(code, out var normalized))
        {
            return TypedResults.NotFound();
        }

        var ownerId = user.GetDmUserId();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.JoinCode == normalized && s.OwnerId == ownerId, ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(CombatViews.ForDm(session));
    }
}
```

`Program.cs` — replace the `ConfigureHttpJsonOptions` call and register the generator:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // A null or missing value for a non-nullable member is a 400, never a NullReferenceException (500).
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddSingleton<IJoinCodeGenerator, RandomJoinCodeGenerator>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures.

- [ ] **Step 6: Commit**

```bash
git add src/DmCompanionLive.Api tests/DmCompanionLive.Api.IntegrationTests
git commit -m "fix: reject null JSON members, map concurrency conflicts and retry join-code collisions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Guest join — `GuestToken`, guest authentication and rate limiting

**Files:**
- Create: `src/DmCompanionLive.Api/Auth/GuestTokens.cs`, `src/DmCompanionLive.Api/Auth/GuestTokenHandler.cs`
- Modify: `src/DmCompanionLive.Api/Auth/AuthSetup.cs` (full file below)
- Create: `src/DmCompanionLive.Api/Features/Sessions/JoinSessionEndpoint.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs`
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/DmCompanionLive.Api.IntegrationTests.csproj`
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/PostgresFixture.cs`
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/Scenario.cs`
- Test: create `tests/DmCompanionLive.Api.IntegrationTests/Auth/GuestTokensTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Features/JoinTests.cs`

**Interfaces:**
- Consumes: `GuestName.Validate`, `JoinCode.TryNormalize`, `CombatSession.IsExpiredAt`, `DomainErrorCode.SessionExpired` (Task 2); `AppClaims` and test client extensions (Task 4).
- Produces:
  - `record GuestTicket(Guid SessionId, Guid GuestId, string Name)`
  - `GuestTokens` (singleton): `string Issue(GuestTicket)`, `GuestTicket? Read(string token)`, `GuestTokens.Lifetime` (12 h)
  - `GuestTokenHandler.Scheme` (`"GuestToken"`): reads `Authorization: Bearer <token>`, or `access_token` in the query string on `/hubs/*`; claims `AppClaims.Role = guest`, `AppClaims.GuestId`, `AppClaims.SessionId`, `ClaimTypes.Name`
  - `AuthSetup.CombatPolicy` (`"CombatParticipant"`): cookie or guest token
  - `POST /api/sessions/{code}/join` body `{ name }` → `200 JoinResponse(Guid SessionId, Guid GuestId, string Name, string GuestToken)`; 404 unknown code; 400 invalid name; 409 `SessionEnded`; 410 `SessionExpired`; 429 when rate limited. Policy name `JoinSessionEndpoint.RateLimitPolicy`, config `RateLimiting:JoinPermitsPerMinute` (default 10, per remote IP).
  - Test helpers: `Scenario.CreateSessionAsync(this HttpClient dm, params string[] characters)` → `Scenario.SessionCreatedDto`; `Scenario.JoinAsync(this WebApplicationFactory<Program>, string code, string name)` → `Scenario.JoinResponse`; `PostgresFixture.ChangeSessionAsync(Guid sessionId, Action<CombatSession> change)`

- [ ] **Step 1: Add the test package**

In the integration test `.csproj`, add to the package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.11.1" />
```

- [ ] **Step 2: Add the test helpers**

Create `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/Scenario.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

/// <summary>Builds the usual starting point of a combat test through the public API.</summary>
public static class Scenario
{
    public sealed record TableDto(Guid Id, string Name);

    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public sealed record JoinResponse(Guid SessionId, Guid GuestId, string Name, string GuestToken);

    /// <summary>Creates a table with one 20 HP / AC 15 character per name and starts a session for it.</summary>
    public static async Task<SessionCreatedDto> CreateSessionAsync(this HttpClient dm, params string[] characters)
    {
        var table = (await (await dm.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        foreach (var name in characters)
        {
            (await dm.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name, maxHp = 20, ac = 15, dexModifier = 0 }))
                .EnsureSuccessStatusCode();
        }

        var response = await dm.PostAsync($"/api/tables/{table.Id}/sessions", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;
    }

    public static async Task<JoinResponse> JoinAsync(this WebApplicationFactory<Program> factory, string code, string name)
    {
        var response = await factory.CreateHttpsClient().PostAsJsonAsync($"/api/sessions/{code}/join", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JoinResponse>(TestJson.Options))!;
    }
}
```

In `PostgresFixture` add (with `using DmCompanionLive.Domain.Combat;`):

```csharp
    /// <summary>Changes a stored session directly through the domain, bypassing the API.</summary>
    public async Task ChangeSessionAsync(Guid sessionId, Action<CombatSession> change)
    {
        await using var db = CreateDbContext();
        var session = await db.Sessions.SingleAsync(s => s.Id == sessionId);
        change(session);
        await db.SaveChangesAsync();
    }
```

- [ ] **Step 3: Write the failing tests**

Create `tests/DmCompanionLive.Api.IntegrationTests/Auth/GuestTokensTests.cs`:

```csharp
using DmCompanionLive.Api.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Auth;

public sealed class GuestTokensTests
{
    // Data Protection checks expiry against the real clock, so the fake clock starts at real "now".
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly IDataProtectionProvider _keys = new EphemeralDataProtectionProvider();

    [Fact]
    public void An_issued_token_reads_back_the_same_ticket()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var ticket = new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana");

        tokens.Read(tokens.Issue(ticket)).Should().Be(ticket);
    }

    [Fact]
    public void Tampered_or_foreign_tokens_are_rejected()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var token = tokens.Issue(new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana"));

        tokens.Read(token[..^4] + "AAAA").Should().BeNull();
        tokens.Read("not-a-token").Should().BeNull();
        new GuestTokens(new EphemeralDataProtectionProvider(), _clock).Read(token).Should().BeNull();
    }

    [Fact]
    public void Tokens_expire_after_their_lifetime()
    {
        var tokens = new GuestTokens(_keys, _clock);
        var token = tokens.Issue(new GuestTicket(Guid.NewGuid(), Guid.NewGuid(), "Ana"));

        _clock.Advance(GuestTokens.Lifetime + TimeSpan.FromMinutes(1));

        tokens.Read(token).Should().BeNull();
    }
}
```

Create `tests/DmCompanionLive.Api.IntegrationTests/Features/JoinTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.Hosting;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class JoinTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_guest_joins_with_the_code_and_a_name()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode.ToLowerInvariant()}/join", new { name = "  Ana " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var joined = (await response.Content.ReadFromJsonAsync<Scenario.JoinResponse>(TestJson.Options))!;
        joined.SessionId.Should().Be(session.SessionId);
        joined.Name.Should().Be("Ana");
        joined.GuestId.Should().NotBeEmpty();
        joined.GuestToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Every_join_is_a_new_guest()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var first = await postgres.Factory.JoinAsync(session.JoinCode, "Ana");
        var second = await postgres.Factory.JoinAsync(session.JoinCode, "Ana");

        second.GuestId.Should().NotBe(first.GuestId);
    }

    [Theory]
    [InlineData("ZZZZZ2")]
    [InlineData("bad")]
    public async Task Unknown_or_malformed_codes_are_not_found(string code)
    {
        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{code}/join", new { name = "Ana" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Blank_or_long_names_are_rejected(string name)
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("InvalidValue");
    }

    [Fact]
    public async Task A_missing_name_is_a_bad_request()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_ended_session_cannot_be_joined()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");
        await postgres.ChangeSessionAsync(session.SessionId, s => s.End(new Actor.Dm(s.OwnerId), DateTimeOffset.UtcNow));

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name = "Ana" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("SessionEnded");
    }

    [Fact]
    public async Task Joining_is_rate_limited()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");
        await using var limited = postgres.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:JoinPermitsPerMinute", "2"));
        var client = limited.CreateHttpsClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync($"/api/sessions/{session.JoinCode}/join", new { name = "Ana" })).StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: build FAILS (`GuestTokens`, `GuestTicket` do not exist).

- [ ] **Step 5: Implement**

Create `src/DmCompanionLive.Api/Auth/GuestTokens.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DmCompanionLive.Api.Auth;

/// <summary>What a guest token proves: this guest, with this name, joined this session.</summary>
public sealed record GuestTicket(Guid SessionId, Guid GuestId, string Name);

/// <summary>Issues and reads guest tokens signed and encrypted with ASP.NET Core Data Protection.</summary>
public sealed class GuestTokens(IDataProtectionProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("DmCompanionLive.GuestToken.v1").ToTimeLimitedDataProtector();

    public string Issue(GuestTicket ticket) =>
        _protector.Protect(JsonSerializer.Serialize(ticket), clock.GetUtcNow().Add(Lifetime));

    /// <summary>The ticket inside <paramref name="token"/>, or null if it is forged, damaged or expired.</summary>
    public GuestTicket? Read(string token)
    {
        try
        {
            var json = _protector.Unprotect(token, out var expiration);
            return expiration < clock.GetUtcNow() ? null : JsonSerializer.Deserialize<GuestTicket>(json);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
```

Create `src/DmCompanionLive.Api/Auth/GuestTokenHandler.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DmCompanionLive.Api.Auth;

/// <summary>Authenticates a player from their guest token.</summary>
public sealed class GuestTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    GuestTokens tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "GuestToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = TokenFrom(Request);
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (tokens.Read(token) is not { } ticket)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired guest token."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(AppClaims.Role, AppClaims.GuestRole),
                new Claim(AppClaims.GuestId, ticket.GuestId.ToString()),
                new Claim(AppClaims.SessionId, ticket.SessionId.ToString()),
                new Claim(ClaimTypes.Name, ticket.Name),
            ],
            Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    private static string? TokenFrom(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        // Browsers cannot set headers on WebSocket requests, so SignalR sends the token in the query string.
        return request.Path.StartsWithSegments("/hubs") ? request.Query["access_token"].FirstOrDefault() : null;
    }
}
```

Replace `src/DmCompanionLive.Api/Auth/AuthSetup.cs` with:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DmCompanionLive.Api.Auth;

public static class AuthSetup
{
    public const string DmPolicy = "Dm";

    /// <summary>The DM (cookie) or a player (guest token): whoever may connect to a combat.</summary>
    public const string CombatPolicy = "CombatParticipant";

    public static IServiceCollection AddDmAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDataProtection();
        services.AddSingleton<GuestTokens>();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "dmcl.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                // An API answers 401/403; it never redirects to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, GuestTokenHandler>(GuestTokenHandler.Scheme, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(DmPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Role, AppClaims.DmRole));
            options.AddPolicy(CombatPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, GuestTokenHandler.Scheme)
                .RequireAuthenticatedUser());
        });
        return services;
    }
}
```

Create `src/DmCompanionLive.Api/Features/Sessions/JoinSessionEndpoint.cs`:

```csharp
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Sessions;

/// <summary>A player joins a combat with its code and a name, and gets a guest token.</summary>
public static class JoinSessionEndpoint
{
    public const string RateLimitPolicy = "join";

    public sealed record JoinRequest(string Name);

    public sealed record JoinResponse(Guid SessionId, Guid GuestId, string Name, string GuestToken);

    public static IEndpointRouteBuilder MapJoinSession(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions/{code}/join", Join)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Ok<JoinResponse>, NotFound>> Join(
        string code, JoinRequest request, AppDbContext db, GuestTokens tokens, TimeProvider clock, CancellationToken ct)
    {
        var name = GuestName.Validate(request.Name);
        if (!JoinCode.TryNormalize(code, out var normalized))
        {
            return TypedResults.NotFound();
        }

        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.JoinCode == normalized, ct);
        if (session is null)
        {
            return TypedResults.NotFound();
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }

        if (session.IsExpiredAt(clock.GetUtcNow()))
        {
            throw new DomainException(DomainErrorCode.SessionExpired, "The combat expired after 12 hours without activity.");
        }

        var ticket = new GuestTicket(session.Id, Guid.NewGuid(), name);
        return TypedResults.Ok(new JoinResponse(ticket.SessionId, ticket.GuestId, ticket.Name, tokens.Issue(ticket)));
    }
}
```

`Program.cs` — add `using System.Threading.RateLimiting;` and, after `builder.Services.AddDmAuthentication(builder.Environment);`:

```csharp
var joinPermits = builder.Configuration.GetValue("RateLimiting:JoinPermitsPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client address. Behind a reverse proxy this needs ForwardedHeaders (Plan 5).
    options.AddPolicy(JoinSessionEndpoint.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = joinPermits, Window = TimeSpan.FromMinutes(1) }));
});
```

after `app.UseAuthorization();`:

```csharp
app.UseRateLimiter();
```

and after `app.MapSessions();`:

```csharp
app.MapJoinSession();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures.

- [ ] **Step 7: Commit**

```bash
git add src/DmCompanionLive.Api tests/DmCompanionLive.Api.IntegrationTests
git commit -m "feat: let players join a combat with a code and a signed guest token

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Combat hub — connect, join and snapshots

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Combat/CombatContracts.cs`, `CombatGroups.cs`, `SessionPresence.cs`, `CombatIdentity.cs`, `CombatHub.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs`
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/DmCompanionLive.Api.IntegrationTests.csproj`, `Infrastructure/ClientExtensions.cs`
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/HubClients.cs`, `Infrastructure/Inbox.cs`
- Test: create `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubConnectionTests.cs`

**Interfaces:**
- Consumes: `AuthSetup.CombatPolicy`, `GuestTokenHandler.Scheme`, `AppClaims` (Tasks 4–5); `CombatLogEntry`, `AppDbContext.CombatLog` (Task 3); `Scenario`, `PostgresFixture.ChangeSessionAsync` (Task 5).
- Produces:
  - Hub at `/hubs/combat`, typed client `ICombatClient { Task DmSnapshot(DmView); Task PlayerSnapshot(PlayerView); Task LogAppended(IReadOnlyList<LogEntryDto>); }`
  - Hub methods `Task<SnapshotResult> Join(string code)`, `Task<SnapshotResult> GetSnapshot()`
  - `record LogEntryDto(int Version, DateTimeOffset At, string Actor, string Type, IReadOnlyDictionary<string, string> Parameters)` with `From(CombatLogEntry)` and `From(CombatEvent)`
  - `record SnapshotResult(string? Error, DmView? Dm, PlayerView? Player, IReadOnlyList<LogEntryDto> Log)` with `bool Succeeded`, `ForDm`, `ForPlayer`, `Fail`
  - `record CommandResult(string? Error, int Version)` with `bool Succeeded`, `Ok(int)`, `Fail(string, int)` (used in Task 7)
  - `CombatErrors.SessionNotFound`, `CombatErrors.NotJoined`, `CombatErrors.Conflict`
  - `CombatGroups.Dm(Guid)`, `CombatGroups.Players(Guid)`, `CombatGroups.Guest(Guid sessionId, Guid guestId)`
  - `SessionPresence` (singleton): `AddGuest(string connectionId, Guid sessionId, Guid guestId)`, `Remove(string connectionId)`, `IReadOnlyList<Guid> GuestsIn(Guid sessionId)`
  - `CombatIdentity(Actor Actor, Guid? TokenSessionId)` with `static CombatIdentity? From(ClaimsPrincipal?)` and `bool CanJoin(CombatSession)`
  - `CombatHub.JoinedSessionId` (private) read from `Context.Items`
  - Test helpers: `TestDm(HttpClient Http, string Cookie)`, `CreateDmAsync(this WebApplicationFactory<Program>, string? name = null)`, `ConnectAsync(this WebApplicationFactory<Program>, string? cookie = null, string? guestToken = null)` → started `HubConnection`, `Inbox<T>(HubConnection, string method)` with `All`, `WaitForAsync(Func<T, bool>)`, `WaitUntilAsync(Func<IReadOnlyList<T>, bool>)`

- [ ] **Step 1: Add the SignalR client package and the test helpers**

In the integration test `.csproj` add:

```xml
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.15" />
```

In `ClientExtensions` add:

```csharp
    /// <summary>A DM whose auth cookie is also available as a raw header value for hub connections.</summary>
    public sealed record TestDm(HttpClient Http, string Cookie);

    public static async Task<TestDm> CreateDmAsync(this WebApplicationFactory<Program> factory, string? name = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';')[0])
            .Single(pair => pair.StartsWith("dmcl.auth=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new TestDm(client, cookie);
    }
```

Create `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/HubClients.cs`:

```csharp
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class HubClients
{
    /// <summary>
    /// A started connection to the combat hub through the in-memory test server. Long polling,
    /// because the test server's handler does not do WebSockets; the hub code is the same.
    /// </summary>
    public static async Task<HubConnection> ConnectAsync(
        this WebApplicationFactory<Program> factory, string? cookie = null, string? guestToken = null)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/combat", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }

                if (guestToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(guestToken);
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
```

Create `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/Inbox.cs`:

```csharp
using Microsoft.AspNetCore.SignalR.Client;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

/// <summary>Records every message a hub connection receives for one method, and waits for them.</summary>
public sealed class Inbox<T>
{
    private readonly List<T> _items = [];
    private readonly SemaphoreSlim _arrived = new(0);

    public Inbox(HubConnection connection, string method) =>
        connection.On<T>(method, item =>
        {
            lock (_items)
            {
                _items.Add(item);
            }

            _arrived.Release();
        });

    public IReadOnlyList<T> All
    {
        get
        {
            lock (_items)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>The latest message matching <paramref name="match"/>, waiting for it if needed.</summary>
    public async Task<T> WaitForAsync(Func<T, bool> match, TimeSpan? timeout = null)
    {
        T found = default!;
        await WaitUntilAsync(
            items =>
            {
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    if (match(items[i]))
                    {
                        found = items[i];
                        return true;
                    }
                }

                return false;
            },
            timeout);
        return found;
    }

    public async Task WaitUntilAsync(Func<IReadOnlyList<T>, bool> condition, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (_items)
            {
                if (condition(_items))
                {
                    return;
                }
            }

            try
            {
                await _arrived.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Timed out waiting for {typeof(T).Name} messages.");
            }
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubConnectionTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~CombatHubConnectionTests"`
Expected: build FAILS (`DmCompanionLive.Api.Features.Combat` does not exist).

- [ ] **Step 4: Implement**

Create `src/DmCompanionLive.Api/Features/Combat/CombatContracts.cs`:

```csharp
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Messages the server pushes to connected clients.</summary>
public interface ICombatClient
{
    Task DmSnapshot(DmView view);

    Task PlayerSnapshot(PlayerView view);

    Task LogAppended(IReadOnlyList<LogEntryDto> entries);
}

/// <summary>A log entry as clients see it: a type and parameters they translate themselves.</summary>
public sealed record LogEntryDto(
    int Version,
    DateTimeOffset At,
    string Actor,
    string Type,
    IReadOnlyDictionary<string, string> Parameters)
{
    public static LogEntryDto From(CombatLogEntry entry) =>
        new(entry.Version, entry.At, entry.ActorName, entry.Type, entry.ReadParameters());

    public static LogEntryDto From(CombatEvent e) =>
        new(e.Version, e.At, e.ActorName, e.Type, e.Parameters);
}

/// <summary>The caller's view of the session plus the latest log entries it may see.</summary>
public sealed record SnapshotResult(string? Error, DmView? Dm, PlayerView? Player, IReadOnlyList<LogEntryDto> Log)
{
    public bool Succeeded => Error is null;

    public static SnapshotResult ForDm(DmView view, IReadOnlyList<LogEntryDto> log) => new(null, view, null, log);

    public static SnapshotResult ForPlayer(PlayerView view, IReadOnlyList<LogEntryDto> log) => new(null, null, view, log);

    public static SnapshotResult Fail(string error) => new(error, null, null, []);
}

/// <summary>
/// Outcome of a command. <see cref="Version"/> is the session version after a success, or the
/// version the server knows after a failure; on <c>Conflict</c> the client re-fetches the snapshot.
/// </summary>
public sealed record CommandResult(string? Error, int Version)
{
    public bool Succeeded => Error is null;

    public static CommandResult Ok(int version) => new(null, version);

    public static CommandResult Fail(string error, int version) => new(error, version);
}

/// <summary>Error codes the hub adds to the domain's <see cref="DomainErrorCode"/> names.</summary>
public static class CombatErrors
{
    public const string SessionNotFound = "SessionNotFound";
    public const string NotJoined = "NotJoined";
    public const string Conflict = nameof(DomainErrorCode.Conflict);
}
```

Create `src/DmCompanionLive.Api/Features/Combat/CombatGroups.cs`:

```csharp
namespace DmCompanionLive.Api.Features.Combat;

/// <summary>SignalR group names. Each guest has its own group because their view says which PC is theirs.</summary>
public static class CombatGroups
{
    public static string Dm(Guid sessionId) => $"session:{sessionId}:dm";

    public static string Players(Guid sessionId) => $"session:{sessionId}:players";

    public static string Guest(Guid sessionId, Guid guestId) => $"session:{sessionId}:guest:{guestId}";
}
```

Create `src/DmCompanionLive.Api/Features/Combat/SessionPresence.cs`:

```csharp
using System.Collections.Concurrent;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Which guests are connected to which session, so a change can send each guest their own view.
/// In memory: valid for a single replica only (a documented limitation).
/// </summary>
public sealed class SessionPresence
{
    private readonly ConcurrentDictionary<string, (Guid SessionId, Guid GuestId)> _guests = new();

    public void AddGuest(string connectionId, Guid sessionId, Guid guestId) => _guests[connectionId] = (sessionId, guestId);

    public void Remove(string connectionId) => _guests.TryRemove(connectionId, out _);

    public IReadOnlyList<Guid> GuestsIn(Guid sessionId) =>
        _guests.Values.Where(g => g.SessionId == sessionId).Select(g => g.GuestId).Distinct().ToList();
}
```

Create `src/DmCompanionLive.Api/Features/Combat/CombatIdentity.cs`:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Who is on the other end of a hub connection, resolved from the authenticated identities.</summary>
public sealed record CombatIdentity(Actor Actor, Guid? TokenSessionId)
{
    /// <summary>A guest token wins over a DM cookie: on the player page the token is sent explicitly.</summary>
    public static CombatIdentity? From(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var guest = user.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == GuestTokenHandler.Scheme);
        if (guest is not null)
        {
            return new CombatIdentity(
                new Actor.Guest(Guid.Parse(guest.FindFirst(AppClaims.GuestId)!.Value), guest.Name ?? string.Empty),
                Guid.Parse(guest.FindFirst(AppClaims.SessionId)!.Value));
        }

        var dm = user.Identities.FirstOrDefault(i => i.IsAuthenticated && i.HasClaim(AppClaims.Role, AppClaims.DmRole));
        return dm is null
            ? null
            : new CombatIdentity(new Actor.Dm(Guid.Parse(dm.FindFirst(ClaimTypes.NameIdentifier)!.Value)), null);
    }

    public bool CanJoin(CombatSession session) => Actor switch
    {
        Actor.Dm dm => dm.UserId == session.OwnerId,
        Actor.Guest => TokenSessionId == session.Id,
        _ => false,
    };
}
```

Create `src/DmCompanionLive.Api/Features/Combat/CombatHub.cs`:

```csharp
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Live combat. A connection first calls <see cref="Join"/> with the room code; from then on it
/// receives its view after every change and can send commands.
/// </summary>
[Authorize(Policy = AuthSetup.CombatPolicy)]
public sealed class CombatHub(AppDbContext db, SessionPresence presence) : Hub<ICombatClient>
{
    private const string SessionKey = "sessionId";
    private const int LogTail = 100;

    private Guid? JoinedSessionId =>
        Context.Items.TryGetValue(SessionKey, out var value) && value is Guid id ? id : null;

    public async Task<SnapshotResult> Join(string code)
    {
        if (CombatIdentity.From(Context.User) is not { } identity || !JoinCode.TryNormalize(code, out var normalized))
        {
            return SnapshotResult.Fail(CombatErrors.SessionNotFound);
        }

        var session = await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.JoinCode == normalized, Context.ConnectionAborted);

        // The same answer for "no such code" and "not yours": nobody learns which codes exist.
        if (session is null || !identity.CanJoin(session))
        {
            return SnapshotResult.Fail(CombatErrors.SessionNotFound);
        }

        if (identity.Actor is Actor.Guest guest)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Players(session.Id));
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Guest(session.Id, guest.GuestId));
            presence.AddGuest(Context.ConnectionId, session.Id, guest.GuestId);
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Dm(session.Id));
        }

        Context.Items[SessionKey] = session.Id;
        return await SnapshotAsync(session, identity.Actor);
    }

    /// <summary>The full current view, e.g. after a reconnect or a <c>Conflict</c>.</summary>
    public async Task<SnapshotResult> GetSnapshot()
    {
        if (JoinedSessionId is not { } sessionId || CombatIdentity.From(Context.User) is not { } identity)
        {
            return SnapshotResult.Fail(CombatErrors.NotJoined);
        }

        var session = await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, Context.ConnectionAborted);
        return session is null
            ? SnapshotResult.Fail(CombatErrors.SessionNotFound)
            : await SnapshotAsync(session, identity.Actor);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<SnapshotResult> SnapshotAsync(CombatSession session, Actor actor)
    {
        var isDm = actor is Actor.Dm;
        var latest = await db.CombatLog.AsNoTracking()
            .Where(e => e.SessionId == session.Id && (isDm || e.VisibleToPlayers))
            .OrderByDescending(e => e.Version)
            .Take(LogTail)
            .ToListAsync(Context.ConnectionAborted);
        var log = latest.OrderBy(e => e.Version).Select(LogEntryDto.From).ToList();

        return actor is Actor.Guest guest
            ? SnapshotResult.ForPlayer(CombatViews.ForPlayer(session, guest.GuestId), log)
            : SnapshotResult.ForDm(CombatViews.ForDm(session), log);
    }
}
```

`Program.cs` — add `using DmCompanionLive.Api.Features.Combat;`; after the rate limiter registration:

```csharp
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Same shapes as the REST API: enums as names, nulls in non-nullable members rejected.
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.RespectNullableAnnotations = true;
    });
builder.Services.AddSingleton<SessionPresence>();
```

and after `app.MapJoinSession();`:

```csharp
app.MapHub<CombatHub>("/hubs/combat");
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures.

- [ ] **Step 6: Commit**

```bash
git add src/DmCompanionLive.Api tests/DmCompanionLive.Api.IntegrationTests
git commit -m "feat: add the combat hub with per-role snapshots

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Combat hub — commands, optimistic concurrency, log and broadcast

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Combat/CombatCommandRunner.cs`, `CombatBroadcaster.cs`, `RandomDiceRoller.cs`
- Modify: `src/DmCompanionLive.Api/Features/Combat/CombatContracts.cs` (add `MonsterInput`), `CombatHub.cs` (constructor and commands)
- Modify: `src/DmCompanionLive.Api/Program.cs`
- Test: create `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs`

**Interfaces:**
- Consumes: everything from Task 6; `CombatLogEntry.From` (Task 3); domain commands (Tasks 1–2).
- Produces:
  - `record MonsterInput(string Name, int MaxHp, int Ac, int DexModifier = 0, bool IsHidden = false)` with `MonsterSeed ToSeed()`
  - `CombatCommandRunner` (scoped): `Task<CommandResult> RunAsync(Guid sessionId, Actor actor, int expectedVersion, Action<CombatSession, DateTimeOffset> command, CancellationToken ct)`
  - `CombatBroadcaster` (singleton): `Task BroadcastAsync(CombatSession session, IReadOnlyList<CombatEvent> events)`
  - `RandomDiceRoller : IDiceRoller` (singleton)
  - Hub commands, all returning `Task<CommandResult>` and taking `int expectedVersion` last: `AddMonster(MonsterInput)`, `RollMonsterInitiative()`, `Start()`, `NextTurn()`, `End()`, `ApplyDamage(Guid combatantId, int amount)`, `Heal(Guid combatantId, int amount)`, `SetTempHp(Guid combatantId, int amount)`, `SetInitiative(Guid combatantId, int value)`, `SetConditions(Guid combatantId, IReadOnlyList<Condition> conditions)`, `Reveal(Guid combatantId)`, `Remove(Guid combatantId)`, `Claim(Guid combatantId)`

- [ ] **Step 1: Write the failing tests**

Create `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs`:

```csharp
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

        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", new MonsterInput("Assassin", 30, 15, IsHidden: true), version));
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", new MonsterInput("Orc", 15, 13), version));
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
        var current = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", new MonsterInput("Orc", 15, 13), initial));

        var stale = await dm.Hub.InvokeAsync<CommandResult>("AddMonster", new MonsterInput("Goblin", 7, 15), initial);

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
                "AddMonster", new MonsterInput($"Goblin {i}", 7, 15), version)));

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
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("AddMonster", new MonsterInput("Assassin", 30, 15, IsHidden: true), version));
        var assassin = Id(await dm.Snapshots.WaitForAsync(v => Version(v) == version), "Assassin");
        version = Accepted(await dm.Hub.InvokeAsync<CommandResult>("Reveal", assassin, version));

        var dmLog = (await dm.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;
        var playerLog = (await ana.Hub.InvokeAsync<SnapshotResult>("GetSnapshot")).Log;

        dmLog.Select(e => e.Type).Should().Equal("MonsterAdded", "Revealed");
        playerLog.Select(e => e.Type).Should().Equal("Revealed");
        await ana.Log.WaitForAsync(entries => entries.GetArrayLength() > 0);
        ana.Log.All.SelectMany(m => m.EnumerateArray()).Select(e => e.GetProperty("type").GetString())
            .Should().Equal("Revealed");
        await using var db = postgres.CreateDbContext();
        (await db.CombatLog.CountAsync(e => e.SessionId == dm.Joined.Dm.SessionId)).Should().Be(2);
    }

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

    private async Task<Participant> ConnectAsync(string code, string? cookie = null, string? guestToken = null)
    {
        var hub = await postgres.Factory.ConnectAsync(cookie, guestToken);
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~CombatHubCommandTests"`
Expected: build FAILS (`MonsterInput` does not exist).

- [ ] **Step 3: Implement**

In `CombatContracts.cs` add:

```csharp
/// <summary>A monster the DM adds by hand (SRD monsters and templates come in Plan 3).</summary>
public sealed record MonsterInput(string Name, int MaxHp, int Ac, int DexModifier = 0, bool IsHidden = false)
{
    public MonsterSeed ToSeed() => new(Name, MaxHp, Ac, DexModifier, IsHidden);
}
```

Create `src/DmCompanionLive.Api/Features/Combat/RandomDiceRoller.cs`:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

public sealed class RandomDiceRoller : IDiceRoller
{
    public int D20() => Random.Shared.Next(1, 21);
}
```

Create `src/DmCompanionLive.Api/Features/Combat/CombatBroadcaster.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Sends every participant of a session the view and log entries they are allowed to see.</summary>
public sealed class CombatBroadcaster(IHubContext<CombatHub, ICombatClient> hub, SessionPresence presence)
{
    public async Task BroadcastAsync(CombatSession session, IReadOnlyList<CombatEvent> events)
    {
        var dm = hub.Clients.Group(CombatGroups.Dm(session.Id));
        await dm.DmSnapshot(CombatViews.ForDm(session));
        if (events.Count > 0)
        {
            await dm.LogAppended(events.Select(LogEntryDto.From).ToList());
        }

        foreach (var guestId in presence.GuestsIn(session.Id))
        {
            await hub.Clients.Group(CombatGroups.Guest(session.Id, guestId))
                .PlayerSnapshot(CombatViews.ForPlayer(session, guestId));
        }

        var visible = events.Where(e => e.VisibleToPlayers).Select(LogEntryDto.From).ToList();
        if (visible.Count > 0)
        {
            await hub.Clients.Group(CombatGroups.Players(session.Id)).LogAppended(visible);
        }
    }
}
```

Create `src/DmCompanionLive.Api/Features/Combat/CombatCommandRunner.cs`:

```csharp
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Runs one command: load the session → check <c>expectedVersion</c> → domain method → save the new
/// state and its log entries in one transaction (optimistic concurrency on <c>Version</c>) → broadcast.
/// </summary>
public sealed class CombatCommandRunner(AppDbContext db, TimeProvider clock, CombatBroadcaster broadcaster)
{
    public async Task<CommandResult> RunAsync(
        Guid sessionId,
        Actor actor,
        int expectedVersion,
        Action<CombatSession, DateTimeOffset> command,
        CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return CommandResult.Fail(CombatErrors.SessionNotFound, 0);
        }

        if (session.Version != expectedVersion)
        {
            return CommandResult.Fail(CombatErrors.Conflict, session.Version);
        }

        try
        {
            command(session, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return CommandResult.Fail(e.Code.ToString(), session.Version);
        }

        var events = session.UncommittedEvents.ToList();
        if (events.Count > 0)
        {
            db.CombatLog.AddRange(events.Select(e => CombatLogEntry.From(session.Id, e)));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (
                e is DbUpdateConcurrencyException
                || e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another command saved this version first: either the Version check on the session row or
                // the unique (SessionId, Version) index on the log caught it, depending on statement order.
                return CommandResult.Fail(CombatErrors.Conflict, expectedVersion);
            }

            session.ClearUncommittedEvents();
        }

        // A command that changed nothing (re-claiming your own character) still re-sends the views,
        // so the caller always ends up with a fresh snapshot.
        await broadcaster.BroadcastAsync(session, events);
        return CommandResult.Ok(session.Version);
    }
}
```

In `CombatHub.cs`, change the constructor and add the commands (keep `Join`, `GetSnapshot`, `OnDisconnectedAsync`, `SnapshotAsync`):

```csharp
public sealed class CombatHub(
    AppDbContext db,
    SessionPresence presence,
    CombatCommandRunner runner,
    IDiceRoller dice) : Hub<ICombatClient>
{
```

```csharp
    public Task<CommandResult> AddMonster(MonsterInput monster, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.AddMonster(actor, monster.ToSeed(), now));

    public Task<CommandResult> RollMonsterInitiative(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.RollMonsterInitiative(actor, dice, now));

    public Task<CommandResult> Start(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Start(actor, now));

    public Task<CommandResult> NextTurn(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.NextTurn(actor, now));

    public Task<CommandResult> End(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.End(actor, now));

    public Task<CommandResult> ApplyDamage(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.ApplyDamage(actor, combatantId, amount, now));

    public Task<CommandResult> Heal(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Heal(actor, combatantId, amount, now));

    public Task<CommandResult> SetTempHp(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetTempHp(actor, combatantId, amount, now));

    public Task<CommandResult> SetInitiative(Guid combatantId, int value, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetInitiative(actor, combatantId, value, now));

    public Task<CommandResult> SetConditions(Guid combatantId, IReadOnlyList<Condition> conditions, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetConditions(
            actor, combatantId, conditions.Aggregate(Condition.None, (all, c) => all | c), now));

    public Task<CommandResult> Reveal(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Reveal(actor, combatantId, now));

    public Task<CommandResult> Remove(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Remove(actor, combatantId, now));

    public Task<CommandResult> Claim(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Claim(actor, combatantId, now));

    private async Task<CommandResult> Run(int expectedVersion, Action<CombatSession, Actor, DateTimeOffset> command)
    {
        if (JoinedSessionId is not { } sessionId || CombatIdentity.From(Context.User) is not { } identity)
        {
            return CommandResult.Fail(CombatErrors.NotJoined, 0);
        }

        return await runner.RunAsync(
            sessionId,
            identity.Actor,
            expectedVersion,
            (session, now) => command(session, identity.Actor, now),
            Context.ConnectionAborted);
    }
```

`Program.cs` — after `builder.Services.AddSingleton<SessionPresence>();`:

```csharp
builder.Services.AddSingleton<CombatBroadcaster>();
builder.Services.AddScoped<CombatCommandRunner>();
builder.Services.AddSingleton<IDiceRoller, RandomDiceRoller>();
```

(`IDiceRoller` needs `using DmCompanionLive.Domain.Combat;` in `Program.cs`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures. Run the hub tests three times in a row (`--filter "FullyQualifiedName~CombatHub"`) to check they are not flaky; they must pass every time.

- [ ] **Step 5: Commit**

```bash
git add src/DmCompanionLive.Api tests/DmCompanionLive.Api.IntegrationTests
git commit -m "feat: run combat commands over the hub and broadcast per-role views and log

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Session expiry end to end, cookie revalidation and Google sign-in

**Files:**
- Modify: `src/DmCompanionLive.Api/DmCompanionLive.Api.csproj`
- Create: `src/DmCompanionLive.Api/Features/Auth/DmUsers.cs`
- Modify: `src/DmCompanionLive.Api/Auth/AuthSetup.cs` (full file below), `src/DmCompanionLive.Api/Features/Auth/AuthEndpoints.cs` (full file below), `src/DmCompanionLive.Api/Program.cs`
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Features/AuthTests.cs`; create `tests/DmCompanionLive.Api.IntegrationTests/Features/GoogleSignInTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Features/SessionExpiryTests.cs`

**Interfaces:**
- Consumes: `CombatSession.InactivityLimit`, `DmUser.Create` validation (Task 2); hub and test helpers (Tasks 5–7).
- Produces:
  - `DmUsers.FindOrCreateAsync(AppDbContext db, string externalId, string displayName, DateTimeOffset now, CancellationToken ct)` → `DmUser`; `DmUsers.DisplayNameFrom(string?)` → `string`
  - `AuthSetup.AddDmAuthentication(IServiceCollection, IHostEnvironment, IConfiguration)`; `AuthSetup.IsGoogleConfigured(IConfiguration)`
  - `AuthEndpoints.MapAuth(this IEndpointRouteBuilder, bool googleEnabled)`; `GET /api/auth/google/login?returnUrl=` (only when configured); `AuthEndpoints.SafeReturnUrl(string?)`
  - Config keys `Authentication:Google:ClientId` / `Authentication:Google:ClientSecret`; callback path `/signin-google`

- [ ] **Step 1: Add the package and user secrets**

In `src/DmCompanionLive.Api/DmCompanionLive.Api.csproj` add to the package `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.15" />
```

and to the `PropertyGroup`:

```xml
    <UserSecretsId>dm-companion-live-api</UserSecretsId>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/DmCompanionLive.Api.IntegrationTests/Features/SessionExpiryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.Features.Combat;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SessionExpiryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_idle_session_can_no_longer_be_joined_or_commanded()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = postgres.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var dm = await factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync("Lyra");
        await using var hub = await factory.ConnectAsync(cookie: dm.Cookie);
        var joined = await hub.InvokeAsync<SnapshotResult>("Join", session.JoinCode);

        clock.Advance(CombatSession.InactivityLimit + TimeSpan.FromMinutes(1));

        var command = await hub.InvokeAsync<CommandResult>("Start", joined.Dm!.Version);
        var join = await factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name = "Ana" });

        command.Error.Should().Be("SessionExpired");
        join.StatusCode.Should().Be(HttpStatusCode.Gone);
    }
}
```

Create `tests/DmCompanionLive.Api.IntegrationTests/Features/GoogleSignInTests.cs`:

```csharp
using System.Net;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Users;
using Microsoft.AspNetCore.Hosting;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class GoogleSignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Without_credentials_google_sign_in_is_not_offered()
    {
        var response = await postgres.Factory.CreateHttpsClient().GetAsync("/api/auth/google/login");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_credentials_sign_in_redirects_to_google()
    {
        await using var factory = postgres.Factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:Google:ClientId", "test-client-id")
            .UseSetting("Authentication:Google:ClientSecret", "test-client-secret"));

        var response = await factory.CreateHttpsClient().GetAsync("/api/auth/google/login?returnUrl=/tables");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://accounts.google.com/");
        location.Should().Contain("client_id=test-client-id");
        location.Should().Contain(Uri.EscapeDataString("https://localhost/signin-google"));
    }

    [Fact]
    public async Task The_same_google_account_always_maps_to_the_same_dm()
    {
        var externalId = $"google:{Guid.NewGuid():N}";
        await using var db = postgres.CreateDbContext();

        var first = await DmUsers.FindOrCreateAsync(db, externalId, "Ana", DateTimeOffset.UtcNow, default);
        var second = await DmUsers.FindOrCreateAsync(db, externalId, "Ana B.", DateTimeOffset.UtcNow, default);

        second.Id.Should().Be(first.Id);
    }

    [Theory]
    [InlineData(null, "DM")]
    [InlineData("   ", "DM")]
    [InlineData(" Ana ", "Ana")]
    public void Display_names_from_google_are_cleaned(string? name, string expected) =>
        DmUsers.DisplayNameFrom(name).Should().Be(expected);

    [Fact]
    public void Long_display_names_from_google_are_shortened() =>
        DmUsers.DisplayNameFrom(new string('a', 120)).Should().HaveLength(DmUser.MaxDisplayNameLength);

    [Theory]
    [InlineData("/tables", "/tables")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void Only_local_return_urls_are_kept(string? returnUrl, string expected) =>
        AuthEndpoints.SafeReturnUrl(returnUrl).Should().Be(expected);
}
```

In `AuthTests` add (with `using Microsoft.EntityFrameworkCore;` and `using System.Net.Http.Json;` if missing):

```csharp
    private sealed record MeDto(Guid Id, string DisplayName);

    [Fact]
    public async Task The_cookie_of_a_deleted_user_stops_working()
    {
        var dm = await postgres.Factory.CreateDmAsync();
        var me = (await dm.Http.GetFromJsonAsync<MeDto>("/api/auth/me", TestJson.Options))!;
        await using (var db = postgres.CreateDbContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == me.Id));
            await db.SaveChangesAsync();
        }

        (await dm.Http.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: build FAILS (`DmUsers`, `AuthEndpoints.SafeReturnUrl` do not exist). `SessionExpiryTests` would already pass on its own (expiry was built in Tasks 2 and 5); it pins the end-to-end behaviour.

- [ ] **Step 4: Implement**

Create `src/DmCompanionLive.Api/Features/Auth/DmUsers.cs`:

```csharp
using DmCompanionLive.Domain.Users;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Auth;

/// <summary>First sign-in creates the DM; later sign-ins find it by external id.</summary>
public static class DmUsers
{
    public static async Task<DmUser> FindOrCreateAsync(
        AppDbContext db, string externalId, string displayName, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is not null)
        {
            return user;
        }

        user = DmUser.Create(externalId, displayName, now);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Provider names can be missing or longer than we store.</summary>
    public static string DisplayNameFrom(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "DM"
            : trimmed[..Math.Min(trimmed.Length, DmUser.MaxDisplayNameLength)];
    }
}
```

Replace `src/DmCompanionLive.Api/Auth/AuthSetup.cs` with:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Auth;

public static class AuthSetup
{
    public const string DmPolicy = "Dm";

    /// <summary>The DM (cookie) or a player (guest token): whoever may connect to a combat.</summary>
    public const string CombatPolicy = "CombatParticipant";

    public static bool IsGoogleConfigured(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration["Authentication:Google:ClientId"]);

    public static IServiceCollection AddDmAuthentication(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddDataProtection();
        services.AddSingleton<GuestTokens>();
        var authentication = services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "dmcl.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                // An API answers 401/403; it never redirects to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                // A cookie outlives its user if the user is deleted: check it against the database.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    if (!Guid.TryParse(id, out var userId)
                        || !await db.Users.AnyAsync(u => u.Id == userId, context.HttpContext.RequestAborted))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, GuestTokenHandler>(GuestTokenHandler.Scheme, _ => { });

        if (IsGoogleConfigured(configuration))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = configuration["Authentication:Google:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"]
                    ?? throw new InvalidOperationException("Missing 'Authentication:Google:ClientSecret'.");
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.CallbackPath = "/signin-google";
                // Store only the Google subject and a display name; never the e-mail.
                options.Events.OnCreatingTicket = async context =>
                {
                    var subject = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? throw new InvalidOperationException("Google did not return a subject.");
                    var services = context.HttpContext.RequestServices;
                    var user = await DmUsers.FindOrCreateAsync(
                        services.GetRequiredService<AppDbContext>(),
                        $"google:{subject}",
                        DmUsers.DisplayNameFrom(context.Principal.FindFirstValue(ClaimTypes.Name)),
                        services.GetRequiredService<TimeProvider>().GetUtcNow(),
                        context.HttpContext.RequestAborted);
                    context.Principal = DmPrincipal.Create(user);
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(DmPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Role, AppClaims.DmRole));
            options.AddPolicy(CombatPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, GuestTokenHandler.Scheme)
                .RequireAuthenticatedUser());
        });
        return services;
    }
}
```

Replace `src/DmCompanionLive.Api/Features/Auth/AuthEndpoints.cs` with:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DmCompanionLive.Api.Features.Auth;

public static class AuthEndpoints
{
    public sealed record DevLoginRequest(string DisplayName);

    public sealed record MeResponse(Guid Id, string DisplayName);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app, bool googleEnabled)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapGet("/me", Me).RequireAuthorization(AuthSetup.DmPolicy);
        group.MapPost("/logout", (Delegate)Logout).RequireAuthorization(AuthSetup.DmPolicy);
        if (googleEnabled)
        {
            group.MapGet("/google/login", GoogleLogin);
        }

        return app;
    }

    /// <summary>Development and test sign-in. Never mapped in production.</summary>
    public static IEndpointRouteBuilder MapDevLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/login", DevLogin).ExcludeFromDescription();
        return app;
    }

    /// <summary>Keeps <paramref name="returnUrl"/> only if it stays on this site (no open redirect).</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    private static ChallengeHttpResult GoogleLogin(string? returnUrl) =>
        TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
            [GoogleDefaults.AuthenticationScheme]);

    private static async Task<Ok<MeResponse>> DevLogin(
        DevLoginRequest request, AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var user = await DmUsers.FindOrCreateAsync(
            db, $"dev:{request.DisplayName.Trim().ToLowerInvariant()}", request.DisplayName, clock.GetUtcNow(), ct);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, DmPrincipal.Create(user));
        return TypedResults.Ok(new MeResponse(user.Id, user.DisplayName));
    }

    private static Ok<MeResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new MeResponse(user.GetDmUserId(), user.Identity?.Name ?? string.Empty));

    private static async Task<NoContent> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}
```

`Program.cs` — change the two calls:

```csharp
builder.Services.AddDmAuthentication(builder.Environment, builder.Configuration);
```

```csharp
app.MapAuth(AuthSetup.IsGoogleConfigured(app.Configuration));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, no failures.

- [ ] **Step 6: Commit**

```bash
git add src/DmCompanionLive.Api tests/DmCompanionLive.Api.IntegrationTests
git commit -m "feat: wire Google sign-in, revalidate DM cookies and expire idle sessions end to end

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Docs — README, spec and carry-over

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md` (§4 hub commands)
- Modify: `docs/superpowers/plans/2026-11-24-plan-1-carryover.md`

**Interfaces:**
- Consumes: everything above. Produces no code.

- [ ] **Step 1: README**

Add a section `## Real-time combat` after the existing API section (keep the README's tone and headings) with, in prose and short lists:
- the flow: the DM starts a session (`POST /api/tables/{id}/sessions`) → players `POST /api/sessions/{code}/join` with a name and get a `guestToken` → everyone connects to `/hubs/combat` (DM with the cookie, players with the token via `accessTokenFactory`) and calls `Join(code)`;
- commands take `expectedVersion`; on `Conflict` the client calls `GetSnapshot()`;
- the server pushes `DmSnapshot` / `PlayerSnapshot` / `LogAppended`; players never receive hidden combatants, exact monster HP or log entries about hidden combatants;
- limits: one replica (presence and groups are in memory); sessions expire after 12 h without activity; `/join` is rate limited per IP (`RateLimiting:JoinPermitsPerMinute`, default 10).

Add a section `## Google sign-in (optional)` explaining that it is off unless `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are set, e.g. locally with

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>" --project src/DmCompanionLive.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>" --project src/DmCompanionLive.Api
```

that the authorized redirect URI is `https://<host>/signin-google`, that the login starts at `GET /api/auth/google/login?returnUrl=/…`, and that only the Google subject and display name are stored.

- [ ] **Step 2: Spec §4**

In the hub bullet list of §4, replace the two "DM commands" / "Player commands" bullets with:

```markdown
- Commands (each with `expectedVersion`): `AddMonster`, `RollMonsterInitiative`, `Start`, `NextTurn`, `End`, `Reveal`, `Remove` (DM only); `ApplyDamage`, `Heal`, `SetTempHp`, `SetInitiative`, `SetConditions` (DM on anyone, a player on the character they claimed); `Claim` (players). The domain authorizes every command, so both roles share the same hub methods.
```

- [ ] **Step 3: Carry-over**

In `2026-11-24-plan-1-carryover.md`, delete the "Plan 2 (real-time combat)" section (every item is done; see the table below) and add, under a new heading `## Plan 2 — deferred`:

```markdown
## Plan 2 — deferred
- Clean up expired and ended sessions (and their log) with a periodic job; today they stay stored, readable and closed to commands.
- Presence and SignalR groups are in memory: a second replica needs a backplane (out of scope for the MVP).
- `RateLimiting` partitions by remote IP: needs `ForwardedHeaders` behind the ingress (Plan 5).
- Hub connections with the DM cookie rely on `SameSite=Lax` against cross-site use; revisit together with the antiforgery decision (Plan 4).
- Guest names are free text shown to the DM and other players: the SPA must render them as text (Plan 4).
```

- [ ] **Step 4: Full verification**

Run: `dotnet build DmCompanionLive.slnx -c Release`
Expected: build succeeds with no warnings (warnings are errors).

Run: `dotnet test DmCompanionLive.slnx`
Expected: PASS, no failures.

- [ ] **Step 5: Commit**

```bash
git add README.md docs
git commit -m "docs: document real-time combat and Google sign-in, update the carry-over

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Coverage

| Spec / carry-over item | Task |
|---|---|
| Guest join with code + name, `GuestToken` (Data Protection, 12 h), rate-limited `/join` | 5 |
| SignalR hub, identity resolved per connection, caller gets its full view | 6 |
| DM and player commands, `expectedVersion`, `Conflict`, typed error codes | 7 |
| Save state + log entry in one transaction, broadcast `DmView` / `PlayerView` | 3, 7 |
| Combat log persisted, filtered for players | 2, 3, 6, 7 |
| Reconnect: full snapshot on request (`GetSnapshot`) | 6 |
| Sessions expire after 12 h of inactivity | 2, 5, 8 |
| Google sign-in wired, inactive without credentials; only `sub` + display name stored | 8 |
| Carry-over: `AddJsonProtocol` with `JsonStringEnumConverter` | 6 |
| Carry-over: `SetInitiative` mid-combat turn skip | 1 |
| Carry-over: authorize before lookup in `Controllable` | 2 |
| Carry-over: validate `Condition` bits | 2, 7 |
| Carry-over: `DbUpdateConcurrencyException` → `Conflict`; join-code retry on 23505; assert 23505 | 3, 4, 7 |
| Carry-over: FKs for sessions and table deletion | 3 |
| Carry-over: add/remove combatants on a loaded session | 3 |
| Carry-over: `RespectNullableAnnotations` (HTTP and hub) | 4, 6 |
| Carry-over: `PlayerView.Version` side channel — accepted and documented | 2 |
| Carry-over: normalize `now` to UTC | 2 |
| Carry-over: `OnValidatePrincipal`, validate `externalId`, test unique `ExternalId` | 2, 3, 8 |
| Carry-over: snapshot after an idempotent re-claim | 7 |
| Carry-over: `JoinCode.Normalize(null)` | 2, 4, 5 |
| Carry-over: several sessions per table; unknown code → 404 | 4 |
| Carry-over: domain tests (all defeated, guest on DM commands, single-combatant wrap, remove non-current / in lobby, ended guard per command, negative temp HP, temp HP replaces) | 1, 2 |
| Carry-over: immutable event parameters | 2 |
| Carry-over: claim-type constants | 4 |
