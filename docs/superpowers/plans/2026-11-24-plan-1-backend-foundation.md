# Plan 1 — Backend foundation and combat domain

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 backend that compiles, persists DM users, tables and combat sessions in PostgreSQL, enforces every combat rule of the spec in a pure domain model, and exposes health, dev login, tables and session-creation endpoints — all covered by domain and integration tests.

**Architecture:** Hybrid layout from the spec: `Domain` (pure .NET, the `CombatSession` aggregate and its rules), `Infrastructure` (EF Core + Npgsql, migrations), `Api` (ASP.NET Core minimal APIs organised in `Features/` slices). Writes go through domain methods; reads query EF Core directly.

**Tech Stack:** .NET 10 (SDK 10.0.403 installed), ASP.NET Core minimal APIs, EF Core 10.0.15, Npgsql EF 10.0.4, PostgreSQL 17, xUnit 2.9.3, AwesomeAssertions, Testcontainers 4.17.0, Scalar 3.2.0.

**Spec:** `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md`

### Roadmap (one plan per subsystem; each one is written when the previous one is done)

1. **Backend foundation and combat domain** — this plan.
2. **Real-time combat** — guest join + `GuestToken`, rate limiting, SignalR hub and commands, views broadcast, optimistic concurrency, `CombatLog` persistence (filtered for players), session expiry, Google sign-in wiring (inactive without credentials).
3. **Monsters and SRD** — `tools/SrdImport` (incl. resolving the English SRD 5.2.1 source risk), seeding, `pg_trgm`/`unaccent` search, favourites, templates, adding SRD/template monsters to a combat, `NOTICE.md`.
4. **Frontend** — `web/` SPA (all screens, i18n, PWA), served by the API, OpenAPI client generation + contract test, Vitest, antiforgery decision.
5. **Delivery** — Dockerfile, full docker-compose, Playwright E2E, Bicep, release workflow, Dependabot, `docs/deploy.md`, final README.

## Global Constraints

- Target framework `net10.0`; `global.json` pins SDK `10.0.100` with `rollForward: latestFeature`.
- `Nullable` enabled, `ImplicitUsings` enabled, `TreatWarningsAsErrors` true in every project.
- Package versions exactly as listed in each task (latest 10.0.x patch lines as of 2026-11-24). Do not move to .NET 11 packages.
- Domain project has **no** package or project references.
- Code, identifiers, commit messages and docs in English. Conventional commits; every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Repo: `dm-companion-live` (git already initialised). Work on a branch `feature/plan-1-backend-foundation`; never commit to `main` directly, never push.
- Integration tests need Docker.
- No cloud accounts or external services are used.

---

### Task 1: Solution scaffold

**Files:**
- Create: `global.json`, `Directory.Build.props`, `.gitignore`, `DmCompanionLive.slnx`
- Create: `src/DmCompanionLive.Domain/DmCompanionLive.Domain.csproj`
- Create: `src/DmCompanionLive.Infrastructure/DmCompanionLive.Infrastructure.csproj`
- Create: `src/DmCompanionLive.Api/DmCompanionLive.Api.csproj`, `src/DmCompanionLive.Api/Program.cs`
- Create: `tests/DmCompanionLive.Domain.Tests/DmCompanionLive.Domain.Tests.csproj`, `tests/DmCompanionLive.Domain.Tests/SmokeTests.cs`
- Create: `tests/DmCompanionLive.Api.IntegrationTests/DmCompanionLive.Api.IntegrationTests.csproj`

**Interfaces:**
- Produces: the five projects and the solution every later task builds on.

- [ ] **Step 1: Create the branch and the root files**

```bash
cd dm-companion-live
git switch -c feature/plan-1-backend-foundation
dotnet new gitignore
```

`global.json`:
```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the project files**

`src/DmCompanionLive.Domain/DmCompanionLive.Domain.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

`src/DmCompanionLive.Infrastructure/DmCompanionLive.Infrastructure.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.15">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DmCompanionLive.Domain\DmCompanionLive.Domain.csproj" />
  </ItemGroup>
</Project>
```

`src/DmCompanionLive.Api/DmCompanionLive.Api.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.15" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.15" />
    <PackageReference Include="Scalar.AspNetCore" Version="3.2.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DmCompanionLive.Infrastructure\DmCompanionLive.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`src/DmCompanionLive.Api/Program.cs` (temporary, replaced in Task 8):
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "dm-companion-live");
app.Run();

public partial class Program;
```

`tests/DmCompanionLive.Domain.Tests/DmCompanionLive.Domain.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
    <PackageReference Include="AwesomeAssertions" Version="9.6.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="AwesomeAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DmCompanionLive.Domain\DmCompanionLive.Domain.csproj" />
  </ItemGroup>
</Project>
```

`tests/DmCompanionLive.Api.IntegrationTests/DmCompanionLive.Api.IntegrationTests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.0.2" />
    <PackageReference Include="AwesomeAssertions" Version="9.6.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.15" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.17.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="AwesomeAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DmCompanionLive.Api\DmCompanionLive.Api.csproj" />
  </ItemGroup>
</Project>
```

`tests/DmCompanionLive.Domain.Tests/SmokeTests.cs` (deleted in Task 2):
```csharp
namespace DmCompanionLive.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_runner_works() => true.Should().BeTrue();
}
```

- [ ] **Step 3: Create the solution and add the projects**

```bash
dotnet new sln --format slnx -n DmCompanionLive
dotnet sln DmCompanionLive.slnx add src/DmCompanionLive.Domain src/DmCompanionLive.Infrastructure src/DmCompanionLive.Api tests/DmCompanionLive.Domain.Tests tests/DmCompanionLive.Api.IntegrationTests
```

- [ ] **Step 4: Build and run the smoke test**

Run: `dotnet test DmCompanionLive.slnx`
Expected: build succeeds with 0 warnings; 1 test passed (the integration project has no tests yet).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: scaffold the .NET 10 solution

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Combat domain — hit points and session creation

**Files:**
- Create: `src/DmCompanionLive.Domain/DomainException.cs`
- Create: `src/DmCompanionLive.Domain/Combat/Actor.cs`, `CombatantKind.cs`, `SessionStatus.cs`, `Condition.cs`, `Seeds.cs`, `CombatEvent.cs`, `Combatant.cs`, `CombatSession.cs`
- Create: `tests/DmCompanionLive.Domain.Tests/Fixtures.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/HitPointsTests.cs`
- Delete: `tests/DmCompanionLive.Domain.Tests/SmokeTests.cs`

**Interfaces:**
- Produces:
  - `DomainException(DomainErrorCode code, string message)` with `Code`; `enum DomainErrorCode { NotAllowed, NotYourCharacter, SessionEnded, SessionNotActive, InvalidState, CombatantNotFound, AlreadyClaimed, AlreadyHasCharacter, InvalidValue, NoCombatants }`
  - `abstract record Actor` with `Actor.Dm(Guid UserId)` and `Actor.Guest(Guid GuestId, string Name)`
  - `record PcSeed(string Name, int MaxHp, int Ac, int DexModifier = 0)`, `record MonsterSeed(string Name, int MaxHp, int Ac, int DexModifier = 0, bool IsHidden = false, string? SourceRef = null)`
  - `record CombatEvent(int Version, DateTimeOffset At, string ActorName, string Type, IReadOnlyDictionary<string,string> Parameters)`
  - `CombatSession.Create(Guid tableId, Guid ownerId, string joinCode, IEnumerable<PcSeed> characters, DateTimeOffset now)`
  - `CombatSession.AddMonster(Actor, MonsterSeed, DateTimeOffset) : Guid`, `ApplyDamage(Actor, Guid, int, DateTimeOffset)`, `Heal(...)`, `SetTempHp(...)`, `ClearUncommittedEvents()`
  - Properties: `Id, TableId, OwnerId, JoinCode, Status, Round, CurrentTurnId, Version, LastActivityAt, Combatants, UncommittedEvents`
  - `Combatant` properties: `Id, Kind, Name, Initiative (int?), DexModifier, MaxHp, CurrentHp, TempHp, Ac, Conditions, IsHidden, SourceRef, ClaimedBy (Guid?), SortOrder, IsDefeated`

- [ ] **Step 1: Write the failing tests**

Delete `tests/DmCompanionLive.Domain.Tests/SmokeTests.cs`.

`tests/DmCompanionLive.Domain.Tests/Fixtures.cs`:
```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

internal static class Fixtures
{
    public static readonly Guid DmId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Actor Dm = new Actor.Dm(DmId);
    public static readonly Actor OtherDm = new Actor.Dm(Guid.Parse("22222222-2222-2222-2222-222222222222"));
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 18, 0, 0, TimeSpan.Zero);

    public static Actor.Guest Guest(string name = "Lyra") => new(Guid.NewGuid(), name);

    public static CombatSession NewSession(params PcSeed[] characters) =>
        CombatSession.Create(Guid.NewGuid(), DmId, "ABC234", characters, Now);
}
```

`tests/DmCompanionLive.Domain.Tests/Combat/HitPointsTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class HitPointsTests
{
    [Fact]
    public void New_session_starts_in_lobby_with_full_hp_characters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));

        session.Status.Should().Be(SessionStatus.Lobby);
        session.Version.Should().Be(0);
        session.Combatants.Should().HaveCount(2);
        session.Combatants.Should().OnlyContain(c => c.Kind == CombatantKind.Pc && c.CurrentHp == c.MaxHp);
    }

    [Fact]
    public void Damage_consumes_temporary_hp_first()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();

        session.SetTempHp(Dm, lyra.Id, 5, Now);
        session.ApplyDamage(Dm, lyra.Id, 8, Now);

        lyra.TempHp.Should().Be(0);
        lyra.CurrentHp.Should().Be(17);
    }

    [Fact]
    public void Hp_never_drops_below_zero_and_a_pc_at_zero_is_unconscious()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();

        session.ApplyDamage(Dm, lyra.Id, 50, Now);

        lyra.CurrentHp.Should().Be(0);
        lyra.Conditions.Should().HaveFlag(Condition.Unconscious);
        lyra.IsDefeated.Should().BeFalse("a PC at 0 HP still takes turns");
    }

    [Fact]
    public void Healing_is_capped_at_max_and_wakes_an_unconscious_pc()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        session.ApplyDamage(Dm, lyra.Id, 25, Now);

        session.Heal(Dm, lyra.Id, 100, Now);

        lyra.CurrentHp.Should().Be(20);
        lyra.Conditions.Should().NotHaveFlag(Condition.Unconscious);
    }

    [Fact]
    public void A_monster_at_zero_hp_is_defeated()
    {
        var session = NewSession();
        var goblinId = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);

        session.ApplyDamage(Dm, goblinId, 7, Now);

        session.Combatants.Single(c => c.Id == goblinId).IsDefeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Damage_and_healing_must_be_positive(int amount)
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var id = session.Combatants.Single().Id;

        var damage = () => session.ApplyDamage(Dm, id, amount, Now);
        var heal = () => session.Heal(Dm, id, amount, Now);

        damage.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        heal.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Invalid_seeds_are_rejected()
    {
        var noHp = () => NewSession(new PcSeed("Lyra", 0, 15));
        var noName = () => NewSession(new PcSeed("  ", 10, 15));

        noHp.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        noName.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Every_change_increments_the_version_and_records_an_event()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var id = session.Combatants.Single().Id;

        session.ApplyDamage(Dm, id, 3, Now);
        session.Heal(Dm, id, 1, Now);

        session.Version.Should().Be(2);
        session.UncommittedEvents.Select(e => e.Type).Should().Equal("DamageApplied", "Healed");
        session.UncommittedEvents[0].Parameters.Should().Contain("amount", "3").And.Contain("name", "Lyra");
        session.UncommittedEvents[0].ActorName.Should().Be("DM");

        session.ClearUncommittedEvents();
        session.UncommittedEvents.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `CombatSession`, `PcSeed`, `DomainException`… do not exist.

- [ ] **Step 3: Implement the domain types**

`src/DmCompanionLive.Domain/DomainException.cs`:
```csharp
namespace DmCompanionLive.Domain;

public enum DomainErrorCode
{
    NotAllowed,
    NotYourCharacter,
    SessionEnded,
    SessionNotActive,
    InvalidState,
    CombatantNotFound,
    AlreadyClaimed,
    AlreadyHasCharacter,
    InvalidValue,
    NoCombatants,
}

public sealed class DomainException(DomainErrorCode code, string message) : Exception(message)
{
    public DomainErrorCode Code { get; } = code;
}
```

`src/DmCompanionLive.Domain/Combat/Actor.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

/// <summary>Who issues a command: the DM who owns the session or a guest player.</summary>
public abstract record Actor
{
    private Actor()
    {
    }

    public sealed record Dm(Guid UserId) : Actor;

    public sealed record Guest(Guid GuestId, string Name) : Actor;
}
```

`src/DmCompanionLive.Domain/Combat/CombatantKind.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public enum CombatantKind
{
    Pc,
    Monster,
}
```

`src/DmCompanionLive.Domain/Combat/SessionStatus.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public enum SessionStatus
{
    Lobby,
    Active,
    Ended,
}
```

`src/DmCompanionLive.Domain/Combat/Condition.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

/// <summary>The 15 conditions of the SRD 5.2.1.</summary>
[Flags]
public enum Condition
{
    None = 0,
    Blinded = 1 << 0,
    Charmed = 1 << 1,
    Deafened = 1 << 2,
    Exhaustion = 1 << 3,
    Frightened = 1 << 4,
    Grappled = 1 << 5,
    Incapacitated = 1 << 6,
    Invisible = 1 << 7,
    Paralyzed = 1 << 8,
    Petrified = 1 << 9,
    Poisoned = 1 << 10,
    Prone = 1 << 11,
    Restrained = 1 << 12,
    Stunned = 1 << 13,
    Unconscious = 1 << 14,
}
```

`src/DmCompanionLive.Domain/Combat/Seeds.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public sealed record PcSeed(string Name, int MaxHp, int Ac, int DexModifier = 0);

public sealed record MonsterSeed(
    string Name,
    int MaxHp,
    int Ac,
    int DexModifier = 0,
    bool IsHidden = false,
    string? SourceRef = null);
```

`src/DmCompanionLive.Domain/Combat/CombatEvent.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

/// <summary>
/// One entry of the combat log. Stores a type and parameters, never rendered text,
/// so each client can translate it into its own language.
/// </summary>
public sealed record CombatEvent(
    int Version,
    DateTimeOffset At,
    string ActorName,
    string Type,
    IReadOnlyDictionary<string, string> Parameters);
```

`src/DmCompanionLive.Domain/Combat/Combatant.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public sealed class Combatant
{
    public const int MaxNameLength = 80;

    private Combatant()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public CombatantKind Kind { get; private set; }
    public string Name { get; private set; }
    public int? Initiative { get; private set; }
    public int DexModifier { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int TempHp { get; private set; }
    public int Ac { get; private set; }
    public Condition Conditions { get; private set; }
    public bool IsHidden { get; private set; }
    public string? SourceRef { get; private set; }
    public Guid? ClaimedBy { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>Only monsters are defeated at 0 HP; PCs fall unconscious and keep their turn.</summary>
    public bool IsDefeated => Kind == CombatantKind.Monster && CurrentHp == 0;

    internal static Combatant CreatePc(PcSeed seed, int sortOrder)
    {
        Validate(seed.Name, seed.MaxHp, seed.Ac);
        return new Combatant
        {
            Id = Guid.NewGuid(),
            Kind = CombatantKind.Pc,
            Name = seed.Name.Trim(),
            MaxHp = seed.MaxHp,
            CurrentHp = seed.MaxHp,
            Ac = seed.Ac,
            DexModifier = seed.DexModifier,
            SortOrder = sortOrder,
        };
    }

    internal static Combatant CreateMonster(MonsterSeed seed, int sortOrder)
    {
        Validate(seed.Name, seed.MaxHp, seed.Ac);
        return new Combatant
        {
            Id = Guid.NewGuid(),
            Kind = CombatantKind.Monster,
            Name = seed.Name.Trim(),
            MaxHp = seed.MaxHp,
            CurrentHp = seed.MaxHp,
            Ac = seed.Ac,
            DexModifier = seed.DexModifier,
            IsHidden = seed.IsHidden,
            SourceRef = seed.SourceRef,
            SortOrder = sortOrder,
        };
    }

    internal void TakeDamage(int amount)
    {
        var absorbed = Math.Min(TempHp, amount);
        TempHp -= absorbed;
        CurrentHp = Math.Max(0, CurrentHp - (amount - absorbed));
        if (Kind == CombatantKind.Pc && CurrentHp == 0)
        {
            Conditions |= Condition.Unconscious;
        }
    }

    internal void Heal(int amount)
    {
        if (Kind == CombatantKind.Pc && CurrentHp == 0)
        {
            Conditions &= ~Condition.Unconscious;
        }

        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }

    internal void SetTempHp(int amount) => TempHp = amount;

    internal void SetInitiative(int value) => Initiative = value;

    internal void SetConditions(Condition conditions) => Conditions = conditions;

    internal void Reveal() => IsHidden = false;

    internal void ClaimBy(Guid guestId) => ClaimedBy = guestId;

    private static void Validate(string name, int maxHp, int ac)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxNameLength} characters.");
        }

        if (maxHp < 1)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Max HP must be at least 1.");
        }

        if (ac < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "AC cannot be negative.");
        }
    }
}
```

`src/DmCompanionLive.Domain/Combat/CombatSession.cs`:
```csharp
using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

/// <summary>Aggregate root of a live combat. Every rule of the combat lives here.</summary>
public sealed class CombatSession
{
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
            LastActivityAt = now,
        };

        foreach (var character in characters)
        {
            session._combatants.Add(Combatant.CreatePc(character, session.NextSortOrder()));
        }

        return session;
    }

    public Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now)
    {
        EnsureNotEnded();
        EnsureDm(actor);
        var monster = Combatant.CreateMonster(seed, NextSortOrder());
        _combatants.Add(monster);
        Record(actor, now, "MonsterAdded", monster);
        return monster.Id;
    }

    public void ApplyDamage(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        EnsurePositive(amount);
        target.TakeDamage(amount);
        Record(actor, now, "DamageApplied", target, ("amount", amount));
    }

    public void Heal(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        EnsurePositive(amount);
        target.Heal(amount);
        Record(actor, now, "Healed", target, ("amount", amount));
    }

    public void SetTempHp(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        if (amount < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Temporary HP cannot be negative.");
        }

        target.SetTempHp(amount);
        Record(actor, now, "TempHpSet", target, ("amount", amount));
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    private Combatant Controllable(Actor actor, Guid combatantId)
    {
        EnsureNotEnded();
        var combatant = Find(combatantId);
        return actor switch
        {
            Actor.Dm dm when dm.UserId == OwnerId => combatant,
            Actor.Guest guest when combatant.ClaimedBy == guest.GuestId => combatant,
            Actor.Guest => throw new DomainException(
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

    private void EnsureNotEnded()
    {
        if (Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }
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
        Version++;
        LastActivityAt = now;

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

        _uncommittedEvents.Add(new CombatEvent(Version, now, ActorName(actor), type, parameters));
    }

    private static string ActorName(Actor actor) => actor is Actor.Guest guest ? guest.Name : "DM";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, 9 tests (the theory counts twice), 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add combat session hit point rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Combat domain — initiative, turns and rounds

**Files:**
- Create: `src/DmCompanionLive.Domain/Combat/IDiceRoller.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/CombatSession.cs` (add members listed below)
- Create: `tests/DmCompanionLive.Domain.Tests/FixedDice.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/TurnOrderTests.cs`

**Interfaces:**
- Consumes: everything from Task 2.
- Produces:
  - `interface IDiceRoller { int D20(); }`
  - `CombatSession.TurnOrder : IReadOnlyList<Combatant>` (initiative desc, null last; then `DexModifier` desc; then `SortOrder` asc)
  - `SetInitiative(Actor, Guid combatantId, int value, DateTimeOffset)`, `RollMonsterInitiative(Actor, IDiceRoller, DateTimeOffset)`, `Start(Actor, DateTimeOffset)`, `NextTurn(Actor, DateTimeOffset)`
  - Private `TryAdvance(Guid? excluded) : bool` reused by Task 4's `Remove`.

Note: the spec lists `TurnIndex`; this plan stores `CurrentTurnId` instead, so the current turn survives initiative edits and removals. Clients receive `CurrentTurnId` in the views.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/FixedDice.cs`:
```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

internal sealed class FixedDice(params int[] rolls) : IDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    public int D20() => _rolls.Dequeue();
}
```

`tests/DmCompanionLive.Domain.Tests/Combat/TurnOrderTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class TurnOrderTests
{
    [Fact]
    public void Turn_order_is_initiative_desc_then_dex_desc_then_insertion_with_unset_last()
    {
        var session = NewSession(
            new PcSeed("Lyra", 20, 15, DexModifier: 2),
            new PcSeed("Torvald", 30, 18, DexModifier: 0),
            new PcSeed("Varek", 25, 16, DexModifier: 2),
            new PcSeed("Nim", 18, 14, DexModifier: 4));
        var (lyra, torvald, varek, _) = Ids(session);

        session.SetInitiative(Dm, lyra, 15, Now);
        session.SetInitiative(Dm, torvald, 15, Now);
        session.SetInitiative(Dm, varek, 15, Now);

        session.TurnOrder.Select(c => c.Name).Should().Equal("Lyra", "Varek", "Torvald", "Nim");
    }

    [Fact]
    public void Rolling_monster_initiative_only_rolls_monsters_without_one()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15, DexModifier: 2), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13, DexModifier: 1), Now);
        session.SetInitiative(Dm, orc, 9, Now);

        session.RollMonsterInitiative(Dm, new FixedDice(11), Now);

        Find(session, goblin).Initiative.Should().Be(13);
        Find(session, orc).Initiative.Should().Be(9);
        session.Combatants.Single(c => c.Kind == CombatantKind.Pc).Initiative.Should().BeNull();
    }

    [Fact]
    public void Start_begins_round_one_with_the_highest_initiative()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var (lyra, torvald, _, _) = Ids(session);
        session.SetInitiative(Dm, lyra, 8, Now);
        session.SetInitiative(Dm, torvald, 17, Now);

        session.Start(Dm, Now);

        session.Status.Should().Be(SessionStatus.Active);
        session.Round.Should().Be(1);
        session.CurrentTurnId.Should().Be(torvald);
    }

    [Fact]
    public void Next_turn_wraps_to_a_new_round()
    {
        var session = StartedSessionWithTwoPcs(out var first, out var second);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(second);
        session.Round.Should().Be(1);

        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(first);
        session.Round.Should().Be(2);
    }

    [Fact]
    public void Next_turn_skips_defeated_monsters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);
        var orc = session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13), Now);
        session.SetInitiative(Dm, lyra, 20, Now);
        session.SetInitiative(Dm, goblin, 15, Now);
        session.SetInitiative(Dm, orc, 10, Now);
        session.Start(Dm, Now);
        session.ApplyDamage(Dm, goblin, 7, Now);

        session.NextTurn(Dm, Now);

        session.CurrentTurnId.Should().Be(orc);
    }

    [Fact]
    public void Next_turn_requires_an_active_session()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.NextTurn(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionNotActive);
    }

    [Fact]
    public void Start_requires_someone_able_to_act()
    {
        var session = NewSession();

        var act = () => session.Start(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NoCombatants);
        session.Status.Should().Be(SessionStatus.Lobby);
    }

    [Fact]
    public void Start_twice_is_an_invalid_state()
    {
        var session = StartedSessionWithTwoPcs(out _, out _);

        var act = () => session.Start(Dm, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidState);
    }

    private static CombatSession StartedSessionWithTwoPcs(out Guid first, out Guid second)
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        (first, second, _, _) = Ids(session);
        session.SetInitiative(Dm, first, 18, Now);
        session.SetInitiative(Dm, second, 12, Now);
        session.Start(Dm, Now);
        return session;
    }

    private static (Guid, Guid, Guid, Guid) Ids(CombatSession session)
    {
        var ids = session.Combatants.Select(c => c.Id).Concat(Enumerable.Repeat(Guid.Empty, 4)).ToArray();
        return (ids[0], ids[1], ids[2], ids[3]);
    }

    private static Combatant Find(CombatSession session, Guid id) => session.Combatants.Single(c => c.Id == id);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `IDiceRoller`, `SetInitiative`, `TurnOrder`, `Start`, `NextTurn`, `RollMonsterInitiative` missing.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Domain/Combat/IDiceRoller.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public interface IDiceRoller
{
    int D20();
}
```

Add to `CombatSession` (after `Combatants`/`UncommittedEvents` properties):
```csharp
    public IReadOnlyList<Combatant> TurnOrder => _combatants
        .OrderByDescending(c => c.Initiative ?? int.MinValue)
        .ThenByDescending(c => c.DexModifier)
        .ThenBy(c => c.SortOrder)
        .ToList();
```

Add to `CombatSession` (after `SetTempHp`):
```csharp
    public void SetInitiative(Actor actor, Guid combatantId, int value, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        target.SetInitiative(value);
        Record(actor, now, "InitiativeSet", target, ("value", value));
    }

    public void RollMonsterInitiative(Actor actor, IDiceRoller dice, DateTimeOffset now)
    {
        EnsureNotEnded();
        EnsureDm(actor);
        foreach (var monster in _combatants.Where(c => c.Kind == CombatantKind.Monster && c.Initiative is null))
        {
            monster.SetInitiative(dice.D20() + monster.DexModifier);
        }

        Record(actor, now, "MonsterInitiativeRolled");
    }

    public void Start(Actor actor, DateTimeOffset now)
    {
        EnsureNotEnded();
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
        EnsureActive();
        EnsureDm(actor);
        if (!TryAdvance(excluded: null))
        {
            throw new DomainException(DomainErrorCode.NoCombatants, "Nobody can act in this combat.");
        }

        Record(actor, now, "TurnAdvanced", Current(), ("round", Round));
    }
```

Add these private helpers to `CombatSession` (next to `EnsureNotEnded`):
```csharp
    private Combatant? Current() => _combatants.SingleOrDefault(c => c.Id == CurrentTurnId);

    /// <summary>
    /// Moves the turn to the next combatant that can act, skipping defeated ones and
    /// <paramref name="excluded"/>. Increments the round when the order wraps.
    /// </summary>
    private bool TryAdvance(Guid? excluded)
    {
        var order = TurnOrder;
        var index = order.ToList().FindIndex(c => c.Id == CurrentTurnId);
        var wrapped = false;
        for (var step = 1; step <= order.Count; step++)
        {
            var raw = index + step;
            if (raw >= order.Count)
            {
                wrapped = true;
            }

            var candidate = order[raw % order.Count];
            if (candidate.IsDefeated || candidate.Id == excluded)
            {
                continue;
            }

            if (wrapped)
            {
                Round++;
            }

            CurrentTurnId = candidate.Id;
            return true;
        }

        return false;
    }

    private void EnsureActive()
    {
        EnsureNotEnded();
        if (Status != SessionStatus.Active)
        {
            throw new DomainException(DomainErrorCode.SessionNotActive, "The combat has not started.");
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, 17 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add initiative, turns and rounds to the combat session

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Combat domain — permissions, claiming and the remaining commands

**Files:**
- Modify: `src/DmCompanionLive.Domain/Combat/CombatSession.cs`
- Create: `tests/DmCompanionLive.Domain.Tests/Combat/PermissionsTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/CombatCommandsTests.cs`

**Interfaces:**
- Consumes: Tasks 2–3 (`Controllable`, `EnsureDm`, `TryAdvance`, `Record`).
- Produces: `Claim(Actor, Guid, DateTimeOffset)`, `SetConditions(Actor, Guid, Condition, DateTimeOffset)`, `Reveal(Actor, Guid, DateTimeOffset)`, `Remove(Actor, Guid, DateTimeOffset)`, `End(Actor, DateTimeOffset)`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/Combat/PermissionsTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class PermissionsTests
{
    [Fact]
    public void Another_dm_cannot_command_the_session()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.AddMonster(OtherDm, new MonsterSeed("Goblin", 7, 15), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void A_guest_cannot_touch_a_character_they_have_not_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;

        var act = () => session.ApplyDamage(Guest(), lyra, 3, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotYourCharacter);
    }

    [Fact]
    public void A_guest_controls_the_character_they_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single();
        var player = Guest("Ana");

        session.Claim(player, lyra.Id, Now);
        session.ApplyDamage(player, lyra.Id, 5, Now);
        session.Heal(player, lyra.Id, 2, Now);
        session.SetInitiative(player, lyra.Id, 14, Now);
        session.SetConditions(player, lyra.Id, Condition.Prone | Condition.Poisoned, Now);

        lyra.ClaimedBy.Should().Be(player.GuestId);
        lyra.CurrentHp.Should().Be(17);
        lyra.Initiative.Should().Be(14);
        lyra.Conditions.Should().Be(Condition.Prone | Condition.Poisoned);
        session.UncommittedEvents.First().ActorName.Should().Be("Ana");
    }

    [Fact]
    public void Monsters_cannot_be_claimed()
    {
        var session = NewSession();
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 7, 15), Now);

        var act = () => session.Claim(Guest(), goblin, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void A_character_claimed_by_someone_else_cannot_be_claimed()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Claim(Guest("Ana"), lyra, Now);

        var act = () => session.Claim(Guest("Bea"), lyra, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.AlreadyClaimed);
    }

    [Fact]
    public void A_guest_can_only_hold_one_character_but_reclaiming_their_own_is_fine()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var lyra = session.Combatants[0].Id;
        var torvald = session.Combatants[1].Id;
        var player = Guest("Ana");
        session.Claim(player, lyra, Now);

        var reclaim = () => session.Claim(player, lyra, Now);
        var second = () => session.Claim(player, torvald, Now);

        reclaim.Should().NotThrow();
        second.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.AlreadyHasCharacter);
    }

    [Fact]
    public void The_dm_does_not_claim_characters()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.Claim(Dm, session.Combatants.Single().Id, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }

    [Fact]
    public void Guests_cannot_run_dm_commands()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));

        var act = () => session.Start(Guest(), Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }
}
```

`tests/DmCompanionLive.Domain.Tests/Combat/CombatCommandsTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class CombatCommandsTests
{
    [Fact]
    public void Reveal_makes_a_hidden_monster_visible()
    {
        var session = NewSession();
        var id = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);

        session.Reveal(Dm, id, Now);

        session.Combatants.Single().IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Removing_the_current_combatant_passes_the_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18));
        var lyra = session.Combatants[0].Id;
        var torvald = session.Combatants[1].Id;
        session.SetInitiative(Dm, lyra, 18, Now);
        session.SetInitiative(Dm, torvald, 10, Now);
        session.Start(Dm, Now);

        session.Remove(Dm, lyra, Now);

        session.Combatants.Should().ContainSingle(c => c.Id == torvald);
        session.CurrentTurnId.Should().Be(torvald);
    }

    [Fact]
    public void Removing_the_last_combatant_clears_the_turn()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Start(Dm, Now);

        session.Remove(Dm, lyra, Now);

        session.CurrentTurnId.Should().BeNull();
    }

    [Fact]
    public void Nothing_changes_after_the_combat_ends()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        session.Start(Dm, Now);

        session.End(Dm, Now);
        var act = () => session.ApplyDamage(Dm, lyra, 1, Now);

        session.Status.Should().Be(SessionStatus.Ended);
        session.CurrentTurnId.Should().BeNull();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.SessionEnded);
    }

    [Fact]
    public void Only_the_dm_reveals_removes_and_ends()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var player = Guest();
        session.Claim(player, lyra, Now);

        var reveal = () => session.Reveal(player, lyra, Now);
        var remove = () => session.Remove(player, lyra, Now);
        var end = () => session.End(player, Now);

        reveal.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
        remove.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
        end.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `Claim`, `SetConditions`, `Reveal`, `Remove`, `End` missing.

- [ ] **Step 3: Implement** — add to `CombatSession` after `NextTurn`:

```csharp
    public void Claim(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureNotEnded();
        if (actor is not Actor.Guest guest)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only players claim characters.");
        }

        var character = Find(combatantId);
        if (character.Kind != CombatantKind.Pc)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only player characters can be claimed.");
        }

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
        var target = Controllable(actor, combatantId);
        target.SetConditions(conditions);
        Record(actor, now, "ConditionsSet", target, ("conditions", conditions));
    }

    public void Reveal(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureNotEnded();
        EnsureDm(actor);
        var target = Find(combatantId);
        target.Reveal();
        Record(actor, now, "Revealed", target);
    }

    public void Remove(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureNotEnded();
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
        EnsureNotEnded();
        EnsureDm(actor);
        Status = SessionStatus.Ended;
        CurrentTurnId = null;
        Record(actor, now, "CombatEnded");
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, 30 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add claiming, permissions and the remaining combat commands

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Combat domain — DM/player views and join codes

**Files:**
- Create: `src/DmCompanionLive.Domain/Combat/Views.cs`, `src/DmCompanionLive.Domain/Combat/JoinCode.cs`
- Create: `tests/DmCompanionLive.Domain.Tests/Combat/ViewsTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/JoinCodeTests.cs`

**Interfaces:**
- Consumes: `CombatSession`, `Combatant` (Tasks 2–4).
- Produces:
  - `enum HpBand { Unhurt, Wounded, Bloodied, Defeated }`
  - `record DmCombatantView(Guid Id, CombatantKind Kind, string Name, int? Initiative, int MaxHp, int CurrentHp, int TempHp, int Ac, Condition Conditions, bool IsHidden, bool IsDefeated, Guid? ClaimedBy, string? SourceRef)`
  - `record DmView(Guid SessionId, string JoinCode, SessionStatus Status, int Round, Guid? CurrentTurnId, int Version, IReadOnlyList<DmCombatantView> Combatants)`
  - `record PlayerCombatantView(Guid Id, CombatantKind Kind, string Name, int? Initiative, int? MaxHp, int? CurrentHp, int? TempHp, HpBand? Band, Condition Conditions, bool IsDefeated, bool IsClaimed, bool IsMine)`
  - `record PlayerView(Guid SessionId, SessionStatus Status, int Round, Guid? CurrentTurnId, int Version, IReadOnlyList<PlayerCombatantView> Combatants)`
  - `static class CombatViews { DmView ForDm(CombatSession); PlayerView ForPlayer(CombatSession, Guid? guestId); HpBand BandFor(int currentHp, int maxHp); }`
  - `static class JoinCode { const string Alphabet; const int Length = 6; string New(); string Normalize(string); }`

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/Combat/ViewsTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class ViewsTests
{
    [Theory]
    [InlineData(10, 10, HpBand.Unhurt)]
    [InlineData(6, 10, HpBand.Wounded)]
    [InlineData(5, 10, HpBand.Bloodied)]
    [InlineData(1, 10, HpBand.Bloodied)]
    [InlineData(0, 10, HpBand.Defeated)]
    public void Hp_bands_follow_the_spec_thresholds(int current, int max, HpBand expected) =>
        CombatViews.BandFor(current, max).Should().Be(expected);

    [Fact]
    public void Players_never_see_hidden_combatants_or_exact_monster_hp()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var goblin = session.AddMonster(Dm, new MonsterSeed("Goblin", 10, 15), Now);
        session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.ApplyDamage(Dm, goblin, 6, Now);
        session.ApplyDamage(Dm, lyra, 4, Now);
        var player = Guest();
        session.Claim(player, lyra, Now);

        var view = CombatViews.ForPlayer(session, player.GuestId);

        view.Combatants.Select(c => c.Name).Should().BeEquivalentTo("Lyra", "Goblin");
        var goblinView = view.Combatants.Single(c => c.Name == "Goblin");
        goblinView.CurrentHp.Should().BeNull();
        goblinView.MaxHp.Should().BeNull();
        goblinView.TempHp.Should().BeNull();
        goblinView.Band.Should().Be(HpBand.Bloodied);
        var lyraView = view.Combatants.Single(c => c.Name == "Lyra");
        lyraView.CurrentHp.Should().Be(16);
        lyraView.Band.Should().BeNull();
        lyraView.IsMine.Should().BeTrue();
        lyraView.IsClaimed.Should().BeTrue();
    }

    [Fact]
    public void The_current_turn_of_a_hidden_combatant_is_not_revealed_to_players()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 15, IsHidden: true), Now);
        session.SetInitiative(Dm, assassin, 25, Now);
        session.SetInitiative(Dm, lyra, 5, Now);
        session.Start(Dm, Now);

        CombatViews.ForPlayer(session, guestId: null).CurrentTurnId.Should().BeNull();
        CombatViews.ForDm(session).CurrentTurnId.Should().Be(assassin);
    }

    [Fact]
    public void The_dm_view_has_everything_in_turn_order()
    {
        var session = NewSession(new PcSeed("Lyra", 20, 15));
        var lyra = session.Combatants.Single().Id;
        var assassin = session.AddMonster(Dm, new MonsterSeed("Assassin", 30, 16, IsHidden: true), Now);
        session.SetInitiative(Dm, assassin, 25, Now);
        session.SetInitiative(Dm, lyra, 5, Now);

        var view = CombatViews.ForDm(session);

        view.JoinCode.Should().Be("ABC234");
        view.Version.Should().Be(session.Version);
        view.Combatants.Select(c => c.Name).Should().Equal("Assassin", "Lyra");
        view.Combatants[0].Should().BeEquivalentTo(new { CurrentHp = 30, MaxHp = 30, Ac = 16, IsHidden = true });
    }
}
```

`tests/DmCompanionLive.Domain.Tests/Combat/JoinCodeTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class JoinCodeTests
{
    [Fact]
    public void Codes_have_six_unambiguous_characters()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => JoinCode.New()).ToList();

        codes.Should().OnlyContain(c => c.Length == JoinCode.Length && c.All(JoinCode.Alphabet.Contains));
        string.Concat(codes).Should().NotContainAny("0", "O", "1", "I");
        codes.Distinct().Should().HaveCountGreaterThan(190);
    }

    [Fact]
    public void Alphabet_gives_over_a_billion_combinations() =>
        Math.Pow(JoinCode.Alphabet.Length, JoinCode.Length).Should().BeGreaterThan(1_000_000_000);

    [Fact]
    public void Normalize_trims_and_uppercases() =>
        JoinCode.Normalize("  abc234 ").Should().Be("ABC234");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `CombatViews`, `HpBand`, `JoinCode` missing.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Domain/Combat/Views.cs`:
```csharp
namespace DmCompanionLive.Domain.Combat;

public enum HpBand
{
    Unhurt,
    Wounded,
    Bloodied,
    Defeated,
}

public sealed record DmCombatantView(
    Guid Id,
    CombatantKind Kind,
    string Name,
    int? Initiative,
    int MaxHp,
    int CurrentHp,
    int TempHp,
    int Ac,
    Condition Conditions,
    bool IsHidden,
    bool IsDefeated,
    Guid? ClaimedBy,
    string? SourceRef);

public sealed record DmView(
    Guid SessionId,
    string JoinCode,
    SessionStatus Status,
    int Round,
    Guid? CurrentTurnId,
    int Version,
    IReadOnlyList<DmCombatantView> Combatants);

public sealed record PlayerCombatantView(
    Guid Id,
    CombatantKind Kind,
    string Name,
    int? Initiative,
    int? MaxHp,
    int? CurrentHp,
    int? TempHp,
    HpBand? Band,
    Condition Conditions,
    bool IsDefeated,
    bool IsClaimed,
    bool IsMine);

public sealed record PlayerView(
    Guid SessionId,
    SessionStatus Status,
    int Round,
    Guid? CurrentTurnId,
    int Version,
    IReadOnlyList<PlayerCombatantView> Combatants);

/// <summary>
/// Projections of a session. The player view is computed on the server so a player's
/// client never receives hidden combatants or exact monster hit points.
/// </summary>
public static class CombatViews
{
    public static DmView ForDm(CombatSession session) => new(
        session.Id,
        session.JoinCode,
        session.Status,
        session.Round,
        session.CurrentTurnId,
        session.Version,
        session.TurnOrder.Select(c => new DmCombatantView(
            c.Id, c.Kind, c.Name, c.Initiative, c.MaxHp, c.CurrentHp, c.TempHp, c.Ac,
            c.Conditions, c.IsHidden, c.IsDefeated, c.ClaimedBy, c.SourceRef)).ToList());

    public static PlayerView ForPlayer(CombatSession session, Guid? guestId)
    {
        var visible = session.TurnOrder.Where(c => !c.IsHidden).ToList();
        var currentIsVisible = visible.Any(c => c.Id == session.CurrentTurnId);
        return new PlayerView(
            session.Id,
            session.Status,
            session.Round,
            currentIsVisible ? session.CurrentTurnId : null,
            session.Version,
            visible.Select(c => ToPlayer(c, guestId)).ToList());
    }

    public static HpBand BandFor(int currentHp, int maxHp) => currentHp switch
    {
        <= 0 => HpBand.Defeated,
        _ when currentHp >= maxHp => HpBand.Unhurt,
        _ when currentHp * 2 > maxHp => HpBand.Wounded,
        _ => HpBand.Bloodied,
    };

    private static PlayerCombatantView ToPlayer(Combatant c, Guid? guestId)
    {
        var isPc = c.Kind == CombatantKind.Pc;
        return new PlayerCombatantView(
            c.Id,
            c.Kind,
            c.Name,
            c.Initiative,
            isPc ? c.MaxHp : null,
            isPc ? c.CurrentHp : null,
            isPc ? c.TempHp : null,
            isPc ? null : BandFor(c.CurrentHp, c.MaxHp),
            c.Conditions,
            c.IsDefeated,
            c.ClaimedBy is not null,
            guestId is not null && c.ClaimedBy == guestId);
    }
}
```

`src/DmCompanionLive.Domain/Combat/JoinCode.cs`:
```csharp
using System.Security.Cryptography;

namespace DmCompanionLive.Domain.Combat;

public static class JoinCode
{
    /// <summary>Uppercase letters and digits without the ambiguous 0/O and 1/I.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    public static string Normalize(string input) => input.Trim().ToUpperInvariant();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, 41 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add DM and player views and join codes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Tables and DM users

**Files:**
- Create: `src/DmCompanionLive.Domain/Tables/Table.cs`, `src/DmCompanionLive.Domain/Tables/PlayerCharacter.cs`, `src/DmCompanionLive.Domain/Users/DmUser.cs`
- Create: `tests/DmCompanionLive.Domain.Tests/Tables/TableTests.cs`, `tests/DmCompanionLive.Domain.Tests/Users/DmUserTests.cs`

**Interfaces:**
- Consumes: `CombatSession.Create`, `PcSeed` (Task 2).
- Produces:
  - `Table.Create(Guid ownerId, string name, DateTimeOffset now)`; props `Id, OwnerId, Name, CreatedAt, Characters`; methods `Rename(string)`, `AddCharacter(string name, int maxHp, int ac, int dexModifier) : PlayerCharacter`, `UpdateCharacter(Guid id, string name, int maxHp, int ac, int dexModifier)`, `RemoveCharacter(Guid id)`, `StartSession(string joinCode, DateTimeOffset now) : CombatSession`
  - `PlayerCharacter` props `Id, Name, MaxHp, Ac, DexModifier`
  - `DmUser.Create(string externalId, string displayName, DateTimeOffset now)`; props `Id, ExternalId, DisplayName, CreatedAt`; `const int MaxDisplayNameLength = 80`

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/Tables/TableTests.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Tables;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Tables;

public class TableTests
{
    [Fact]
    public void A_table_manages_its_characters()
    {
        var table = Table.Create(DmId, "  Costa de Sal ", Now);

        var lyra = table.AddCharacter("Lyra", 20, 15, 2);
        table.AddCharacter("Torvald", 30, 18, 0);
        table.UpdateCharacter(lyra.Id, "Lyra Vientoalto", 22, 15, 3);
        table.RemoveCharacter(table.Characters[1].Id);
        table.Rename("Costa de Sal II");

        table.Name.Should().Be("Costa de Sal II");
        table.Characters.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "Lyra Vientoalto", MaxHp = 22, Ac = 15, DexModifier = 3 });
    }

    [Fact]
    public void Starting_a_session_copies_the_characters_and_the_owner()
    {
        var table = Table.Create(DmId, "Costa de Sal", Now);
        table.AddCharacter("Lyra", 20, 15, 2);

        var session = table.StartSession("ABC234", Now);

        session.TableId.Should().Be(table.Id);
        session.OwnerId.Should().Be(DmId);
        session.Combatants.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "Lyra", Kind = CombatantKind.Pc, MaxHp = 20, DexModifier = 2 });
    }

    [Fact]
    public void Invalid_values_are_rejected()
    {
        var table = Table.Create(DmId, "Costa de Sal", Now);

        var emptyName = () => Table.Create(DmId, " ", Now);
        var badCharacter = () => table.AddCharacter("Lyra", 0, 15, 0);
        var unknown = () => table.RemoveCharacter(Guid.NewGuid());

        emptyName.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        badCharacter.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        unknown.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.CombatantNotFound);
    }
}
```

`tests/DmCompanionLive.Domain.Tests/Users/DmUserTests.cs`:
```csharp
using DmCompanionLive.Domain.Users;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Users;

public class DmUserTests
{
    [Fact]
    public void Creates_a_user_with_a_trimmed_display_name()
    {
        var user = DmUser.Create("google:123", "  Pau ", Now);

        user.Id.Should().NotBeEmpty();
        user.DisplayName.Should().Be("Pau");
        user.ExternalId.Should().Be("google:123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_display_name(string name)
    {
        var act = () => DmUser.Create("dev:x", name, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `Table`, `DmUser` missing.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Domain/Tables/PlayerCharacter.cs`:
```csharp
namespace DmCompanionLive.Domain.Tables;

public sealed class PlayerCharacter
{
    private PlayerCharacter()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int MaxHp { get; private set; }
    public int Ac { get; private set; }
    public int DexModifier { get; private set; }

    internal static PlayerCharacter Create(string name, int maxHp, int ac, int dexModifier)
    {
        var character = new PlayerCharacter { Id = Guid.NewGuid() };
        character.Update(name, maxHp, ac, dexModifier);
        return character;
    }

    internal void Update(string name, int maxHp, int ac, int dexModifier)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Name must have 1 to 80 characters.");
        }

        if (maxHp < 1 || ac < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Max HP must be at least 1 and AC cannot be negative.");
        }

        Name = name.Trim();
        MaxHp = maxHp;
        Ac = ac;
        DexModifier = dexModifier;
    }
}
```

`src/DmCompanionLive.Domain/Tables/Table.cs`:
```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tables;

/// <summary>A DM's campaign: its name and the party's player characters.</summary>
public sealed class Table
{
    public const int MaxNameLength = 80;

    private readonly List<PlayerCharacter> _characters = [];

    private Table()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<PlayerCharacter> Characters => _characters;

    public static Table Create(Guid ownerId, string name, DateTimeOffset now)
    {
        var table = new Table { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = now };
        table.Rename(name);
        return table;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxNameLength} characters.");
        }

        Name = name.Trim();
    }

    public PlayerCharacter AddCharacter(string name, int maxHp, int ac, int dexModifier)
    {
        var character = PlayerCharacter.Create(name, maxHp, ac, dexModifier);
        _characters.Add(character);
        return character;
    }

    public void UpdateCharacter(Guid id, string name, int maxHp, int ac, int dexModifier) =>
        FindCharacter(id).Update(name, maxHp, ac, dexModifier);

    public void RemoveCharacter(Guid id) => _characters.Remove(FindCharacter(id));

    public CombatSession StartSession(string joinCode, DateTimeOffset now) =>
        CombatSession.Create(
            Id,
            OwnerId,
            joinCode,
            _characters.Select(c => new PcSeed(c.Name, c.MaxHp, c.Ac, c.DexModifier)),
            now);

    private PlayerCharacter FindCharacter(Guid id) =>
        _characters.SingleOrDefault(c => c.Id == id)
        ?? throw new DomainException(DomainErrorCode.CombatantNotFound, $"Character {id} not found.");
}
```

`src/DmCompanionLive.Domain/Users/DmUser.cs`:
```csharp
namespace DmCompanionLive.Domain.Users;

/// <summary>A signed-in DM. Only an external id (e.g. "google:&lt;sub&gt;") and a display name are stored.</summary>
public sealed class DmUser
{
    public const int MaxDisplayNameLength = 80;

    private DmUser()
    {
        ExternalId = string.Empty;
        DisplayName = string.Empty;
    }

    public Guid Id { get; private set; }
    public string ExternalId { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static DmUser Create(string externalId, string displayName, DateTimeOffset now)
    {
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
            CreatedAt = now,
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS, 47 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add tables with player characters and DM users

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Persistence with EF Core and PostgreSQL

**Files:**
- Create: `src/DmCompanionLive.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/DmUserConfiguration.cs`, `TableConfiguration.cs`, `CombatSessionConfiguration.cs`
- Create: `src/DmCompanionLive.Infrastructure/Persistence/AppDbContextFactory.cs`
- Create: `src/DmCompanionLive.Infrastructure/DependencyInjection.cs`
- Create (generated): `src/DmCompanionLive.Infrastructure/Persistence/Migrations/*`
- Create: `.config/dotnet-tools.json` (generated)
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/PostgresFixture.cs`
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs`

**Interfaces:**
- Consumes: `DmUser`, `Table`, `CombatSession` (Tasks 2–6).
- Produces:
  - `AppDbContext` with `DbSet<DmUser> Users`, `DbSet<Table> Tables`, `DbSet<CombatSession> Sessions`
  - `DependencyInjection.AddInfrastructure(this IServiceCollection, string connectionString)`
  - Test fixture `PostgresFixture` (`ConnectionString`, `CreateDbContext()`), collection name `PostgresCollection.Name = "postgres"`; Task 8 extends it with `Factory`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/PostgresFixture.cs`:
```csharp
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
```

`tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: build FAILS — `DmCompanionLive.Infrastructure.Persistence.AppDbContext` missing.

- [ ] **Step 3: Implement the DbContext and configurations**

`src/DmCompanionLive.Infrastructure/Persistence/AppDbContext.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DmUser> Users => Set<DmUser>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<CombatSession> Sessions => Set<CombatSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

`src/DmCompanionLive.Infrastructure/Persistence/Configurations/DmUserConfiguration.cs`:
```csharp
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class DmUserConfiguration : IEntityTypeConfiguration<DmUser>
{
    public void Configure(EntityTypeBuilder<DmUser> builder)
    {
        builder.ToTable("dm_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.ExternalId).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(DmUser.MaxDisplayNameLength).IsRequired();
    }
}
```

`src/DmCompanionLive.Infrastructure/Persistence/Configurations/TableConfiguration.cs`:
```csharp
using DmCompanionLive.Domain.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("tables");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Table.MaxNameLength).IsRequired();
        builder.HasIndex(t => t.OwnerId);

        builder.OwnsMany(t => t.Characters, character =>
        {
            character.ToTable("player_characters");
            character.WithOwner().HasForeignKey("TableId");
            character.HasKey(c => c.Id);
            character.Property(c => c.Id).ValueGeneratedNever();
            character.Property(c => c.Name).HasMaxLength(80).IsRequired();
        });
        builder.Navigation(t => t.Characters).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

`src/DmCompanionLive.Infrastructure/Persistence/Configurations/CombatSessionConfiguration.cs`:
```csharp
using DmCompanionLive.Domain.Combat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class CombatSessionConfiguration : IEntityTypeConfiguration<CombatSession>
{
    public void Configure(EntityTypeBuilder<CombatSession> builder)
    {
        builder.ToTable("combat_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.JoinCode).HasMaxLength(JoinCode.Length).IsRequired();
        builder.HasIndex(s => s.JoinCode).IsUnique();
        builder.HasIndex(s => s.OwnerId);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Ignore(s => s.UncommittedEvents);
        builder.Ignore(s => s.TurnOrder);

        builder.OwnsMany(s => s.Combatants, combatant =>
        {
            combatant.ToTable("combatants");
            combatant.WithOwner().HasForeignKey("SessionId");
            combatant.HasKey(c => c.Id);
            combatant.Property(c => c.Id).ValueGeneratedNever();
            combatant.Property(c => c.Name).HasMaxLength(Combatant.MaxNameLength).IsRequired();
            combatant.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
            combatant.Property(c => c.SourceRef).HasMaxLength(120);
            combatant.Ignore(c => c.IsDefeated);
        });
        builder.Navigation(s => s.Combatants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

`src/DmCompanionLive.Infrastructure/Persistence/AppDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DmCompanionLive.Infrastructure.Persistence;

/// <summary>Used only by the dotnet-ef tool to create migrations; it never connects.</summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dmcompanionlive;Username=dmcl;Password=dmcl")
            .Options);
}
```

`src/DmCompanionLive.Infrastructure/DependencyInjection.cs`:
```csharp
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
```

- [ ] **Step 4: Create the initial migration**

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.15
dotnet ef migrations add Initial \
  --project src/DmCompanionLive.Infrastructure \
  --startup-project src/DmCompanionLive.Infrastructure \
  --output-dir Persistence/Migrations
```
Expected: files `Persistence/Migrations/*_Initial.cs`, `*_Initial.Designer.cs`, `AppDbContextModelSnapshot.cs`. Open `*_Initial.cs` and check it creates `dm_users`, `tables`, `player_characters`, `combat_sessions`, `combatants`, a unique index on `combat_sessions.JoinCode` and a unique index on `dm_users.ExternalId`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, 4 tests (first run pulls `postgres:17-alpine`).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: persist users, tables and combat sessions with EF Core and PostgreSQL

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: API host — health, problem details, OpenAPI and dev login

**Files:**
- Modify: `src/DmCompanionLive.Api/Program.cs` (full replacement)
- Create: `src/DmCompanionLive.Api/appsettings.json`, `src/DmCompanionLive.Api/appsettings.Development.json`
- Create: `src/DmCompanionLive.Api/Auth/AuthSetup.cs`, `src/DmCompanionLive.Api/Auth/DmPrincipal.cs`, `src/DmCompanionLive.Api/Features/Auth/AuthEndpoints.cs`
- Create: `src/DmCompanionLive.Api/Errors/DomainExceptionHandler.cs`
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/PostgresFixture.cs` (add `Factory`)
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/ApiFactory.cs`, `TestJson.cs`, `ClientExtensions.cs`
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Features/HostTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Features/AuthTests.cs`

**Interfaces:**
- Consumes: `AddInfrastructure`, `AppDbContext`, `DmUser`, `DomainException`.
- Produces:
  - `POST /dev/login` body `{ "displayName": string }` → 200 `{ id, displayName }` + auth cookie `dmcl.auth` (only in `Development` and `Testing` environments)
  - `GET /api/auth/me` → 200 `{ id, displayName }` or 401; `POST /api/auth/logout` → 204
  - `GET /health` → 200 `Healthy` (includes DB check); `GET /openapi/v1.json`; Scalar UI at `/scalar/v1`
  - `DmPrincipal.Create(DmUser)`, extension `ClaimsPrincipal.GetDmUserId() : Guid`
  - Domain errors → `ProblemDetails` with `code` extension; status 403 (`NotAllowed`, `NotYourCharacter`), 404 (`CombatantNotFound`), 400 (`InvalidValue`), 409 (others)
  - Enums serialised as strings
  - Test helpers: `ApiFactory`, `PostgresFixture.Factory`, `ApiFactory.CreateHttpsClient()`, `ApiFactory.CreateDmClientAsync(string? name = null)`, `TestJson.Options`

- [ ] **Step 1: Write the failing tests**

Replace `PostgresFixture` (`tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/PostgresFixture.cs`) with:
```csharp
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
```

`tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/ApiFactory.cs`:
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
    }

    /// <summary>HTTPS base address so the Secure auth cookie is sent back.</summary>
    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });
}
```

`tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/TestJson.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
```

`tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/ClientExtensions.cs`:
```csharp
using System.Net.Http.Json;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class ClientExtensions
{
    public static async Task<HttpClient> CreateDmClientAsync(this ApiFactory factory, string? name = null)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
```

`tests/DmCompanionLive.Api.IntegrationTests/Features/HostTests.cs`:
```csharp
using System.Net;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class HostTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_reports_healthy_with_the_database()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task The_openapi_document_is_published()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"openapi\"");
    }
}
```

`tests/DmCompanionLive.Api.IntegrationTests/Features/AuthTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string DisplayName);

    [Fact]
    public async Task Anonymous_callers_get_401_from_me()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dev_login_signs_in_and_reuses_the_same_user()
    {
        var name = $"DM {Guid.NewGuid():N}";
        var first = await postgres.Factory.CreateDmClientAsync(name);
        var second = await postgres.Factory.CreateDmClientAsync(name);

        var me1 = await first.GetFromJsonAsync<Me>("/api/auth/me", TestJson.Options);
        var me2 = await second.GetFromJsonAsync<Me>("/api/auth/me", TestJson.Options);

        me1!.DisplayName.Should().Be(name);
        me2!.Id.Should().Be(me1.Id);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var logout = await client.PostAsync("/api/auth/logout", null);
        var me = await client.GetAsync("/api/auth/me");

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_invalid_display_name_is_a_400_problem_with_a_code()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = new string('x', 81) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"InvalidValue\"");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: the 4 persistence tests PASS; HostTests and AuthTests FAIL (404s / missing connection string handling).

- [ ] **Step 3: Implement the host**

`src/DmCompanionLive.Api/appsettings.json`:
```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Database": { "MigrateOnStartup": false }
}
```

`src/DmCompanionLive.Api/appsettings.Development.json` (local docker-compose database only, not a secret):
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=dmcompanionlive;Username=dmcl;Password=dmcl"
  },
  "Database": { "MigrateOnStartup": true }
}
```

`src/DmCompanionLive.Api/Auth/DmPrincipal.cs`:
```csharp
using System.Security.Claims;
using DmCompanionLive.Domain.Users;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DmCompanionLive.Api.Auth;

public static class DmPrincipal
{
    public static ClaimsPrincipal Create(DmUser user) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    public static Guid GetDmUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The caller is not an authenticated DM."));
}
```

`src/DmCompanionLive.Api/Auth/AuthSetup.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DmCompanionLive.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddDmAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
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
            });
        services.AddAuthorization();
        return services;
    }
}
```

`src/DmCompanionLive.Api/Errors/DomainExceptionHandler.cs`:
```csharp
using DmCompanionLive.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace DmCompanionLive.Api.Errors;

/// <summary>Maps domain errors to ProblemDetails with a stable, localizable "code".</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusFor(domain.Code);
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = domain.Code.ToString(),
                Detail = domain.Message,
                Status = httpContext.Response.StatusCode,
                Extensions = { ["code"] = domain.Code.ToString() },
            },
        });
    }

    public static int StatusFor(DomainErrorCode code) => code switch
    {
        DomainErrorCode.NotAllowed or DomainErrorCode.NotYourCharacter => StatusCodes.Status403Forbidden,
        DomainErrorCode.CombatantNotFound => StatusCodes.Status404NotFound,
        DomainErrorCode.InvalidValue => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict,
    };
}
```

`src/DmCompanionLive.Api/Features/Auth/AuthEndpoints.cs`:
```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Users;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Auth;

public static class AuthEndpoints
{
    public sealed record DevLoginRequest(string DisplayName);

    public sealed record MeResponse(Guid Id, string DisplayName);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();
        return app;
    }

    /// <summary>Development and test sign-in. Never mapped in production.</summary>
    public static IEndpointRouteBuilder MapDevLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/login", DevLogin).ExcludeFromDescription();
        return app;
    }

    private static async Task<Ok<MeResponse>> DevLogin(
        DevLoginRequest request, AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var externalId = $"dev:{request.DisplayName.Trim().ToLowerInvariant()}";
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is null)
        {
            user = DmUser.Create(externalId, request.DisplayName, clock.GetUtcNow());
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

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

`src/DmCompanionLive.Api/Program.cs` (full replacement):
```csharp
using System.Text.Json.Serialization;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Errors;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Infrastructure;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing connection string 'Postgres'.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDmAuthentication(builder.Environment);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapDevLogin();
}

app.MapAuth();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add the API host with health, problem details, OpenAPI and dev login

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Tables slice

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Tables/TablesEndpoints.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs` (add `app.MapTables();` after `app.MapAuth();`)
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Features/TablesTests.cs`

**Interfaces:**
- Consumes: `Table`, `AppDbContext`, `GetDmUserId()`, `CreateDmClientAsync()`.
- Produces (all require a signed-in DM; tables of other DMs answer 404):
  - `GET /api/tables` → `TableSummaryDto[]` (`Id, Name, CharacterCount`), ordered by name
  - `POST /api/tables` body `{ name }` → 201 `TableDto` (`Id, Name, Characters: CharacterDto[]`), `Location: /api/tables/{id}`
  - `GET /api/tables/{id}` → `TableDto`; `PUT /api/tables/{id}` body `{ name }` → 204; `DELETE /api/tables/{id}` → 204
  - `POST /api/tables/{id}/characters` body `CharacterRequest` (`Name, MaxHp, Ac, DexModifier`) → 201 `CharacterDto` (`Id, Name, MaxHp, Ac, DexModifier`)
  - `PUT /api/tables/{id}/characters/{characterId}` → 204; `DELETE …` → 204

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Api.IntegrationTests/Features/TablesTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class TablesTests(PostgresFixture postgres)
{
    private sealed record CharacterDto(Guid Id, string Name, int MaxHp, int Ac, int DexModifier);

    private sealed record TableDto(Guid Id, string Name, List<CharacterDto> Characters);

    private sealed record TableSummaryDto(Guid Id, string Name, int CharacterCount);

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/tables")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dm_creates_and_edits_a_table_with_characters()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var created = await client.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var table = (await created.Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        created.Headers.Location!.ToString().Should().Be($"/api/tables/{table.Id}");

        var addLyra = await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Lyra", maxHp = 20, ac = 15, dexModifier = 2 });
        addLyra.StatusCode.Should().Be(HttpStatusCode.Created);
        var lyra = (await addLyra.Content.ReadFromJsonAsync<CharacterDto>(TestJson.Options))!;
        await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Torvald", maxHp = 30, ac = 18, dexModifier = 0 });

        (await client.PutAsJsonAsync(
            $"/api/tables/{table.Id}/characters/{lyra.Id}", new { name = "Lyra", maxHp = 22, ac = 15, dexModifier = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/api/tables/{table.Id}", new { name = "Costa de Sal II" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loaded = await client.GetFromJsonAsync<TableDto>($"/api/tables/{table.Id}", TestJson.Options);
        loaded!.Name.Should().Be("Costa de Sal II");
        loaded.Characters.Should().HaveCount(2);
        loaded.Characters.Single(c => c.Name == "Lyra").MaxHp.Should().Be(22);

        var list = await client.GetFromJsonAsync<List<TableSummaryDto>>("/api/tables", TestJson.Options);
        list.Should().ContainSingle().Which.CharacterCount.Should().Be(2);
    }

    [Fact]
    public async Task Removing_characters_and_tables()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Temp" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        var character = (await (await client.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name = "Nim", maxHp = 10, ac = 12, dexModifier = 1 }))
            .Content.ReadFromJsonAsync<CharacterDto>(TestJson.Options))!;

        (await client.DeleteAsync($"/api/tables/{table.Id}/characters/{character.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<TableDto>($"/api/tables/{table.Id}", TestJson.Options))!
            .Characters.Should().BeEmpty();
        (await client.DeleteAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_dm_cannot_see_or_change_my_table()
    {
        var owner = await postgres.Factory.CreateDmClientAsync();
        var intruder = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await owner.PostAsJsonAsync("/api/tables", new { name = "Mine" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;

        (await intruder.GetAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PutAsJsonAsync($"/api/tables/{table.Id}", new { name = "Stolen" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetFromJsonAsync<List<TableSummaryDto>>("/api/tables", TestJson.Options))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_input_is_a_400_problem()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Valid" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;

        (await client.PostAsJsonAsync("/api/tables", new { name = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Nim", maxHp = 0, ac = 12, dexModifier = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter FullyQualifiedName~TablesTests`
Expected: FAIL — 404 on `/api/tables` (the anonymous test may already pass with 404 ≠ 401 → it fails too).

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Api/Features/Tables/TablesEndpoints.cs`:
```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Tables;

public static class TablesEndpoints
{
    public sealed record TableRequest(string Name);

    public sealed record CharacterRequest(string Name, int MaxHp, int Ac, int DexModifier);

    public sealed record CharacterDto(Guid Id, string Name, int MaxHp, int Ac, int DexModifier);

    public sealed record TableDto(Guid Id, string Name, IReadOnlyList<CharacterDto> Characters);

    public sealed record TableSummaryDto(Guid Id, string Name, int CharacterCount);

    public static IEndpointRouteBuilder MapTables(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tables").RequireAuthorization().WithTags("Tables");
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Rename);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/characters", AddCharacter);
        group.MapPut("/{id:guid}/characters/{characterId:guid}", UpdateCharacter);
        group.MapDelete("/{id:guid}/characters/{characterId:guid}", RemoveCharacter);
        return app;
    }

    private static async Task<Ok<List<TableSummaryDto>>> List(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var tables = await db.Tables
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .Select(t => new TableSummaryDto(t.Id, t.Name, t.Characters.Count))
            .ToListAsync(ct);
        return TypedResults.Ok(tables);
    }

    private static async Task<Created<TableDto>> Create(
        TableRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var table = Table.Create(user.GetDmUserId(), request.Name, clock.GetUtcNow());
        db.Tables.Add(table);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tables/{table.Id}", ToDto(table));
    }

    private static async Task<Results<Ok<TableDto>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        return table is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(table));
    }

    private static async Task<Results<NoContent, NotFound>> Rename(
        Guid id, TableRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        db.Tables.Remove(table);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<CharacterDto>, NotFound>> AddCharacter(
        Guid id, CharacterRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        var character = table.AddCharacter(request.Name, request.MaxHp, request.Ac, request.DexModifier);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tables/{id}/characters/{character.Id}", ToDto(character));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateCharacter(
        Guid id, Guid characterId, CharacterRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.UpdateCharacter(characterId, request.Name, request.MaxHp, request.Ac, request.DexModifier);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveCharacter(
        Guid id, Guid characterId, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.RemoveCharacter(characterId);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Tables of other DMs are reported as not found, never as forbidden.</summary>
    private static Task<Table?> FindOwnedAsync(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        return db.Tables.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);
    }

    private static TableDto ToDto(Table table) =>
        new(table.Id, table.Name, table.Characters.Select(ToDto).ToList());

    private static CharacterDto ToDto(PlayerCharacter c) => new(c.Id, c.Name, c.MaxHp, c.Ac, c.DexModifier);
}
```

In `Program.cs` add `using DmCompanionLive.Api.Features.Tables;` and, after `app.MapAuth();`:
```csharp
app.MapTables();
```

Note: removing an unknown character id raises `DomainException(CombatantNotFound)` → 404 via `DomainExceptionHandler`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, 15 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add the tables slice for DMs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Sessions slice — start a combat and read the DM view

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Sessions/SessionsEndpoints.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs` (add `app.MapSessions();` after `app.MapTables();`)
- Create: `tests/DmCompanionLive.Api.IntegrationTests/Features/SessionsTests.cs`

**Interfaces:**
- Consumes: `Table.StartSession`, `JoinCode`, `CombatViews.ForDm`, `DmView`.
- Produces:
  - `POST /api/tables/{tableId}/sessions` → 201 `SessionCreatedDto(Guid SessionId, string JoinCode)`, `Location: /api/sessions/{joinCode}`; 404 for a table that is not the caller's
  - `GET /api/sessions/{code}` → 200 `DmView` for the owning DM (code is case-insensitive); 404 otherwise

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Api.IntegrationTests/Features/SessionsTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SessionsTests(PostgresFixture postgres)
{
    private sealed record TableDto(Guid Id, string Name);

    private sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    [Fact]
    public async Task A_dm_starts_a_session_with_the_party_and_reads_it_by_code()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);

        var response = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;
        created.JoinCode.Should().HaveLength(JoinCode.Length);
        response.Headers.Location!.ToString().Should().Be($"/api/sessions/{created.JoinCode}");

        var view = await client.GetFromJsonAsync<DmView>(
            $"/api/sessions/{created.JoinCode.ToLowerInvariant()}", TestJson.Options);
        view!.SessionId.Should().Be(created.SessionId);
        view.Status.Should().Be(SessionStatus.Lobby);
        view.Combatants.Should().ContainSingle().Which.Name.Should().Be("Lyra");
    }

    [Fact]
    public async Task Other_dms_cannot_start_or_read_my_sessions()
    {
        var owner = await postgres.Factory.CreateDmClientAsync();
        var intruder = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(owner);
        var created = (await (await owner.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;

        (await intruder.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/sessions/{created.JoinCode}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<TableDto> CreateTableWithLyraAsync(HttpClient client)
    {
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Lyra", maxHp = 20, ac = 15, dexModifier = 2 });
        return table;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter FullyQualifiedName~SessionsTests`
Expected: FAIL — 404/405 on the session routes.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Api/Features/Sessions/SessionsEndpoints.cs`:
```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Sessions;

public static class SessionsEndpoints
{
    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tables/{tableId:guid}/sessions", Create).RequireAuthorization().WithTags("Sessions");
        app.MapGet("/api/sessions/{code}", GetDmView).RequireAuthorization().WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Created<SessionCreatedDto>, NotFound>> Create(
        Guid tableId, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == tableId && t.OwnerId == ownerId, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        string code;
        do
        {
            code = JoinCode.New();
        }
        while (await db.Sessions.AnyAsync(s => s.JoinCode == code, ct));

        var session = table.StartSession(code, clock.GetUtcNow());
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/sessions/{code}", new SessionCreatedDto(session.Id, code));
    }

    private static async Task<Results<Ok<DmView>, NotFound>> GetDmView(
        string code, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var normalized = JoinCode.Normalize(code);
        var ownerId = user.GetDmUserId();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.JoinCode == normalized && s.OwnerId == ownerId, ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(CombatViews.ForDm(session));
    }
}
```

In `Program.cs` add `using DmCompanionLive.Api.Features.Sessions;` and, after `app.MapTables();`:
```csharp
app.MapSessions();
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test DmCompanionLive.slnx`
Expected: PASS — 47 domain tests and 17 integration tests, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: start combat sessions and read the DM view by join code

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Local database, CI and repository basics

**Files:**
- Create: `docker-compose.yml`, `.github/workflows/ci.yml`, `README.md`, `LICENSE`

**Interfaces:**
- Produces: `docker compose up -d db` gives the database `appsettings.Development.json` expects; CI that builds and runs all tests on PRs and `main`.

- [ ] **Step 1: Write the files**

`docker-compose.yml`:
```yaml
# Local development only. These credentials never leave this machine.
services:
  db:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: dmcompanionlive
      POSTGRES_USER: dmcl
      POSTGRES_PASSWORD: dmcl
    ports:
      - "5432:5432"
    volumes:
      - db-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dmcl -d dmcompanionlive"]
      interval: 5s
      retries: 10

volumes:
  db-data:
```

`.github/workflows/ci.yml`:
```yaml
name: CI

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read

jobs:
  backend:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - run: dotnet restore DmCompanionLive.slnx
      - run: dotnet build DmCompanionLive.slnx --no-restore -c Release
      - run: dotnet test DmCompanionLive.slnx --no-build -c Release --logger "trx" --results-directory TestResults
      - uses: actions/upload-artifact@v4
        if: failure()
        with:
          name: test-results
          path: TestResults
```

`LICENSE`: the standard MIT license text with the line `Copyright (c) 2026 Pau Vidal`.

`README.md`:
```markdown
# dm-companion-live

The online, multi-user companion to [dm-companion](https://github.com/pvidram/dm-companion): the DM runs a live D&D 5e combat from a PC or tablet, and players follow it — and manage their own character — from their phones, in real time.

> Work in progress. Design: [`docs/superpowers/specs/2026-11-24-dm-companion-live-design.md`](docs/superpowers/specs/2026-11-24-dm-companion-live-design.md).

## Stack

ASP.NET Core 10 · EF Core + PostgreSQL · SignalR · React + TypeScript (planned) · xUnit + Testcontainers.

## Run locally

Requirements: .NET SDK from `global.json`, Docker.

```bash
docker compose up -d db
dotnet run --project src/DmCompanionLive.Api
# API docs: http://localhost:<port>/scalar/v1 · dev sign-in: POST /dev/login { "displayName": "..." }
```

## Tests

```bash
dotnet test DmCompanionLive.slnx   # integration tests start PostgreSQL with Testcontainers (Docker required)
```

## License

[MIT](LICENSE). Not affiliated with, endorsed or sponsored by Wizards of the Coast.
```

- [ ] **Step 2: Verify the local setup**

```bash
docker compose up -d db
dotnet run --project src/DmCompanionLive.Api --urls http://localhost:5199 &
sleep 8
curl -s http://localhost:5199/health
kill %1
docker compose down
```
Expected: `Healthy` (migrations applied at startup in Development).

- [ ] **Step 3: Run all tests once more**

Run: `dotnet test DmCompanionLive.slnx -c Release`
Expected: PASS, 64 tests, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: add local database, CI workflow, README and license

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Notes for the next plans

- Spec §6 asks for an antiforgery header for the SPA. With cookie `SameSite=Lax`, JSON-only endpoints and no CORS, cross-site requests cannot carry the cookie or a JSON body; Plan 4 decides whether to add ASP.NET Core antiforgery on top, with this reasoning recorded.
- `CombatEvent`s are produced by the domain but not yet persisted: Plan 2 adds the `combat_log` table, saves `UncommittedEvents` in the same transaction as the session, and filters events about hidden combatants out of the player log.
- The spec's `TurnIndex` became `CurrentTurnId` (see Task 3).
