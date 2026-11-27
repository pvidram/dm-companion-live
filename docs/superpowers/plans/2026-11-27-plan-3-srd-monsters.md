# Plan 3 — SRD monsters, favourites and templates

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The DM searches the SRD 5.2.1 bestiary in English or Spanish, marks favourites, keeps their own monster templates, and adds monsters to a live combat from the SRD, a template or ad-hoc stats — several at once, with average or rolled hit points — while players keep seeing only what Plan 2 lets them see.

**Architecture:** A console tool (`tools/DmCompanionLive.SrdImport`) merges the Spanish creature pack of dm-companion with the English TidyTuesday SRD 5.2.1 dataset into `srd-monsters.json`, embedded in Infrastructure and served by an in-memory `ISrdCatalog` (no database table). The domain gains `Names`, `HpFormula`, `MonsterSource`, `InitiativeBonus`, `CombatSession.AddMonsters`, `MonsterTemplate` and `FavoriteMonster`. The API gains `Features/Srd` (catalogue search, language resolution), `Features/Monsters` (favourites, templates) and a hub `AddMonster` that resolves its source on the server before running the usual command pipeline.

**Tech Stack:** As Plan 2 (.NET 10, ASP.NET Core 10, EF Core 10.0.15, Npgsql 10.0.4, xUnit 2.9.3, AwesomeAssertions 9.6.0, Testcontainers 4.17.0, `Microsoft.AspNetCore.SignalR.Client` 10.0.15). No new packages: JSON, CSV and text normalisation use the shared framework.

**Spec:** `docs/superpowers/specs/2026-11-27-plan-3-srd-monsters-design.md` (this plan's design) and `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md` (§3 domain, §4 API, §7 SRD). Carry-over: `docs/superpowers/plans/2026-11-24-plan-1-carryover.md` — the two "Plan 3" items and the opportunistic "magic `80` / repeated name validation" item are covered (see the coverage table at the end).

## Global Constraints

- Target framework `net10.0`; `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors` stay on (from `Directory.Build.props`).
- Package versions exactly as already referenced; add no package.
- Domain project has **no** package or project references.
- Code, identifiers, commit messages and docs in English. Conventional commits; every commit message ends with the line `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Work on the existing branch `feature/plan-3-srd-monsters` (created from `main`); never commit to `main`, never push.
- Every `dotnet build` and `dotnet test` runs through the machine-wide lock: `~/.claude/bin/heavy.sh <command>`. Exit code 75 means the lock is busy: wait and retry.
- Integration tests need Docker. No external service is called at test time.
- Run commands from the repository root.

## Decisions taken in this plan

These refine the Plan 3 design after looking at the real data; Task 9 updates the design to match.

- **Spanish input is the dm-companion creature pack**, not compendio-srd's raw `datos/` folder: `src/DmCompanion.Core/Glossary/Packs/criaturas.json` of dm-companion at commit `b95f1e65795d752d8ab167c60ff55751b38ec75b`, which was generated from compendio-srd commit `2d61b5dcabe91ab69d5c4a5a41822eb83c2038cc`. It already has structured numbers, the English name and the Spanish stat block, so we do not re-parse Markdown.
- **English input:** TidyTuesday `data/2025/2025-05-27/monsters.csv` at commit `97a250500c85463e590b23f9e195c58232a04584` (SHA-256 `0f3ba32d721af74e66a92bbb2d5eae6cb394796c2887da81c88af571a82cedf4`). It has 330 rows and a `full_text` column with the whole English stat block extracted from the SRD 5.2.1 PDF.
- **Every creature has both languages.** The two sources match 1:1 by English name (330/330), so the importer **fails** on any mismatch instead of falling back to Spanish text. `StatBlock.En` is never missing and the API has no `statBlockLang` field.
- **Stat block shape:** `{ subtitle, details: [{ label, text }], sections: [{ kind, entries: [{ name?, text }] }] }`. `kind` is a language-neutral enum (`Traits`, `Actions`, `BonusActions`, `Reactions`, `LegendaryActions`) so clients translate section titles. Details hold speed, skills, senses, languages, resistances, etc. as given by each source (labels in that language). Plain text only.
- **Numbers come from the Spanish pack** (AC, HP, hit dice, initiative bonus, CR, size, type, abilities). The importer prints a warning when the English row disagrees on AC, HP or initiative.
- **`MonsterNotFound`** is a new `DomainErrorCode`: REST 404 with `code: "MonsterNotFound"` for an unknown SRD slug, and the hub error for an unknown slug or a template that is missing or belongs to another DM. A template that does not exist on REST is a bare 404, as tables are.
- **Duplicate from SRD is its own route:** `POST /api/monsters/templates/from-srd/{slug}?lang=` (instead of `?fromSrd=` on the create route) so each route has one body shape.
- **`CombatSession.AddMonster(actor, seed, now)` stays** as a one-line wrapper over `AddMonsters(actor, seed, 1, hpDice: null, now)`; tests and fixtures keep using it. Both log `MonstersAdded` (Plan 2 logged `MonsterAdded`).
- **`AddMonsters` takes `IDiceRoller? hpDice`** instead of `bool rollHp` + dice: `null` means average HP.
- **The `MonstersAdded` log entry** uses the first added monster as its subject (`combatantId`, `name`, `kind`, visibility) and adds `baseName`, `count` and `combatantIds` (comma-separated). It never carries hit points.
- **Templates use their initiative bonus as the Dex tie-break** (they have no Dex score); ad-hoc monsters too.
- **Initiative bonus is bounded** to ±30 for monsters and templates, so `d20 + bonus` can never overflow.
- **Hub source errors** (`InvalidValue`, `MonsterNotFound`, `NotAllowed` for players) are returned before the session is loaded, with the caller's `expectedVersion` as `Version`.
- **Hub language:** the `lang` query string of the hub connection (`/hubs/combat?lang=es`), default `en`. REST uses `?lang=`, then `Accept-Language`, then `en`.

---

### Task 1: SRD import — data model, importer and `srd-monsters.json`

**Files:**
- Create: `src/DmCompanionLive.Infrastructure/Srd/SrdMonster.cs`
- Create: `tools/DmCompanionLive.SrdImport/DmCompanionLive.SrdImport.csproj`
- Create: `tools/DmCompanionLive.SrdImport/Program.cs`
- Create: `tools/DmCompanionLive.SrdImport/Csv.cs`
- Create: `tools/DmCompanionLive.SrdImport/Slugs.cs`
- Create: `tools/DmCompanionLive.SrdImport/EnglishStatBlock.cs`
- Create: `tools/DmCompanionLive.SrdImport/SpanishPack.cs`
- Create: `tools/DmCompanionLive.SrdImport/MonsterMerger.cs`
- Create: `tests/DmCompanionLive.Srd.Tests/DmCompanionLive.Srd.Tests.csproj`
- Create: `tests/DmCompanionLive.Srd.Tests/Import/CsvTests.cs`
- Create: `tests/DmCompanionLive.Srd.Tests/Import/SlugsTests.cs`
- Create: `tests/DmCompanionLive.Srd.Tests/Import/EnglishStatBlockTests.cs`
- Create: `tests/DmCompanionLive.Srd.Tests/Import/SpanishPackTests.cs`
- Create: `tests/DmCompanionLive.Srd.Tests/Import/MonsterMergerTests.cs`
- Modify: `DmCompanionLive.slnx`
- Generate: `src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json`

**Interfaces:**
- Produces (namespace `DmCompanionLive.Infrastructure.Srd`): `enum SrdLanguage { En, Es }`; `enum StatBlockSectionKind { Traits, Actions, BonusActions, Reactions, LegendaryActions }`; records `LocalizedText(string En, string Es)` with `For(SrdLanguage)`, `StatBlockDetail(string Label, string Text)`, `StatBlockEntry(string? Name, string Text)`, `StatBlockSection(StatBlockSectionKind Kind, IReadOnlyList<StatBlockEntry> Entries)`, `StatBlock(string Subtitle, IReadOnlyList<StatBlockDetail> Details, IReadOnlyList<StatBlockSection> Sections)`, `LocalizedStatBlock(StatBlock En, StatBlock Es)` with `For(SrdLanguage)`, `SrdMonster(string Slug, LocalizedText Name, string Type, IReadOnlyList<string> Sizes, string Cr, int Ac, int HpAverage, string HpFormula, int InitiativeBonus, int DexModifier, IReadOnlyList<int> Abilities, LocalizedStatBlock StatBlock)`; `static class SrdJson { JsonSerializerOptions Options }`.
- Produces: the committed file `src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json` (330 monsters sorted by slug).

- [ ] **Step 1: Add the data model**

`src/DmCompanionLive.Infrastructure/Srd/SrdMonster.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmCompanionLive.Infrastructure.Srd;

public enum SrdLanguage
{
    En,
    Es,
}

/// <summary>Language-neutral section of a stat block; clients translate the title.</summary>
public enum StatBlockSectionKind
{
    Traits,
    Actions,
    BonusActions,
    Reactions,
    LegendaryActions,
}

public sealed record LocalizedText(string En, string Es)
{
    public string For(SrdLanguage language) => language == SrdLanguage.Es ? Es : En;
}

/// <summary>A labelled line such as "Senses" or "Idiomas", in the stat block's own language.</summary>
public sealed record StatBlockDetail(string Label, string Text);

/// <summary>A trait or action. <see cref="Name"/> is null for untitled text (e.g. the legendary actions intro).</summary>
public sealed record StatBlockEntry(string? Name, string Text);

public sealed record StatBlockSection(StatBlockSectionKind Kind, IReadOnlyList<StatBlockEntry> Entries);

/// <summary>The descriptive part of a stat block in one language, as plain text.</summary>
public sealed record StatBlock(
    string Subtitle,
    IReadOnlyList<StatBlockDetail> Details,
    IReadOnlyList<StatBlockSection> Sections);

public sealed record LocalizedStatBlock(StatBlock En, StatBlock Es)
{
    public StatBlock For(SrdLanguage language) => language == SrdLanguage.Es ? Es : En;
}

/// <summary>
/// An SRD 5.2.1 monster. Numbers are language-neutral; <see cref="Type"/> and <see cref="Sizes"/> are
/// English keys (e.g. "Aberration", "Large") that clients translate.
/// </summary>
public sealed record SrdMonster(
    string Slug,
    LocalizedText Name,
    string Type,
    IReadOnlyList<string> Sizes,
    string Cr,
    int Ac,
    int HpAverage,
    string HpFormula,
    int InitiativeBonus,
    int DexModifier,
    IReadOnlyList<int> Abilities,
    LocalizedStatBlock StatBlock);

/// <summary>The format of <c>srd-monsters.json</c>, shared by the importer and the catalogue.</summary>
public static class SrdJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        // Keep accents readable in the committed file; it is data, never HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
```

- [ ] **Step 2: Create the tool and test projects**

`tools/DmCompanionLive.SrdImport/DmCompanionLive.SrdImport.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DmCompanionLive.Infrastructure\DmCompanionLive.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

`tests/DmCompanionLive.Srd.Tests/DmCompanionLive.Srd.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\DmCompanionLive.Infrastructure\DmCompanionLive.Infrastructure.csproj" />
    <ProjectReference Include="..\..\tools\DmCompanionLive.SrdImport\DmCompanionLive.SrdImport.csproj" />
  </ItemGroup>
</Project>
```

Replace `DmCompanionLive.slnx` with:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/DmCompanionLive.Api/DmCompanionLive.Api.csproj" />
    <Project Path="src/DmCompanionLive.Domain/DmCompanionLive.Domain.csproj" />
    <Project Path="src/DmCompanionLive.Infrastructure/DmCompanionLive.Infrastructure.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/DmCompanionLive.Api.IntegrationTests/DmCompanionLive.Api.IntegrationTests.csproj" />
    <Project Path="tests/DmCompanionLive.Domain.Tests/DmCompanionLive.Domain.Tests.csproj" />
    <Project Path="tests/DmCompanionLive.Srd.Tests/DmCompanionLive.Srd.Tests.csproj" />
  </Folder>
  <Folder Name="/tools/">
    <Project Path="tools/DmCompanionLive.SrdImport/DmCompanionLive.SrdImport.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 3: Write the failing importer tests**

`tests/DmCompanionLive.Srd.Tests/Import/CsvTests.cs`:

```csharp
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class CsvTests
{
    [Fact]
    public void Quoted_fields_keep_commas_quotes_and_line_breaks()
    {
        var text = "name,full_text\r\nAboleth,\"Line one, still one\nLine \"\"two\"\"\"\nGoblin,plain\n";

        var rows = Csv.Read(new StringReader(text));

        rows.Should().HaveCount(2);
        rows[0]["name"].Should().Be("Aboleth");
        rows[0]["full_text"].Should().Be("Line one, still one\nLine \"two\"");
        rows[1]["full_text"].Should().Be("plain");
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_fields_is_rejected()
    {
        var act = () => Csv.Read(new StringReader("a,b\n1,2,3\n"));

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void An_unterminated_quote_is_rejected()
    {
        var act = () => Csv.Read(new StringReader("a\n\"open\n"));

        act.Should().Throw<FormatException>();
    }
}
```

`tests/DmCompanionLive.Srd.Tests/Import/SlugsTests.cs`:

```csharp
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class SlugsTests
{
    [Theory]
    [InlineData("Aboleth", "aboleth")]
    [InlineData("Adult Red Dragon", "adult-red-dragon")]
    [InlineData("Will-o'-Wisp", "will-o-wisp")]
    [InlineData("  Swarm of Rats ", "swarm-of-rats")]
    public void Slugs_are_lower_case_kebab_from_the_english_name(string name, string slug) =>
        Slugs.From(name).Should().Be(slug);
}
```

`tests/DmCompanionLive.Srd.Tests/Import/EnglishStatBlockTests.cs`:

```csharp
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class EnglishStatBlockTests
{
    private const string GoblinWarrior = """
        Goblin Warrior
        Small Fey (Goblinoid), Chaotic Neutral
        AC 15		                   Initiative +2 (12)
        HP 10 (3d6)
        Speed 30 ft.
        MOD SAVE            MOD SAVE           MOD SAVE
        Str 8 −1 −1          Dex 15 +2 +2        Con 10 +0 +0
        Int 10 +0 +0         Wis 8 −1 −1         Cha 8 −1 −1
        Skills Stealth +6
        Gear Leather Armor, Scimitar, Shield, Shortbow
        Senses Darkvision 60 ft.; Passive Perception 9
        Languages Common, Goblin
        CR 1/4 (XP 50; PB +2)
        Actions
        Scimitar. Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6
        + 2) Slashing damage, plus 2 (1d4) Slashing damage if
        the attack roll had Advantage.
        Shortbow. Ranged Attack Roll: +4, range 80/320 ft.
        Hit: 5 (1d6 + 2) Piercing damage, plus 2 (1d4) Piercing
        damage if the attack roll had Advantage.
        Bonus Actions
        Nimble Escape. The goblin takes the Disengage or
        Hide action.
        """;

    private const string Aboleth = """
        Aboleth
        Large Aberration, Lawful Evil
        AC 17
        HP 150 (20d10 + 40)
        CR 10 (XP 5,900, or 7,200 in lair; PB +4)
        Traits
        Legendary Resistance (3/Day, or 4/Day in Lair). If
        the aboleth fails a saving throw, it can choose to suc-
        ceed instead.
        Legendary Actions
        Legendary Action Uses: 3 (4 in Lair). Immediately after
        another creature’s turn, the aboleth can expend a use.
        Lash. The aboleth makes one Tentacle attack.
        """;

    private static Dictionary<string, string> Row(string name, string fullText, string speed = "NA") => new()
    {
        ["name"] = name,
        ["full_text"] = fullText,
        ["speed"] = speed,
        ["skills"] = name == "Goblin Warrior" ? "Stealth +6" : "NA",
        ["resistances"] = "NA",
        ["vulnerabilities"] = "NA",
        ["immunities"] = "NA",
        ["gear"] = name == "Goblin Warrior" ? "Leather Armor, Scimitar, Shield, Shortbow" : "NA",
        ["senses"] = name == "Goblin Warrior" ? "Darkvision 60 ft.; Passive Perception 9" : "NA",
        ["languages"] = name == "Goblin Warrior" ? "Common, Goblin" : "NA",
    };

    [Fact]
    public void Subtitle_details_and_sections_are_read_from_the_text_and_columns()
    {
        var block = EnglishStatBlock.Parse(Row("Goblin Warrior", GoblinWarrior, "Speed 30 ft."));

        block.Subtitle.Should().Be("Small Fey (Goblinoid), Chaotic Neutral");
        block.Details.Should().Equal(
            new StatBlockDetail("Speed", "30 ft."),
            new StatBlockDetail("Skills", "Stealth +6"),
            new StatBlockDetail("Gear", "Leather Armor, Scimitar, Shield, Shortbow"),
            new StatBlockDetail("Senses", "Darkvision 60 ft.; Passive Perception 9"),
            new StatBlockDetail("Languages", "Common, Goblin"),
            new StatBlockDetail("CR", "1/4 (XP 50; PB +2)"));
        block.Sections.Select(s => s.Kind).Should().Equal(StatBlockSectionKind.Actions, StatBlockSectionKind.BonusActions);
        block.Sections[0].Entries.Should().Equal(
            new StatBlockEntry(
                "Scimitar",
                "Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6 + 2) Slashing damage, plus 2 (1d4) Slashing damage if the attack roll had Advantage."),
            new StatBlockEntry(
                "Shortbow",
                "Ranged Attack Roll: +4, range 80/320 ft. Hit: 5 (1d6 + 2) Piercing damage, plus 2 (1d4) Piercing damage if the attack roll had Advantage."));
        block.Sections[1].Entries.Should().Equal(
            new StatBlockEntry("Nimble Escape", "The goblin takes the Disengage or Hide action."));
    }

    [Fact]
    public void Hyphenated_line_breaks_are_joined_and_untitled_text_keeps_no_name()
    {
        var block = EnglishStatBlock.Parse(Row("Aboleth", Aboleth));

        block.Details.Should().Equal(new StatBlockDetail("CR", "10 (XP 5,900, or 7,200 in lair; PB +4)"));
        block.Sections[0].Entries.Should().Equal(new StatBlockEntry(
            "Legendary Resistance (3/Day, or 4/Day in Lair)",
            "If the aboleth fails a saving throw, it can choose to succeed instead."));
        block.Sections[1].Entries.Should().Equal(
            new StatBlockEntry(
                null,
                "Legendary Action Uses: 3 (4 in Lair). Immediately after another creature’s turn, the aboleth can expend a use."),
            new StatBlockEntry("Lash", "The aboleth makes one Tentacle attack."));
    }

    [Fact]
    public void A_stat_block_without_a_cr_line_is_rejected()
    {
        var act = () => EnglishStatBlock.Parse(Row("Broken", "Broken\nTiny Beast\nActions\nBite. It bites."));

        act.Should().Throw<FormatException>().WithMessage("*Broken*");
    }
}
```

`tests/DmCompanionLive.Srd.Tests/Import/SpanishPackTests.cs`:

```csharp
using System.Text;
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class SpanishPackTests
{
    internal const string Pack = """
        {
          "header": { "source": "compendio-srd", "lang": "es" },
          "entries": [
            {
              "key": "srd:criaturas/guerrero-goblin",
              "name": "Guerrero goblin",
              "nameEn": "Goblin Warrior",
              "creature": {
                "ac": 15, "hp": 10, "hitDice": "3d6", "initiativeBonus": 2,
                "abilities": [8, 15, 10, 10, 8, 8], "modifiers": [-1, 2, 0, 0, -1, -1],
                "cr": "1/4", "sizeKeys": ["Small"], "typeKey": "Fey",
                "typeLine": "Feérico pequeño (goblinoide), caótico neutral",
                "fields": [
                  { "label": "CA", "value": [{ "text": "15", "style": "None" }] },
                  { "label": "Iniciativa", "value": [{ "text": "+2 (12)", "style": "None" }] },
                  { "label": "PG", "value": [{ "text": "10 (3d6)", "style": "None" }] },
                  { "label": "Velocidad", "value": [{ "text": "9 m", "style": "None" }] },
                  { "label": "VD", "value": [{ "text": "1/4 (50 PX; BC +2)", "style": "None" }] }
                ],
                "sections": [
                  {
                    "key": "acciones", "title": "Acciones",
                    "items": [
                      {
                        "name": "Cimitarra",
                        "blocks": [
                          { "kind": "Paragraph", "text": [
                            { "text": "Tirada de ataque cuerpo a cuerpo:", "style": "Italic" },
                            { "text": " +4, alcance 1,5 m.", "style": "None" } ] },
                          { "kind": "ListItem", "text": [ { "text": "Ventaja: +2 (1d4).", "style": "None" } ] }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
          ]
        }
        """;

    internal static IReadOnlyList<PackEntry> Entries() =>
        SpanishPack.Read(new MemoryStream(Encoding.UTF8.GetBytes(Pack)));

    [Fact]
    public void The_pack_is_read_with_its_numbers_and_english_name()
    {
        var entry = Entries().Should().ContainSingle().Subject;

        entry.Name.Should().Be("Guerrero goblin");
        entry.NameEn.Should().Be("Goblin Warrior");
        entry.Creature.Ac.Should().Be(15);
        entry.Creature.HitDice.Should().Be("3d6");
        entry.Creature.Modifiers[1].Should().Be(2);
    }

    [Fact]
    public void The_stat_block_skips_numeric_fields_and_flattens_runs_to_plain_text()
    {
        var block = SpanishPack.ToStatBlock(Entries()[0]);

        block.Subtitle.Should().Be("Feérico pequeño (goblinoide), caótico neutral");
        block.Details.Should().Equal(
            new StatBlockDetail("Velocidad", "9 m"),
            new StatBlockDetail("VD", "1/4 (50 PX; BC +2)"));
        block.Sections.Should().ContainSingle().Which.Kind.Should().Be(StatBlockSectionKind.Actions);
        block.Sections[0].Entries.Should().Equal(new StatBlockEntry(
            "Cimitarra",
            "Tirada de ataque cuerpo a cuerpo: +4, alcance 1,5 m.\n• Ventaja: +2 (1d4)."));
    }
}
```

`tests/DmCompanionLive.Srd.Tests/Import/MonsterMergerTests.cs`:

```csharp
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

namespace DmCompanionLive.Srd.Tests.Import;

public class MonsterMergerTests
{
    private static Dictionary<string, string> EnglishRow(string name, string ac = "15") => new()
    {
        ["name"] = name,
        ["ac"] = ac,
        ["hp_number"] = "10",
        ["initiative"] = "2",
        ["speed"] = "Speed 30 ft.",
        ["skills"] = "NA",
        ["resistances"] = "NA",
        ["vulnerabilities"] = "NA",
        ["immunities"] = "NA",
        ["gear"] = "NA",
        ["senses"] = "NA",
        ["languages"] = "NA",
        ["full_text"] = $"{name}\nSmall Fey (Goblinoid), Chaotic Neutral\nCR 1/4 (XP 50; PB +2)\nActions\nScimitar. It slashes.",
    };

    [Fact]
    public void Matching_entries_become_one_bilingual_monster_with_the_spanish_numbers()
    {
        var monster = MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Warrior")], TextWriter.Null)
            .Should().ContainSingle().Subject;

        monster.Slug.Should().Be("goblin-warrior");
        monster.Name.Should().Be(new LocalizedText("Goblin Warrior", "Guerrero goblin"));
        monster.Type.Should().Be("Fey");
        monster.Sizes.Should().Equal("Small");
        monster.Cr.Should().Be("1/4");
        (monster.Ac, monster.HpAverage, monster.HpFormula).Should().Be((15, 10, "3d6"));
        (monster.InitiativeBonus, monster.DexModifier).Should().Be((2, 2));
        monster.Abilities.Should().Equal(8, 15, 10, 10, 8, 8);
        monster.StatBlock.En.Sections[0].Entries[0].Name.Should().Be("Scimitar");
        monster.StatBlock.Es.Sections[0].Entries[0].Name.Should().Be("Cimitarra");
    }

    [Fact]
    public void A_creature_missing_from_either_source_stops_the_import()
    {
        var act = () => MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Boss")], TextWriter.Null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Goblin Warrior*Goblin Boss*");
    }

    [Fact]
    public void Disagreeing_numbers_are_reported_and_the_spanish_value_wins()
    {
        var warnings = new StringWriter();

        var monster = MonsterMerger.Merge(SpanishPackTests.Entries(), [EnglishRow("Goblin Warrior", ac: "16")], warnings)[0];

        monster.Ac.Should().Be(15);
        warnings.ToString().Should().Contain("Goblin Warrior: AC");
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Srd.Tests`
Expected: build FAILS — `Csv`, `Slugs`, `EnglishStatBlock`, `SpanishPack`, `PackEntry`, `MonsterMerger` do not exist.

- [ ] **Step 5: Implement the importer**

`tools/DmCompanionLive.SrdImport/Csv.cs`:

```csharp
using System.Text;

namespace DmCompanionLive.SrdImport;

/// <summary>Minimal RFC 4180 reader: quoted fields may hold commas, doubled quotes and line breaks.</summary>
public static class Csv
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(TextReader reader)
    {
        var rows = ReadRows(reader).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0];
        return rows.Skip(1).Select(row =>
        {
            if (row.Count != header.Count)
            {
                throw new FormatException($"Expected {header.Count} fields, found {row.Count}.");
            }

            return (IReadOnlyDictionary<string, string>)header.Zip(row).ToDictionary(p => p.First, p => p.Second);
        }).ToList();
    }

    private static IEnumerable<List<string>> ReadRows(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var pending = false;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var ch = (char)next;
            pending = true;
            if (inQuotes)
            {
                if (ch != '"')
                {
                    field.Append(ch);
                }
                else if (reader.Peek() == '"')
                {
                    field.Append('"');
                    reader.Read();
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
                pending = false;
            }
            else if (ch != '\r')
            {
                field.Append(ch);
            }
        }

        if (inQuotes)
        {
            throw new FormatException("Unterminated quoted field.");
        }

        if (pending)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
```

`tools/DmCompanionLive.SrdImport/Slugs.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace DmCompanionLive.SrdImport;

public static class Slugs
{
    /// <summary>"Will-o'-Wisp" → "will-o-wisp": lower-case ASCII letters and digits joined by single hyphens.</summary>
    public static string From(string englishName)
    {
        var builder = new StringBuilder();
        foreach (var ch in englishName.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark
                && builder.Length > 0
                && builder[builder.Length - 1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
```

`tools/DmCompanionLive.SrdImport/EnglishStatBlock.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

/// <summary>
/// Reads the English stat block of a TidyTuesday row. <c>full_text</c> comes from the SRD PDF: lines are
/// wrapped (sometimes mid-word with a hyphen) and each trait or action starts with "Name. ".
/// </summary>
public static class EnglishStatBlock
{
    private static readonly Dictionary<string, StatBlockSectionKind> Headings = new(StringComparer.Ordinal)
    {
        ["Traits"] = StatBlockSectionKind.Traits,
        ["Actions"] = StatBlockSectionKind.Actions,
        ["Bonus Actions"] = StatBlockSectionKind.BonusActions,
        ["Reactions"] = StatBlockSectionKind.Reactions,
        ["Legendary Actions"] = StatBlockSectionKind.LegendaryActions,
    };

    private static readonly (string Column, string Label)[] DetailColumns =
    [
        ("speed", "Speed"),
        ("skills", "Skills"),
        ("resistances", "Resistances"),
        ("vulnerabilities", "Vulnerabilities"),
        ("immunities", "Immunities"),
        ("gear", "Gear"),
        ("senses", "Senses"),
        ("languages", "Languages"),
    ];

    // "Scimitar. Melee Attack Roll: …" — a capitalised name without '.' or ':' followed by more text.
    private static readonly Regex EntryStart = new(
        @"^(?<name>[A-Z][^.:]{0,80})\.\s+(?<rest>\S.*)$", RegexOptions.CultureInvariant);

    public static StatBlock Parse(IReadOnlyDictionary<string, string> row)
    {
        var name = row["name"];
        var lines = row["full_text"]
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var crIndex = lines.FindIndex(l => l.StartsWith("CR ", StringComparison.Ordinal));
        if (lines.Count < 2 || crIndex < 0)
        {
            throw new FormatException($"{name}: the stat block has no CR line.");
        }

        var details = new List<StatBlockDetail>();
        foreach (var (column, label) in DetailColumns)
        {
            var value = row[column].Trim();
            if (value is "" or "NA")
            {
                continue;
            }

            if (column == "speed" && value.StartsWith("Speed ", StringComparison.Ordinal))
            {
                value = value["Speed ".Length..];
            }

            details.Add(new StatBlockDetail(label, value));
        }

        details.Add(new StatBlockDetail("CR", lines[crIndex]["CR ".Length..]));
        return new StatBlock(lines[1], details, ParseSections(lines.Skip(crIndex + 1), name));
    }

    private static List<StatBlockSection> ParseSections(IEnumerable<string> lines, string monster)
    {
        var sections = new List<StatBlockSection>();
        List<StatBlockEntry>? entries = null;
        string? entryName = null;
        var text = new StringBuilder();

        void Flush()
        {
            if (entries is not null && text.Length > 0)
            {
                entries.Add(new StatBlockEntry(entryName, text.ToString()));
            }

            entryName = null;
            text.Clear();
        }

        foreach (var line in lines)
        {
            if (Headings.TryGetValue(line, out var kind))
            {
                Flush();
                entries = [];
                sections.Add(new StatBlockSection(kind, entries));
                continue;
            }

            if (entries is null)
            {
                throw new FormatException($"{monster}: text before the first section: '{line}'.");
            }

            var start = EntryStart.Match(line);
            if (start.Success && (text.Length == 0 || text[text.Length - 1] == '.'))
            {
                Flush();
                entryName = start.Groups["name"].Value;
                text.Append(start.Groups["rest"].Value);
            }
            else
            {
                Append(text, line);
            }
        }

        Flush();
        return sections;
    }

    /// <summary>Joins a wrapped line; "suc-" + "ceed" becomes "succeed".</summary>
    private static void Append(StringBuilder text, string line)
    {
        if (text.Length == 0)
        {
            text.Append(line);
        }
        else if (text[text.Length - 1] == '-' && char.IsLower(line[0]))
        {
            text.Length--;
            text.Append(line);
        }
        else
        {
            text.Append(' ').Append(line);
        }
    }
}
```

`tools/DmCompanionLive.SrdImport/SpanishPack.cs`:

```csharp
using System.Text.Json;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

public sealed record PackFile(IReadOnlyList<PackEntry> Entries);

public sealed record PackEntry(string Name, string NameEn, PackCreature Creature);

public sealed record PackCreature(
    int Ac,
    int Hp,
    string HitDice,
    int InitiativeBonus,
    IReadOnlyList<int> Abilities,
    IReadOnlyList<int> Modifiers,
    string Cr,
    IReadOnlyList<string> SizeKeys,
    string TypeKey,
    string TypeLine,
    IReadOnlyList<PackField> Fields,
    IReadOnlyList<PackSection> Sections);

public sealed record PackField(string Label, IReadOnlyList<PackRun> Value);

public sealed record PackRun(string Text);

public sealed record PackSection(string Key, IReadOnlyList<PackItem> Items);

public sealed record PackItem(string Name, IReadOnlyList<PackBlock> Blocks);

public sealed record PackBlock(string Kind, IReadOnlyList<PackRun> Text);

/// <summary>Reads the Spanish creature pack of dm-companion (generated from compendio-srd).</summary>
public static class SpanishPack
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // AC, initiative and HP are shown from the numbers, not repeated as text.
    private static readonly HashSet<string> NumericFields = new(StringComparer.Ordinal) { "CA", "Iniciativa", "PG" };

    private static readonly Dictionary<string, StatBlockSectionKind> SectionKinds = new(StringComparer.Ordinal)
    {
        ["rasgos"] = StatBlockSectionKind.Traits,
        ["acciones"] = StatBlockSectionKind.Actions,
        ["adicionales"] = StatBlockSectionKind.BonusActions,
        ["reacciones"] = StatBlockSectionKind.Reactions,
        ["legendarias"] = StatBlockSectionKind.LegendaryActions,
    };

    public static IReadOnlyList<PackEntry> Read(Stream stream) =>
        (JsonSerializer.Deserialize<PackFile>(stream, Options) ?? throw new FormatException("The pack is empty.")).Entries;

    public static StatBlock ToStatBlock(PackEntry entry)
    {
        var creature = entry.Creature;
        var details = creature.Fields
            .Where(f => !NumericFields.Contains(f.Label))
            .Select(f => new StatBlockDetail(f.Label, Join(f.Value)))
            .ToList();
        var sections = creature.Sections
            .Select(s => new StatBlockSection(
                SectionKinds.TryGetValue(s.Key, out var kind)
                    ? kind
                    : throw new FormatException($"{entry.NameEn}: unknown section '{s.Key}'."),
                s.Items.Select(i => new StatBlockEntry(i.Name, string.Join('\n', i.Blocks.Select(Text)))).ToList()))
            .ToList();
        return new StatBlock(creature.TypeLine, details, sections);
    }

    private static string Text(PackBlock block) =>
        block.Kind == "ListItem" ? "• " + Join(block.Text) : Join(block.Text);

    private static string Join(IEnumerable<PackRun> runs) => string.Concat(runs.Select(r => r.Text)).Trim();
}
```

`tools/DmCompanionLive.SrdImport/MonsterMerger.cs`:

```csharp
using System.Globalization;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.SrdImport;

public static class MonsterMerger
{
    /// <summary>
    /// Joins both sources by English name. Numbers come from the Spanish pack; the English row gives the
    /// English stat block. Any creature missing from one side stops the import.
    /// </summary>
    public static IReadOnlyList<SrdMonster> Merge(
        IReadOnlyList<PackEntry> spanish,
        IReadOnlyList<IReadOnlyDictionary<string, string>> english,
        TextWriter warnings)
    {
        var englishBySlug = english.ToDictionary(r => Slugs.From(r["name"]), StringComparer.Ordinal);
        var spanishSlugs = spanish.Select(e => Slugs.From(e.NameEn)).ToList();
        if (spanishSlugs.Distinct(StringComparer.Ordinal).Count() != spanishSlugs.Count)
        {
            throw new InvalidOperationException("Two Spanish entries share an English name.");
        }

        var withoutEnglish = spanish.Where(e => !englishBySlug.ContainsKey(Slugs.From(e.NameEn))).Select(e => e.NameEn).ToList();
        var withoutSpanish = english.Where(r => !spanishSlugs.Contains(Slugs.From(r["name"]))).Select(r => r["name"]).ToList();
        if (withoutEnglish.Count > 0 || withoutSpanish.Count > 0)
        {
            throw new InvalidOperationException(
                $"The sources do not match. Without English: [{string.Join(", ", withoutEnglish)}]. "
                + $"Without Spanish: [{string.Join(", ", withoutSpanish)}].");
        }

        return spanish
            .Select(entry =>
            {
                var slug = Slugs.From(entry.NameEn);
                var row = englishBySlug[slug];
                var creature = entry.Creature;
                Check(warnings, entry.NameEn, "AC", creature.Ac, row["ac"]);
                Check(warnings, entry.NameEn, "HP", creature.Hp, row["hp_number"]);
                Check(warnings, entry.NameEn, "initiative", creature.InitiativeBonus, row["initiative"]);
                return new SrdMonster(
                    slug,
                    new LocalizedText(row["name"], entry.Name),
                    creature.TypeKey,
                    creature.SizeKeys,
                    creature.Cr,
                    creature.Ac,
                    creature.Hp,
                    creature.HitDice,
                    creature.InitiativeBonus,
                    creature.Modifiers[1],
                    creature.Abilities,
                    new LocalizedStatBlock(EnglishStatBlock.Parse(row), SpanishPack.ToStatBlock(entry)));
            })
            .OrderBy(m => m.Slug, StringComparer.Ordinal)
            .ToList();
    }

    private static void Check(TextWriter warnings, string monster, string field, int spanish, string english)
    {
        if (!int.TryParse(english, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value != spanish)
        {
            warnings.WriteLine($"{monster}: {field} is {spanish} in Spanish and '{english}' in English; using {spanish}.");
        }
    }
}
```

`tools/DmCompanionLive.SrdImport/Program.cs`:

```csharp
using System.Text.Json;
using DmCompanionLive.Infrastructure.Srd;
using DmCompanionLive.SrdImport;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: SrdImport <criaturas.json> <monsters.csv> <output.json>");
    return 1;
}

IReadOnlyList<PackEntry> spanish;
await using (var pack = File.OpenRead(args[0]))
{
    spanish = SpanishPack.Read(pack);
}

IReadOnlyList<IReadOnlyDictionary<string, string>> english;
using (var csv = new StreamReader(args[1]))
{
    english = Csv.Read(csv);
}

var monsters = MonsterMerger.Merge(spanish, english, Console.Error);
await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(monsters, SrdJson.Options) + "\n");
Console.WriteLine($"Wrote {monsters.Count} monsters to {args[2]}.");
return 0;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Srd.Tests`
Expected: PASS (all tests in `Import/`).

- [ ] **Step 7: Generate `srd-monsters.json`**

The Spanish pack lives in the dm-companion repository (local clone at `~/Personal/dm-companion-main`).

```bash
WORK="${TMPDIR:-/tmp}/srd-import" && mkdir -p "$WORK"
git -C ~/Personal/dm-companion-main show \
  b95f1e65795d752d8ab167c60ff55751b38ec75b:src/DmCompanion.Core/Glossary/Packs/criaturas.json > "$WORK/criaturas.json"
curl -sSfL -o "$WORK/monsters.csv" \
  https://raw.githubusercontent.com/rfordatascience/tidytuesday/97a250500c85463e590b23f9e195c58232a04584/data/2025/2025-05-27/monsters.csv
echo "0f3ba32d721af74e66a92bbb2d5eae6cb394796c2887da81c88af571a82cedf4  $WORK/monsters.csv" | sha256sum -c
~/.claude/bin/heavy.sh dotnet run --project tools/DmCompanionLive.SrdImport -- \
  "$WORK/criaturas.json" "$WORK/monsters.csv" src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json
```

Expected: `Wrote 330 monsters to src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json.` Warnings about AC/HP/initiative disagreements may appear on stderr; copy them into the task report (they are kept, the Spanish value wins). If the import throws "The sources do not match", stop and report the listed names.

Spot-check the file: `grep -c '"slug"' src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json` prints `330`, and the `goblin-warrior` entry has `"es": "Guerrero goblin"`, `"hpFormula": "3d6"` and an English `Scimitar` action whose text has no line breaks.

- [ ] **Step 8: Commit**

```bash
git add src/DmCompanionLive.Infrastructure/Srd tools tests/DmCompanionLive.Srd.Tests DmCompanionLive.slnx
git commit -m "feat: import the SRD 5.2.1 monsters in English and Spanish

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: SRD catalogue in memory

**Files:**
- Create: `src/DmCompanionLive.Infrastructure/Srd/SrdCatalog.cs`
- Create: `src/DmCompanionLive.Infrastructure/Srd/SearchText.cs`
- Modify: `src/DmCompanionLive.Infrastructure/DmCompanionLive.Infrastructure.csproj` (embed the JSON)
- Modify: `src/DmCompanionLive.Infrastructure/DependencyInjection.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs` (load the catalogue at startup)
- Test: `tests/DmCompanionLive.Srd.Tests/Catalog/TestMonsters.cs`, `tests/DmCompanionLive.Srd.Tests/Catalog/SrdCatalogTests.cs`, `tests/DmCompanionLive.Srd.Tests/Catalog/SearchTextTests.cs`

**Interfaces:**
- Consumes: `SrdMonster`, `SrdLanguage`, `SrdJson` (Task 1); `srd-monsters.json` (Task 1).
- Produces: `interface ISrdCatalog { int Count { get; } SrdMonster? Find(string slug); SrdSearchPage Search(string? query, SrdLanguage language, int page); }`; `record SrdSearchPage(IReadOnlyList<SrdMonster> Items, int Page, int Total)`; `class SrdCatalog : ISrdCatalog` with `const int PageSize = 20`, `static SrdCatalog LoadEmbedded()`, constructor `SrdCatalog(IEnumerable<SrdMonster>)`; `static class SearchText { static string Normalize(string text) }`; `ISrdCatalog` registered as a singleton by `AddInfrastructure`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Srd.Tests/Catalog/TestMonsters.cs`:

```csharp
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

internal static class TestMonsters
{
    private static readonly StatBlock Empty = new(string.Empty, [], []);

    public static SrdMonster Create(string slug, string en, string es) => new(
        slug, new LocalizedText(en, es), "Monstrosity", ["Large"], "3", 13, 59, "7d10+21", 1, 1,
        [20, 12, 17, 3, 12, 7], new LocalizedStatBlock(Empty, Empty));
}
```

`tests/DmCompanionLive.Srd.Tests/Catalog/SearchTextTests.cs`:

```csharp
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SearchTextTests
{
    [Theory]
    [InlineData("  Dragón   ROJO ", "dragon rojo")]
    [InlineData("Ñu", "nu")]
    [InlineData("", "")]
    public void Normalising_removes_accents_case_and_extra_spaces(string text, string expected) =>
        SearchText.Normalize(text).Should().Be(expected);
}
```

`tests/DmCompanionLive.Srd.Tests/Catalog/SrdCatalogTests.cs`:

```csharp
using DmCompanionLive.Infrastructure.Srd;
using static DmCompanionLive.Srd.Tests.Catalog.TestMonsters;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SrdCatalogTests
{
    [Fact]
    public void Search_ignores_case_and_accents_and_matches_either_language()
    {
        var catalog = new SrdCatalog([
            Create("adult-red-dragon", "Adult Red Dragon", "Dragón rojo adulto"),
            Create("owlbear", "Owlbear", "Oso lechuza"),
        ]);

        catalog.Search("DRAGON ROJO", SrdLanguage.Es, 1).Items.Select(m => m.Slug).Should().Equal("adult-red-dragon");
        catalog.Search("oso", SrdLanguage.En, 1).Items.Select(m => m.Slug).Should().Equal("owlbear");
        catalog.Search("kraken", SrdLanguage.En, 1).Items.Should().BeEmpty();
    }

    [Fact]
    public void Exact_matches_come_first_then_prefixes_then_substrings()
    {
        var catalog = new SrdCatalog([
            Create("hobgoblin", "Hobgoblin", "Hobgoblin"),
            Create("goblin-boss", "Goblin Boss", "Jefe goblin"),
            Create("goblin", "Goblin", "Goblin"),
        ]);

        catalog.Search("goblin", SrdLanguage.En, 1).Items.Select(m => m.Slug)
            .Should().Equal("goblin", "goblin-boss", "hobgoblin");
    }

    [Fact]
    public void Ties_are_ordered_by_the_name_in_the_requested_language()
    {
        var catalog = new SrdCatalog([
            Create("wolf", "Wolf", "Lobo"),
            Create("brown-bear", "Brown Bear", "Oso pardo"),
        ]);

        catalog.Search(null, SrdLanguage.En, 1).Items.Select(m => m.Slug).Should().Equal("brown-bear", "wolf");
        catalog.Search(null, SrdLanguage.Es, 1).Items.Select(m => m.Slug).Should().Equal("wolf", "brown-bear");
    }

    [Fact]
    public void Results_come_in_pages_of_twenty()
    {
        var catalog = new SrdCatalog(Enumerable.Range(0, 45).Select(i => Create($"m{i:00}", $"M{i:00}", $"M{i:00}")));

        var third = catalog.Search(string.Empty, SrdLanguage.En, 3);

        third.Total.Should().Be(45);
        third.Page.Should().Be(3);
        third.Items.Select(m => m.Slug).Should().Equal("m40", "m41", "m42", "m43", "m44");
        catalog.Search(string.Empty, SrdLanguage.En, 4).Items.Should().BeEmpty();
    }

    [Fact]
    public void Find_returns_the_monster_or_null()
    {
        var catalog = new SrdCatalog([Create("owlbear", "Owlbear", "Oso lechuza")]);

        catalog.Find("owlbear")!.Name.Es.Should().Be("Oso lechuza");
        catalog.Find("kraken").Should().BeNull();
    }

    [Fact]
    public void Duplicate_slugs_are_rejected()
    {
        var act = () => new SrdCatalog([Create("owlbear", "Owlbear", "Oso lechuza"), Create("owlbear", "Other", "Otro")]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_embedded_catalogue_has_every_srd_monster_in_both_languages()
    {
        var catalog = SrdCatalog.LoadEmbedded();

        catalog.Count.Should().Be(330);
        var all = catalog.Search(null, SrdLanguage.En, 1).Total;
        all.Should().Be(330);
        var goblin = catalog.Find("goblin-warrior")!;
        goblin.Name.Should().Be(new LocalizedText("Goblin Warrior", "Guerrero goblin"));
        (goblin.Ac, goblin.HpAverage, goblin.HpFormula, goblin.InitiativeBonus).Should().Be((15, 10, "3d6", 2));
        goblin.StatBlock.En.Sections.Should().Contain(s => s.Kind == StatBlockSectionKind.Actions);
        goblin.StatBlock.Es.Sections.Should().Contain(s => s.Kind == StatBlockSectionKind.Actions);
        catalog.Search("aboleth", SrdLanguage.Es, 1).Items[0].Slug.Should().Be("aboleth");
    }

    [Fact]
    public void Every_embedded_monster_has_usable_numbers_and_names()
    {
        var catalog = SrdCatalog.LoadEmbedded();
        var monsters = Enumerable.Range(1, 17).SelectMany(page => catalog.Search(null, SrdLanguage.En, page).Items).ToList();

        monsters.Should().HaveCount(330);
        monsters.Should().OnlyContain(m =>
            m.Ac > 0 && m.HpAverage > 0 && m.Name.En.Length > 0 && m.Name.Es.Length > 0
            && m.StatBlock.En.Subtitle.Length > 0 && m.StatBlock.Es.Subtitle.Length > 0
            && m.Abilities.Count == 6);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Srd.Tests --filter "FullyQualifiedName~Catalog"`
Expected: build FAILS — `SrdCatalog`, `SearchText` do not exist.

- [ ] **Step 3: Implement the catalogue**

`src/DmCompanionLive.Infrastructure/Srd/SearchText.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace DmCompanionLive.Infrastructure.Srd;

public static class SearchText
{
    /// <summary>Lower-case, without accents and with single spaces: "  Dragón ROJO" → "dragon rojo".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
```

`src/DmCompanionLive.Infrastructure/Srd/SrdCatalog.cs`:

```csharp
using System.Text.Json;

namespace DmCompanionLive.Infrastructure.Srd;

public sealed record SrdSearchPage(IReadOnlyList<SrdMonster> Items, int Page, int Total);

/// <summary>The read-only SRD bestiary.</summary>
public interface ISrdCatalog
{
    int Count { get; }

    SrdMonster? Find(string slug);

    /// <summary>
    /// Matches the query against the English and Spanish names, ignoring case and accents: exact matches
    /// first, then prefixes, then substrings; ties by name in <paramref name="language"/>. An empty query
    /// lists everything. Pages start at 1.
    /// </summary>
    SrdSearchPage Search(string? query, SrdLanguage language, int page);
}

/// <summary>
/// The SRD bestiary held in memory. About 330 read-only entries and a single replica make a database
/// table, a seeder and Postgres search extensions unnecessary; the price is no typo tolerance.
/// </summary>
public sealed class SrdCatalog : ISrdCatalog
{
    public const int PageSize = 20;
    public const string ResourceName = "DmCompanionLive.Infrastructure.Srd.srd-monsters.json";

    private readonly IReadOnlyList<Indexed> _monsters;
    private readonly Dictionary<string, SrdMonster> _bySlug;

    public SrdCatalog(IEnumerable<SrdMonster> monsters)
    {
        _monsters = monsters
            .Select(m => new Indexed(m, SearchText.Normalize(m.Name.En), SearchText.Normalize(m.Name.Es)))
            .ToList();
        _bySlug = _monsters.ToDictionary(i => i.Monster.Slug, i => i.Monster, StringComparer.Ordinal);
    }

    public int Count => _monsters.Count;

    /// <summary>Loads the embedded <c>srd-monsters.json</c>; a missing or empty resource is a startup error.</summary>
    public static SrdCatalog LoadEmbedded()
    {
        using var stream = typeof(SrdCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        var monsters = JsonSerializer.Deserialize<List<SrdMonster>>(stream, SrdJson.Options);
        if (monsters is null || monsters.Count == 0)
        {
            throw new InvalidOperationException("The SRD monster catalogue is empty.");
        }

        return new SrdCatalog(monsters);
    }

    public SrdMonster? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public SrdSearchPage Search(string? query, SrdLanguage language, int page)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var normalized = SearchText.Normalize(query ?? string.Empty);
        var matches = _monsters
            .Select(m => (Entry: m, Rank: Rank(m, normalized)))
            .Where(x => x.Rank is not null)
            .OrderBy(x => x.Rank)
            .ThenBy(x => language == SrdLanguage.Es ? x.Entry.Es : x.Entry.En, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Monster.Slug, StringComparer.Ordinal)
            .Select(x => x.Entry.Monster)
            .ToList();
        return new SrdSearchPage(matches.Skip((page - 1) * PageSize).Take(PageSize).ToList(), page, matches.Count);
    }

    /// <summary>0 exact, 1 prefix, 2 substring, null no match — the best over both languages.</summary>
    private static int? Rank(Indexed monster, string query)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        int? best = null;
        foreach (var name in new[] { monster.En, monster.Es })
        {
            int? rank = name == query ? 0
                : name.StartsWith(query, StringComparison.Ordinal) ? 1
                : name.Contains(query, StringComparison.Ordinal) ? 2
                : null;
            if (rank < (best ?? int.MaxValue))
            {
                best = rank;
            }
        }

        return best;
    }

    private sealed record Indexed(SrdMonster Monster, string En, string Es);
}
```

Add to `src/DmCompanionLive.Infrastructure/DmCompanionLive.Infrastructure.csproj`, after the existing `ItemGroup`s:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Srd\srd-monsters.json" LogicalName="DmCompanionLive.Infrastructure.Srd.srd-monsters.json" />
  </ItemGroup>
```

In `src/DmCompanionLive.Infrastructure/DependencyInjection.cs`, add `using DmCompanionLive.Infrastructure.Srd;` and register the catalogue in `AddInfrastructure`, before `return services;`:

```csharp
        services.AddSingleton<ISrdCatalog>(_ => SrdCatalog.LoadEmbedded());
```

In `src/DmCompanionLive.Api/Program.cs`, add `using DmCompanionLive.Infrastructure.Srd;` and, right after `var app = builder.Build();`:

```csharp
// Load the SRD catalogue now: a missing or broken resource must stop the app, not its first search.
app.Services.GetRequiredService<ISrdCatalog>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Srd.Tests`
Expected: PASS.

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~HostTests"`
Expected: PASS (the API still starts with the catalogue loaded).

- [ ] **Step 5: Commit**

```bash
git add src/DmCompanionLive.Infrastructure src/DmCompanionLive.Api/Program.cs tests/DmCompanionLive.Srd.Tests
git commit -m "feat: search the SRD monster catalogue in memory

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Domain building blocks — shared names, hit dice formulas, any die

**Files:**
- Create: `src/DmCompanionLive.Domain/Names.cs`
- Create: `src/DmCompanionLive.Domain/Combat/HpFormula.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/IDiceRoller.cs`
- Modify: `src/DmCompanionLive.Domain/Tables/Table.cs`, `src/DmCompanionLive.Domain/Tables/PlayerCharacter.cs`, `src/DmCompanionLive.Domain/Combat/Combatant.cs` (use `Names`)
- Modify: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/TableConfiguration.cs` (`HasMaxLength(Names.MaxLength)`)
- Modify: `src/DmCompanionLive.Api/Features/Combat/RandomDiceRoller.cs`
- Modify: `tests/DmCompanionLive.Domain.Tests/FixedDice.cs`
- Test: `tests/DmCompanionLive.Domain.Tests/NamesTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/HpFormulaTests.cs`, `tests/DmCompanionLive.Srd.Tests/Catalog/SrdFormulaTests.cs`

**Interfaces:**
- Produces: `static class Names { const int MaxLength = 80; static string Validate(string? name) }` (returns the trimmed name, throws `DomainException(InvalidValue)`); `sealed record HpFormula` with `int Count`, `int Sides`, `int Modifier`, `static HpFormula Parse(string? text)`, `int Roll(IDiceRoller dice)`, `ToString()` → `"NdM"`, `"NdM+K"` or `"NdM-K"`; `IDiceRoller.Roll(int sides)` plus default `D20() => Roll(20)`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/NamesTests.cs`:

```csharp
namespace DmCompanionLive.Domain.Tests;

public class NamesTests
{
    [Fact]
    public void A_name_is_trimmed() => Names.Validate("  Lyra ").Should().Be("Lyra");

    [Fact]
    public void Eighty_characters_are_allowed() => Names.Validate(new string('a', 80)).Should().HaveLength(80);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_name_is_invalid(string? name)
    {
        var act = () => Names.Validate(name);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void Eighty_one_characters_are_invalid()
    {
        var act = () => Names.Validate(new string('a', 81));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }
}
```

`tests/DmCompanionLive.Domain.Tests/Combat/HpFormulaTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests.Combat;

public class HpFormulaTests
{
    [Theory]
    [InlineData("2d8+2", 2, 8, 2, "2d8+2")]
    [InlineData("20d10 + 40", 20, 10, 40, "20d10+40")]
    [InlineData("1d4 − 1", 1, 4, -1, "1d4-1")]
    [InlineData("3D6", 3, 6, 0, "3d6")]
    [InlineData("100d20-1000", 100, 20, -1000, "100d20-1000")]
    public void Formulas_are_parsed_and_normalised(string text, int count, int sides, int modifier, string normalised)
    {
        var formula = HpFormula.Parse(text);

        (formula.Count, formula.Sides, formula.Modifier).Should().Be((count, sides, modifier));
        formula.ToString().Should().Be(normalised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("d6")]
    [InlineData("0d6")]
    [InlineData("101d6")]
    [InlineData("2d7")]
    [InlineData("2d6+1001")]
    [InlineData("2d6+")]
    [InlineData("2d6*2")]
    [InlineData("abc")]
    public void Anything_else_is_an_invalid_value(string? text)
    {
        var act = () => HpFormula.Parse(text);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void A_roll_adds_every_die_and_the_modifier() =>
        HpFormula.Parse("2d8+2").Roll(new FixedDice(3, 5)).Should().Be(10);

    [Fact]
    public void A_roll_is_never_below_one() =>
        HpFormula.Parse("1d4-3").Roll(new FixedDice(1)).Should().Be(1);
}
```

`tests/DmCompanionLive.Srd.Tests/Catalog/SrdFormulaTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Srd.Tests.Catalog;

public class SrdFormulaTests
{
    [Fact]
    public void Every_srd_hit_dice_formula_is_a_valid_normalised_HpFormula()
    {
        var catalog = SrdCatalog.LoadEmbedded();
        var monsters = Enumerable.Range(1, 17).SelectMany(page => catalog.Search(null, SrdLanguage.En, page).Items);

        monsters.Should().OnlyContain(m => HpFormula.Parse(m.HpFormula).ToString() == m.HpFormula);
    }
}
```

Replace `tests/DmCompanionLive.Domain.Tests/FixedDice.cs` with:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tests;

/// <summary>Returns the given rolls in order, whatever die is rolled.</summary>
internal sealed class FixedDice(params int[] rolls) : IDiceRoller
{
    private readonly Queue<int> _rolls = new(rolls);

    public int Roll(int sides) => _rolls.Dequeue();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `Names`, `HpFormula`, `IDiceRoller.Roll` do not exist.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Domain/Names.cs`:

```csharp
namespace DmCompanionLive.Domain;

/// <summary>Display names of tables, characters, combatants and monster templates.</summary>
public static class Names
{
    public const int MaxLength = 80;

    /// <summary>The trimmed name, or <see cref="DomainErrorCode.InvalidValue"/> when it is blank or too long.</summary>
    public static string Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxLength} characters.");
        }

        return name.Trim();
    }
}
```

Replace `src/DmCompanionLive.Domain/Combat/IDiceRoller.cs` with:

```csharp
namespace DmCompanionLive.Domain.Combat;

public interface IDiceRoller
{
    /// <summary>A uniformly random number from 1 to <paramref name="sides"/>.</summary>
    int Roll(int sides);

    int D20() => Roll(20);
}
```

`src/DmCompanionLive.Domain/Combat/HpFormula.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace DmCompanionLive.Domain.Combat;

/// <summary>A hit dice formula such as "2d8+2": up to 100 dice of d4–d20 and a modifier of at most ±1000.</summary>
public sealed record HpFormula
{
    public const int MaxDice = 100;
    public const int MaxModifier = 1000;

    private static readonly int[] AllowedSides = [4, 6, 8, 10, 12, 20];
    private static readonly Regex Pattern = new(@"^(\d{1,3})d(\d{1,2})(?:([+-])(\d{1,4}))?$", RegexOptions.CultureInvariant);

    private HpFormula(int count, int sides, int modifier)
    {
        Count = count;
        Sides = sides;
        Modifier = modifier;
    }

    public int Count { get; }
    public int Sides { get; }
    public int Modifier { get; }

    /// <summary>Accepts spaces, upper-case "D" and the Unicode minus sign used by the SRD.</summary>
    public static HpFormula Parse(string? text)
    {
        var compact = (text ?? string.Empty)
            .Replace('−', '-')
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        var match = Pattern.Match(compact);
        if (!match.Success)
        {
            throw Invalid();
        }

        var count = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var sides = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var modifier = match.Groups[3].Success
            ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) * (match.Groups[3].Value == "-" ? -1 : 1)
            : 0;
        if (count is < 1 or > MaxDice || !AllowedSides.Contains(sides) || Math.Abs(modifier) > MaxModifier)
        {
            throw Invalid();
        }

        return new HpFormula(count, sides, modifier);
    }

    /// <summary>Rolls every die and adds the modifier; never less than 1.</summary>
    public int Roll(IDiceRoller dice)
    {
        var total = Modifier;
        for (var i = 0; i < Count; i++)
        {
            total += dice.Roll(Sides);
        }

        return Math.Max(1, total);
    }

    public override string ToString() => Modifier switch
    {
        0 => $"{Count}d{Sides}",
        > 0 => $"{Count}d{Sides}+{Modifier}",
        _ => $"{Count}d{Sides}{Modifier}",
    };

    private static DomainException Invalid() => new(
        DomainErrorCode.InvalidValue,
        $"Hit dice must look like 2d8+2: 1 to {MaxDice} dice of d4, d6, d8, d10, d12 or d20 and a modifier up to ±{MaxModifier}.");
}
```

Use `Names` everywhere a display name is validated:

- `Table.cs`: `public const int MaxNameLength = Names.MaxLength;` and `Rename` becomes `public void Rename(string name) => Name = Names.Validate(name);`.
- `PlayerCharacter.cs`: in `Update`, replace the name check and `Name = name.Trim();` with `var validName = Names.Validate(name);` at the top and `Name = validName;` where the name is assigned (keep the HP/AC check and its message).
- `Combatant.cs`: `public const int MaxNameLength = Names.MaxLength;` and in `Validate` replace the name check with `Names.Validate(name);`.
- `TableConfiguration.cs`: `character.Property(c => c.Name).HasMaxLength(Names.MaxLength).IsRequired();` (add `using DmCompanionLive.Domain;`).

Replace `src/DmCompanionLive.Api/Features/Combat/RandomDiceRoller.cs` with:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

public sealed class RandomDiceRoller : IDiceRoller
{
    public int Roll(int sides) => Random.Shared.Next(1, sides + 1);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS (new and existing tests).

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Srd.Tests`
Expected: PASS, including `Every_srd_hit_dice_formula_is_a_valid_normalised_HpFormula`.

Run: `~/.claude/bin/heavy.sh dotnet build DmCompanionLive.slnx`
Expected: build succeeds with no warnings. The EF model did not change (same max length), so no migration is needed.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "refactor: share name validation, parse hit dice formulas and roll any die

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Combat domain — several monsters, initiative bonus, source and rolled HP

**Files:**
- Create: `src/DmCompanionLive.Domain/Combat/MonsterSource.cs`
- Create: `src/DmCompanionLive.Domain/Combat/MonsterNames.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/Seeds.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/Combatant.cs`
- Modify: `src/DmCompanionLive.Domain/Combat/CombatSession.cs` (`AddMonsters`, `AddMonster`, `RollMonsterInitiative`)
- Modify: `src/DmCompanionLive.Domain/Combat/Views.cs` (`DmCombatantView`)
- Modify: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/CombatSessionConfiguration.cs`
- Create: migration `MonsterSources` under `src/DmCompanionLive.Infrastructure/Persistence/Migrations/`
- Test: `tests/DmCompanionLive.Domain.Tests/Combat/AddMonstersTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/TurnOrderTests.cs`, `tests/DmCompanionLive.Domain.Tests/Combat/CombatEventTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Persistence/MigrationTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs`

**Interfaces:**
- Consumes: `Names`, `HpFormula`, `IDiceRoller.Roll` (Task 3).
- Produces:
  - `enum MonsterSourceKind { None, Srd, Template }`; `sealed record MonsterSource(MonsterSourceKind Kind, string? Id)` with `static MonsterSource None`, `static MonsterSource Srd(string slug)`, `static MonsterSource Template(Guid id)`.
  - `MonsterSeed(string Name, int MaxHp, int Ac, int DexModifier = 0, bool IsHidden = false, MonsterSource? Source = null, int? InitiativeBonus = null, HpFormula? HpFormula = null)`.
  - `Combatant.InitiativeBonus` (`int`), `Combatant.SourceKind` (`MonsterSourceKind`), `Combatant.SourceId` (`string?`); `Combatant.MaxInitiativeBonus = 30`. `Combatant.SourceRef` is removed.
  - `CombatSession.MaxMonstersPerCommand = 20`; `IReadOnlyList<Guid> CombatSession.AddMonsters(Actor actor, MonsterSeed seed, int count, IDiceRoller? hpDice, DateTimeOffset now)`; `Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now)` (wrapper).
  - Log entry type `MonstersAdded` with parameters `combatantId`, `name`, `kind` (first monster), `baseName`, `count`, `combatantIds`.
  - `DmCombatantView` loses `SourceRef` and gains `int InitiativeBonus`, `MonsterSourceKind SourceKind`, `string? SourceId` (in that order, after `ClaimedBy`).

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/Combat/AddMonstersTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Combat;

public class AddMonstersTests
{
    private static readonly MonsterSeed Goblin = new(
        "Goblin Warrior", 10, 15, DexModifier: 2, Source: MonsterSource.Srd("goblin-warrior"),
        InitiativeBonus: 2, HpFormula: HpFormula.Parse("3d6"));

    private static IEnumerable<string> MonsterNamesIn(CombatSession session) =>
        session.Combatants.Where(c => c.Kind == CombatantKind.Monster).Select(c => c.Name);

    [Fact]
    public void A_single_monster_keeps_its_name()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior");
    }

    [Fact]
    public void Several_monsters_are_numbered_after_the_highest_existing_number()
    {
        var session = NewSession();
        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);
        session.AddMonsters(Dm, Goblin, 1, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior", "Goblin Warrior 2", "Goblin Warrior 3", "Goblin Warrior 4");
    }

    [Fact]
    public void Numbering_starts_at_one_and_ignores_longer_names_with_the_same_start()
    {
        var session = NewSession();
        session.AddMonster(Dm, new MonsterSeed("Goblin Warrior Boss", 21, 17), Now);

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);

        MonsterNamesIn(session).Should().Equal("Goblin Warrior Boss", "Goblin Warrior 1", "Goblin Warrior 2");
    }

    [Fact]
    public void Monsters_get_average_hp_unless_dice_are_given()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin, 2, hpDice: null, Now);
        session.AddMonsters(Dm, Goblin, 2, new FixedDice(1, 1, 1, 6, 6, 6), Now);

        session.Combatants.Select(c => (c.MaxHp, c.CurrentHp)).Should().Equal((10, 10), (10, 10), (3, 3), (18, 18));
    }

    [Fact]
    public void Without_a_formula_dice_are_ignored()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { HpFormula = null }, 1, new FixedDice(), Now);

        session.Combatants.Single().MaxHp.Should().Be(10);
    }

    [Fact]
    public void Monsters_keep_their_source_and_initiative_bonus()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { DexModifier = -1, InitiativeBonus = 7 }, 1, hpDice: null, Now);

        var goblin = session.Combatants.Single();
        (goblin.SourceKind, goblin.SourceId).Should().Be((MonsterSourceKind.Srd, "goblin-warrior"));
        (goblin.InitiativeBonus, goblin.DexModifier).Should().Be((7, -1));
    }

    [Fact]
    public void Without_an_initiative_bonus_the_dex_modifier_is_used()
    {
        var session = NewSession();

        session.AddMonster(Dm, new MonsterSeed("Orc", 15, 13, DexModifier: 1), Now);

        var orc = session.Combatants.Single();
        orc.InitiativeBonus.Should().Be(1);
        (orc.SourceKind, orc.SourceId).Should().Be((MonsterSourceKind.None, (string?)null));
    }

    [Fact]
    public void Monster_initiative_rolls_add_the_initiative_bonus_not_the_dex_modifier()
    {
        var session = NewSession();
        session.AddMonsters(Dm, Goblin with { DexModifier = -1, InitiativeBonus = 7 }, 1, hpDice: null, Now);

        session.RollMonsterInitiative(Dm, new FixedDice(10), Now);

        session.Combatants.Single().Initiative.Should().Be(17);
    }

    [Fact]
    public void One_command_bumps_the_version_once_and_logs_one_entry_without_hit_points()
    {
        var session = NewSession();
        var before = session.Version;

        var ids = session.AddMonsters(Dm, Goblin, 3, new FixedDice(1, 2, 3, 4, 5, 6, 1, 2, 3), Now);

        session.Version.Should().Be(before + 1);
        var entry = session.UncommittedEvents.Should().ContainSingle().Subject;
        entry.Type.Should().Be("MonstersAdded");
        entry.VisibleToPlayers.Should().BeTrue();
        entry.Parameters.Keys.Should().BeEquivalentTo("combatantId", "name", "kind", "baseName", "count", "combatantIds");
        entry.Parameters["baseName"].Should().Be("Goblin Warrior");
        entry.Parameters["count"].Should().Be("3");
        entry.Parameters["combatantIds"].Should().Be(string.Join(',', ids));
    }

    [Fact]
    public void Hidden_monsters_are_logged_out_of_players_sight()
    {
        var session = NewSession();

        session.AddMonsters(Dm, Goblin with { IsHidden = true }, 2, hpDice: null, Now);

        session.UncommittedEvents.Single().VisibleToPlayers.Should().BeFalse();
        session.Combatants.Should().OnlyContain(c => c.IsHidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void One_to_twenty_monsters_can_be_added_at_a_time(int count)
    {
        var session = NewSession();

        var act = () => session.AddMonsters(Dm, Goblin, count, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void An_out_of_range_initiative_bonus_is_invalid()
    {
        var session = NewSession();

        var act = () => session.AddMonsters(Dm, Goblin with { InitiativeBonus = 31 }, 1, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void A_rejected_command_adds_nothing()
    {
        var session = NewSession();
        var longName = new string('x', 79);

        var act = () => session.AddMonsters(Dm, Goblin with { Name = longName }, 2, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        session.Combatants.Should().BeEmpty();
        session.Version.Should().Be(0);
    }

    [Fact]
    public void Only_the_dm_of_the_session_adds_monsters()
    {
        var session = NewSession();

        var act = () => session.AddMonsters(OtherDm, Goblin, 2, hpDice: null, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.NotAllowed);
    }
}
```

Add to `TurnOrderTests` (inside the class; keep existing tests and helpers):

```csharp
    [Fact]
    public void A_combatant_added_mid_round_with_a_higher_initiative_than_the_current_one_acts_next()
    {
        // Intended: turns go to whoever has not acted this round, in initiative order (Plan 2 decision).
        var session = NewSession(new PcSeed("Lyra", 20, 15), new PcSeed("Torvald", 30, 18), new PcSeed("Nim", 18, 14));
        var (lyra, torvald, nim) = (Id(session, "Lyra"), Id(session, "Torvald"), Id(session, "Nim"));
        session.SetInitiative(Dm, lyra, 20, Now);
        session.SetInitiative(Dm, torvald, 10, Now);
        session.SetInitiative(Dm, nim, 5, Now);
        session.Start(Dm, Now);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(torvald);

        var ogre = session.AddMonster(Dm, new MonsterSeed("Ogre", 68, 11), Now);
        session.SetInitiative(Dm, ogre, 15, Now);
        session.NextTurn(Dm, Now);

        session.CurrentTurnId.Should().Be(ogre);
        session.Round.Should().Be(1);
        session.NextTurn(Dm, Now);
        session.CurrentTurnId.Should().Be(nim);
        session.NextTurn(Dm, Now);
        (session.CurrentTurnId, session.Round).Should().Be((lyra, 2));
    }

    private static Guid Id(CombatSession session, string name) => session.Combatants.Single(c => c.Name == name).Id;
```

If `TurnOrderTests` already has a private helper with the same signature `Id(CombatSession, string)`, use it and do not add a second one.

In `CombatEventTests.Events_about_a_combatant_hidden_at_that_moment_are_not_visible_to_players`, change `("MonsterAdded", false)` to `("MonstersAdded", false)`.

In `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs`, change `dmLog.Select(e => e.Type).Should().Equal("MonsterAdded", "Revealed");` to `dmLog.Select(e => e.Type).Should().Equal("MonstersAdded", "Revealed");`.

In `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs`:
- in `SavedSessionAsync`, replace `new MonsterSeed("Goblin", 7, 15, SourceRef: "srd:goblin")` with `new MonsterSeed("Goblin", 7, 15, DexModifier: 2, Source: MonsterSource.Srd("goblin"), InitiativeBonus: 4)`;
- in `A_combat_session_round_trips_with_its_combatants`, replace `goblin.SourceRef.Should().Be("srd:goblin");` with:

```csharp
        (goblin.SourceKind, goblin.SourceId).Should().Be((MonsterSourceKind.Srd, "goblin"));
        (goblin.InitiativeBonus, goblin.DexModifier).Should().Be((4, 2));
```

`tests/DmCompanionLive.Api.IntegrationTests/Persistence/MigrationTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the domain tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `MonsterSource`, `AddMonsters`, `InitiativeBonus`, `SourceKind` do not exist.

- [ ] **Step 3: Implement the domain**

`src/DmCompanionLive.Domain/Combat/MonsterSource.cs`:

```csharp
namespace DmCompanionLive.Domain.Combat;

public enum MonsterSourceKind
{
    None,
    Srd,
    Template,
}

/// <summary>Where a monster's stats came from. Informational: the stats are copied when it is added.</summary>
public sealed record MonsterSource(MonsterSourceKind Kind, string? Id)
{
    public static readonly MonsterSource None = new(MonsterSourceKind.None, null);

    public static MonsterSource Srd(string slug) => new(MonsterSourceKind.Srd, slug);

    public static MonsterSource Template(Guid id) => new(MonsterSourceKind.Template, id.ToString());
}
```

`src/DmCompanionLive.Domain/Combat/MonsterNames.cs`:

```csharp
using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

internal static class MonsterNames
{
    /// <summary>
    /// Names for <paramref name="count"/> new monsters called <paramref name="baseName"/>: the plain name for a
    /// single monster nobody else uses, otherwise "Base N" continuing after the highest N in use (a plain
    /// "Base" counts as 1).
    /// </summary>
    public static IReadOnlyList<string> Next(string baseName, int count, IEnumerable<string> existing)
    {
        var highest = 0;
        foreach (var name in existing)
        {
            if (name == baseName)
            {
                highest = Math.Max(highest, 1);
            }
            else if (name.StartsWith(baseName + " ", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(baseName.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return count == 1 && highest == 0
            ? [baseName]
            : Enumerable.Range(highest + 1, count).Select(n => $"{baseName} {n}").ToList();
    }
}
```

Replace `src/DmCompanionLive.Domain/Combat/Seeds.cs` with:

```csharp
namespace DmCompanionLive.Domain.Combat;

public sealed record PcSeed(string Name, int MaxHp, int Ac, int DexModifier = 0);

/// <summary>
/// The stats of a monster to add. <see cref="MaxHp"/> is the average used unless HP are rolled with
/// <see cref="HpFormula"/>; <see cref="InitiativeBonus"/> defaults to <see cref="DexModifier"/>, which
/// stays the initiative tie-break.
/// </summary>
public sealed record MonsterSeed(
    string Name,
    int MaxHp,
    int Ac,
    int DexModifier = 0,
    bool IsHidden = false,
    MonsterSource? Source = null,
    int? InitiativeBonus = null,
    HpFormula? HpFormula = null);
```

In `src/DmCompanionLive.Domain/Combat/Combatant.cs`:
- add `public const int MaxInitiativeBonus = 30;` below `MaxNameLength`;
- replace `public string? SourceRef { get; private set; }` with:

```csharp
    public int InitiativeBonus { get; private set; }
    public MonsterSourceKind SourceKind { get; private set; }
    public string? SourceId { get; private set; }
```

- in `CreatePc`, add `InitiativeBonus = seed.DexModifier,` to the initializer;
- replace `CreateMonster` with:

```csharp
    internal static Combatant CreateMonster(MonsterSeed seed, string name, int maxHp, int sortOrder)
    {
        Validate(name, maxHp, seed.Ac);
        var initiativeBonus = seed.InitiativeBonus ?? seed.DexModifier;
        if (Math.Abs(initiativeBonus) > MaxInitiativeBonus)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"Initiative bonus must be between -{MaxInitiativeBonus} and +{MaxInitiativeBonus}.");
        }

        var source = seed.Source ?? MonsterSource.None;
        return new Combatant
        {
            Id = Guid.NewGuid(),
            Kind = CombatantKind.Monster,
            Name = name.Trim(),
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Ac = seed.Ac,
            DexModifier = seed.DexModifier,
            InitiativeBonus = initiativeBonus,
            IsHidden = seed.IsHidden,
            SourceKind = source.Kind,
            SourceId = source.Id,
            SortOrder = sortOrder,
        };
    }
```

In `src/DmCompanionLive.Domain/Combat/CombatSession.cs`:
- add `public const int MaxMonstersPerCommand = 20;` below `InactivityLimit`;
- replace `AddMonster` with:

```csharp
    /// <summary>
    /// Adds <paramref name="count"/> copies of a monster, numbered when needed. With <paramref name="hpDice"/>
    /// each one rolls its own HP from the seed's formula; otherwise they get the seed's average HP. All or
    /// nothing: one version, one log entry (without hit points).
    /// </summary>
    public IReadOnlyList<Guid> AddMonsters(Actor actor, MonsterSeed seed, int count, IDiceRoller? hpDice, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        if (count is < 1 or > MaxMonstersPerCommand)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"Add 1 to {MaxMonstersPerCommand} monsters at a time.");
        }

        var baseName = Names.Validate(seed.Name);
        var firstSortOrder = NextSortOrder();
        var monsters = MonsterNames.Next(baseName, count, _combatants.Select(c => c.Name))
            .Select((name, i) => Combatant.CreateMonster(seed, name, HitPointsFor(seed, hpDice), firstSortOrder + i))
            .ToList();

        _combatants.AddRange(monsters);
        Record(
            actor,
            now,
            "MonstersAdded",
            monsters[0],
            ("baseName", baseName),
            ("count", count),
            ("combatantIds", string.Join(',', monsters.Select(m => m.Id))));
        return monsters.Select(m => m.Id).ToList();
    }

    public Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now) =>
        AddMonsters(actor, seed, 1, hpDice: null, now)[0];
```

- add next to `EnsurePositive`:

```csharp
    private static int HitPointsFor(MonsterSeed seed, IDiceRoller? hpDice) =>
        hpDice is not null && seed.HpFormula is { } formula ? formula.Roll(hpDice) : seed.MaxHp;
```

- in `RollMonsterInitiative`, replace `dice.D20() + monster.DexModifier` with `dice.D20() + monster.InitiativeBonus`.

In `src/DmCompanionLive.Domain/Combat/Views.cs`, replace the last parameter `string? SourceRef);` of `DmCombatantView` with:

```csharp
    int InitiativeBonus,
    MonsterSourceKind SourceKind,
    string? SourceId);
```

and in `CombatViews.ForDm` replace `c.ClaimedBy, c.SourceRef)` with `c.ClaimedBy, c.InitiativeBonus, c.SourceKind, c.SourceId)`.

- [ ] **Step 4: Run the domain tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS.

- [ ] **Step 5: Map the new columns**

In `CombatSessionConfiguration.cs`, replace `combatant.Property(c => c.SourceRef).HasMaxLength(120);` with:

```csharp
            combatant.Property(c => c.SourceKind).HasConversion<string>().HasMaxLength(16);
            combatant.Property(c => c.SourceId).HasMaxLength(120);
```

- [ ] **Step 6: Create the migration**

```bash
dotnet tool restore
~/.claude/bin/heavy.sh dotnet ef migrations add MonsterSources \
  --project src/DmCompanionLive.Infrastructure \
  --startup-project src/DmCompanionLive.Infrastructure \
  --output-dir Persistence/Migrations
```

EF cannot see that `SourceRef` became `SourceId`, and existing rows need values. In the generated `*_MonsterSources.cs` (not the `.Designer.cs`), replace the bodies of `Up` and `Down` with:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "SourceRef", table: "combatants", newName: "SourceId");

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "combatants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "InitiativeBonus",
                table: "combatants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Plan 2 rolled monster initiative with the Dex modifier, and stored SRD sources as "srd:<slug>".
            migrationBuilder.Sql("""
                UPDATE combatants SET "InitiativeBonus" = "DexModifier";
                UPDATE combatants SET "SourceKind" = 'Srd', "SourceId" = substr("SourceId", 5) WHERE "SourceId" LIKE 'srd:%';
                UPDATE combatants SET "SourceId" = NULL WHERE "SourceKind" = 'None';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE combatants SET "SourceId" = 'srd:' || "SourceId" WHERE "SourceKind" = 'Srd';
                UPDATE combatants SET "SourceId" = 'template:' || "SourceId" WHERE "SourceKind" = 'Template';
                """);
            migrationBuilder.DropColumn(name: "InitiativeBonus", table: "combatants");
            migrationBuilder.DropColumn(name: "SourceKind", table: "combatants");
            migrationBuilder.RenameColumn(name: "SourceId", table: "combatants", newName: "SourceRef");
        }
```

- [ ] **Step 7: Run the integration tests**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS, including `MigrationTests` and the updated `PersistenceTests` and `CombatHubCommandTests` (the hub's `AddMonster` still takes the Plan 2 `MonsterInput`; it now logs `MonstersAdded`).

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat: add several monsters at once with initiative bonus, source and rolled hit points

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Monster templates and favourites — domain and persistence

**Files:**
- Create: `src/DmCompanionLive.Domain/Monsters/MonsterTemplate.cs`
- Create: `src/DmCompanionLive.Domain/Monsters/FavoriteMonster.cs`
- Create: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/MonsterTemplateConfiguration.cs`
- Create: `src/DmCompanionLive.Infrastructure/Persistence/Configurations/FavoriteMonsterConfiguration.cs`
- Modify: `src/DmCompanionLive.Infrastructure/Persistence/AppDbContext.cs`
- Create: migration `MonsterTemplates`
- Test: `tests/DmCompanionLive.Domain.Tests/Monsters/MonsterTemplateTests.cs`, `tests/DmCompanionLive.Api.IntegrationTests/Persistence/PersistenceTests.cs`

**Interfaces:**
- Consumes: `Names`, `HpFormula` (Task 3); `MonsterSeed`, `MonsterSource`, `Combatant.MaxInitiativeBonus` (Task 4).
- Produces (namespace `DmCompanionLive.Domain.Monsters`):
  - `sealed record MonsterTemplateData(string Name, int MaxHp, int Ac, int InitiativeBonus, string? HpFormula = null, string? Notes = null)`.
  - `sealed class MonsterTemplate` with `Id`, `OwnerId`, `Name`, `MaxHp`, `Ac`, `InitiativeBonus`, `HpFormula` (`string?`, normalised), `Notes` (`string`, never null), `CreatedAt`; `const int MaxNotesLength = 2000`; `static MonsterTemplate Create(Guid ownerId, MonsterTemplateData data, DateTimeOffset now)`; `void Update(MonsterTemplateData data)`; `MonsterSeed ToSeed(bool isHidden)`.
  - `sealed class FavoriteMonster` with `OwnerId`, `SrdSlug`, `CreatedAt`; `const int MaxSlugLength = 120`; `static FavoriteMonster Create(Guid ownerId, string srdSlug, DateTimeOffset now)`.
  - `AppDbContext.MonsterTemplates` and `AppDbContext.FavoriteMonsters`; tables `monster_templates` and `favorite_monsters` (key `(OwnerId, SrdSlug)`), both with a cascading FK to `dm_users`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Domain.Tests/Monsters/MonsterTemplateTests.cs`:

```csharp
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Monsters;
using static DmCompanionLive.Domain.Tests.Fixtures;

namespace DmCompanionLive.Domain.Tests.Monsters;

public class MonsterTemplateTests
{
    private static readonly MonsterTemplateData Veteran = new("  Bandido veterano ", 30, 14, 3, "4d8 + 12", " Lleva una ballesta. ");

    [Fact]
    public void A_template_is_created_with_trimmed_text_and_a_normalised_formula()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        template.OwnerId.Should().Be(DmId);
        template.Name.Should().Be("Bandido veterano");
        (template.MaxHp, template.Ac, template.InitiativeBonus).Should().Be((30, 14, 3));
        template.HpFormula.Should().Be("4d8+12");
        template.Notes.Should().Be("Lleva una ballesta.");
    }

    [Fact]
    public void Formula_and_notes_are_optional()
    {
        var template = MonsterTemplate.Create(DmId, new MonsterTemplateData("Lobo", 11, 12, 2, "  ", null), Now);

        template.HpFormula.Should().BeNull();
        template.Notes.Should().BeEmpty();
    }

    // Keyed by a readable name so the theory data stays serializable for xUnit.
    private static readonly Dictionary<string, MonsterTemplateData> InvalidData = new()
    {
        ["blank name"] = Veteran with { Name = " " },
        ["long name"] = Veteran with { Name = new string('a', 81) },
        ["zero hp"] = Veteran with { MaxHp = 0 },
        ["negative ac"] = Veteran with { Ac = -1 },
        ["bonus above 30"] = Veteran with { InitiativeBonus = 31 },
        ["bonus below -30"] = Veteran with { InitiativeBonus = -31 },
        ["bad formula"] = Veteran with { HpFormula = "2d7" },
        ["long notes"] = Veteran with { Notes = new string('a', 2001) },
    };

    public static TheoryData<string> InvalidCases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (var key in InvalidData.Keys)
            {
                cases.Add(key);
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_data_is_rejected(string invalidCase)
    {
        var act = () => MonsterTemplate.Create(DmId, InvalidData[invalidCase], Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
    }

    [Fact]
    public void An_update_replaces_every_field_and_a_rejected_one_changes_nothing()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        template.Update(new MonsterTemplateData("Capitán bandido", 52, 15, 3, null, "Jefe."));
        var act = () => template.Update(Veteran with { MaxHp = 0 });

        act.Should().Throw<DomainException>();
        (template.Name, template.MaxHp, template.HpFormula, template.Notes).Should().Be(("Capitán bandido", 52, (string?)null, "Jefe."));
    }

    [Fact]
    public void A_template_becomes_a_monster_seed_that_remembers_it()
    {
        var template = MonsterTemplate.Create(DmId, Veteran, Now);

        var seed = template.ToSeed(isHidden: true);

        seed.Should().BeEquivalentTo(new MonsterSeed(
            "Bandido veterano", 30, 14, DexModifier: 3, IsHidden: true, Source: MonsterSource.Template(template.Id),
            InitiativeBonus: 3, HpFormula: HpFormula.Parse("4d8+12")));
    }

    [Fact]
    public void A_favourite_needs_a_slug()
    {
        var act = () => FavoriteMonster.Create(DmId, " ", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainErrorCode.InvalidValue);
        FavoriteMonster.Create(DmId, "owlbear", Now).SrdSlug.Should().Be("owlbear");
    }
}
```

Add to `PersistenceTests` (inside the class; add `using DmCompanionLive.Domain.Monsters;`):

```csharp
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
```

- [ ] **Step 2: Run the domain tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: build FAILS — `DmCompanionLive.Domain.Monsters` does not exist.

- [ ] **Step 3: Implement the domain**

`src/DmCompanionLive.Domain/Monsters/MonsterTemplate.cs`:

```csharp
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Monsters;

public sealed record MonsterTemplateData(
    string Name,
    int MaxHp,
    int Ac,
    int InitiativeBonus,
    string? HpFormula = null,
    string? Notes = null);

/// <summary>A DM's own monster. Its stats are copied into a combat when it is added, never linked.</summary>
public sealed class MonsterTemplate
{
    public const int MaxNotesLength = 2000;

    private MonsterTemplate()
    {
        Name = string.Empty;
        Notes = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public int MaxHp { get; private set; }
    public int Ac { get; private set; }
    public int InitiativeBonus { get; private set; }

    /// <summary>Normalised, e.g. "4d8+12"; null when HP are always the fixed <see cref="MaxHp"/>.</summary>
    public string? HpFormula { get; private set; }

    public string Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MonsterTemplate Create(Guid ownerId, MonsterTemplateData data, DateTimeOffset now)
    {
        var template = new MonsterTemplate { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = now.ToUniversalTime() };
        template.Update(data);
        return template;
    }

    public void Update(MonsterTemplateData data)
    {
        var name = Names.Validate(data.Name);
        if (data.MaxHp < 1)
        {
            throw Invalid("Max HP must be at least 1.");
        }

        if (data.Ac < 0)
        {
            throw Invalid("AC cannot be negative.");
        }

        if (Math.Abs(data.InitiativeBonus) > Combatant.MaxInitiativeBonus)
        {
            throw Invalid($"Initiative bonus must be between -{Combatant.MaxInitiativeBonus} and +{Combatant.MaxInitiativeBonus}.");
        }

        var formula = string.IsNullOrWhiteSpace(data.HpFormula) ? null : Combat.HpFormula.Parse(data.HpFormula).ToString();
        var notes = data.Notes?.Trim() ?? string.Empty;
        if (notes.Length > MaxNotesLength)
        {
            throw Invalid($"Notes have at most {MaxNotesLength} characters.");
        }

        Name = name;
        MaxHp = data.MaxHp;
        Ac = data.Ac;
        InitiativeBonus = data.InitiativeBonus;
        HpFormula = formula;
        Notes = notes;
    }

    /// <summary>Templates have no Dex score: the initiative bonus is also the tie-break.</summary>
    public MonsterSeed ToSeed(bool isHidden) => new(
        Name,
        MaxHp,
        Ac,
        DexModifier: InitiativeBonus,
        IsHidden: isHidden,
        Source: MonsterSource.Template(Id),
        InitiativeBonus: InitiativeBonus,
        HpFormula: HpFormula is null ? null : Combat.HpFormula.Parse(HpFormula));

    private static DomainException Invalid(string message) => new(DomainErrorCode.InvalidValue, message);
}
```

`src/DmCompanionLive.Domain/Monsters/FavoriteMonster.cs`:

```csharp
namespace DmCompanionLive.Domain.Monsters;

/// <summary>An SRD monster a DM marked. The slug is checked against the catalogue by the API.</summary>
public sealed class FavoriteMonster
{
    public const int MaxSlugLength = 120;

    private FavoriteMonster()
    {
        SrdSlug = string.Empty;
    }

    public Guid OwnerId { get; private set; }
    public string SrdSlug { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static FavoriteMonster Create(Guid ownerId, string srdSlug, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(srdSlug) || srdSlug.Length > MaxSlugLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"A favourite needs an SRD slug of 1 to {MaxSlugLength} characters.");
        }

        return new FavoriteMonster { OwnerId = ownerId, SrdSlug = srdSlug, CreatedAt = now.ToUniversalTime() };
    }
}
```

- [ ] **Step 4: Run the domain tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Domain.Tests`
Expected: PASS.

- [ ] **Step 5: Persistence**

`src/DmCompanionLive.Infrastructure/Persistence/Configurations/MonsterTemplateConfiguration.cs`:

```csharp
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class MonsterTemplateConfiguration : IEntityTypeConfiguration<MonsterTemplate>
{
    public void Configure(EntityTypeBuilder<MonsterTemplate> builder)
    {
        builder.ToTable("monster_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Names.MaxLength).IsRequired();
        builder.Property(t => t.HpFormula).HasMaxLength(32);
        builder.Property(t => t.Notes).HasMaxLength(MonsterTemplate.MaxNotesLength).IsRequired();
        builder.HasIndex(t => t.OwnerId);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/DmCompanionLive.Infrastructure/Persistence/Configurations/FavoriteMonsterConfiguration.cs`:

```csharp
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class FavoriteMonsterConfiguration : IEntityTypeConfiguration<FavoriteMonster>
{
    public void Configure(EntityTypeBuilder<FavoriteMonster> builder)
    {
        builder.ToTable("favorite_monsters");
        builder.HasKey(f => new { f.OwnerId, f.SrdSlug });
        builder.Property(f => f.SrdSlug).HasMaxLength(FavoriteMonster.MaxSlugLength);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

In `AppDbContext.cs`, add `using DmCompanionLive.Domain.Monsters;` and:

```csharp
    public DbSet<MonsterTemplate> MonsterTemplates => Set<MonsterTemplate>();
    public DbSet<FavoriteMonster> FavoriteMonsters => Set<FavoriteMonster>();
```

Create the migration:

```bash
~/.claude/bin/heavy.sh dotnet ef migrations add MonsterTemplates \
  --project src/DmCompanionLive.Infrastructure \
  --startup-project src/DmCompanionLive.Infrastructure \
  --output-dir Persistence/Migrations
```

Open `*_MonsterTemplates.cs` and check it: creates `monster_templates` (`Name` varchar(80), `HpFormula` varchar(32) nullable, `Notes` varchar(2000), index on `OwnerId`) and `favorite_monsters` (primary key `OwnerId, SrdSlug`, `SrdSlug` varchar(120)), both with an FK to `dm_users` and `ReferentialAction.Cascade`. Nothing else.

- [ ] **Step 6: Run the integration tests**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~Persistence"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat: store monster templates and favourite SRD monsters

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: REST — SRD search, monster details and favourites

**Files:**
- Modify: `src/DmCompanionLive.Domain/DomainException.cs` (`MonsterNotFound`)
- Modify: `src/DmCompanionLive.Api/Errors/DomainExceptionHandler.cs` (404 for `MonsterNotFound`)
- Create: `src/DmCompanionLive.Api/Features/Srd/Languages.cs`
- Create: `src/DmCompanionLive.Api/Features/Srd/SrdEndpoints.cs`
- Create: `src/DmCompanionLive.Api/Features/Monsters/FavoritesEndpoints.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs` (`MapSrd()`, `MapFavorites()`)
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Features/SrdTests.cs`

**Interfaces:**
- Consumes: `ISrdCatalog`, `SrdMonster`, `SrdLanguage`, `SearchText` (Tasks 1–2); `FavoriteMonster`, `AppDbContext.FavoriteMonsters` (Task 5).
- Produces:
  - `DomainErrorCode.MonsterNotFound` (appended last), mapped to HTTP 404.
  - `static class Languages { static SrdLanguage Resolve(string? lang, HttpRequest request); static SrdLanguage? TryParse(string? tag) }` in `DmCompanionLive.Api.Features.Srd`.
  - `SrdEndpoints.SrdMonsterSummaryDto(string Slug, string Name, string Type, IReadOnlyList<string> Sizes, string Cr, int Ac, int HpAverage, bool IsFavorite)` with `static From(SrdMonster, SrdLanguage, bool isFavorite)`; `SrdEndpoints.MonsterNotFound(string slug)` → `DomainException`.
  - Routes: `GET /api/srd/monsters?q=&lang=&page=`, `GET /api/srd/monsters/{slug}?lang=`, `GET /api/monsters/favorites?lang=`, `PUT /api/monsters/favorites/{slug}`, `DELETE /api/monsters/favorites/{slug}` — all DM-only.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Api.IntegrationTests/Features/SrdTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SrdTests(PostgresFixture postgres)
{
    private sealed record SummaryDto(string Slug, string Name, string Type, List<string> Sizes, string Cr, int Ac, int HpAverage, bool IsFavorite);

    private sealed record SearchDto(List<SummaryDto> Items, int Page, int Total);

    private sealed record EntryDto(string? Name, string Text);

    private sealed record SectionDto(string Kind, List<EntryDto> Entries);

    private sealed record StatBlockDto(string Subtitle, List<SectionDto> Sections);

    private sealed record MonsterDto(string Slug, string Name, string HpFormula, int InitiativeBonus, bool IsFavorite, StatBlockDto StatBlock);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/srd/monsters")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/monsters/favorites")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_ignores_accents_and_returns_names_in_the_requested_language()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var spanish = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=dragon%20rojo&lang=es"));
        var english = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=oso%20lechuza&lang=en"));

        spanish.Items[0].Should().BeEquivalentTo(new { Slug = "adult-red-dragon", Name = "Dragón rojo adulto" });
        english.Items.Should().ContainSingle().Which.Name.Should().Be("Owlbear");
        english.Total.Should().Be(1);
    }

    [Fact]
    public async Task Without_lang_the_accept_language_header_decides_and_english_is_the_default()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/srd/monsters?q=owlbear");
        request.Headers.TryAddWithoutValidation("Accept-Language", "fr-FR,es;q=0.9,en;q=0.8");

        var spanish = await ReadAsync<SearchDto>(await client.SendAsync(request));
        var english = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=owlbear"));

        spanish.Items[0].Name.Should().Be("Oso lechuza");
        english.Items[0].Name.Should().Be("Owlbear");
    }

    [Theory]
    [InlineData("/api/srd/monsters?lang=fr")]
    [InlineData("/api/srd/monsters?page=0")]
    public async Task An_unsupported_language_or_page_is_an_invalid_value(string url)
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("InvalidValue");
    }

    [Fact]
    public async Task A_monster_comes_with_its_stat_block_in_the_requested_language()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var english = await ReadAsync<MonsterDto>(await client.GetAsync("/api/srd/monsters/goblin-warrior?lang=en"));
        var spanish = await ReadAsync<MonsterDto>(await client.GetAsync("/api/srd/monsters/goblin-warrior?lang=es"));
        var missing = await client.GetAsync("/api/srd/monsters/kraken-of-nowhere");

        (english.Name, english.HpFormula, english.InitiativeBonus).Should().Be(("Goblin Warrior", "3d6", 2));
        english.StatBlock.Sections.Should().Contain(s => s.Kind == "Actions" && s.Entries.Any(e => e.Name == "Scimitar"));
        spanish.Name.Should().Be("Guerrero goblin");
        spanish.StatBlock.Sections.Should().Contain(s => s.Kind == "Actions");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).Should().Be("MonsterNotFound");
    }

    [Fact]
    public async Task Favourites_are_idempotent_per_dm_and_marked_in_search_results()
    {
        var ana = await postgres.Factory.CreateDmClientAsync();
        var bea = await postgres.Factory.CreateDmClientAsync();

        (await ana.PutAsync("/api/monsters/favorites/owlbear", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ana.PutAsync("/api/monsters/favorites/owlbear", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var favorites = await ReadAsync<List<SummaryDto>>(await ana.GetAsync("/api/monsters/favorites?lang=es"));
        favorites.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Slug = "owlbear", Name = "Oso lechuza", IsFavorite = true });
        (await ReadAsync<SearchDto>(await ana.GetAsync("/api/srd/monsters?q=owlbear"))).Items[0].IsFavorite.Should().BeTrue();
        (await ReadAsync<MonsterDto>(await ana.GetAsync("/api/srd/monsters/owlbear"))).IsFavorite.Should().BeTrue();
        (await ReadAsync<SearchDto>(await bea.GetAsync("/api/srd/monsters?q=owlbear"))).Items[0].IsFavorite.Should().BeFalse();
        (await ReadAsync<List<SummaryDto>>(await bea.GetAsync("/api/monsters/favorites"))).Should().BeEmpty();

        (await ana.DeleteAsync("/api/monsters/favorites/owlbear")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ana.DeleteAsync("/api/monsters/favorites/owlbear")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync<List<SummaryDto>>(await ana.GetAsync("/api/monsters/favorites"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_srd_monsters_can_be_favourites()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var response = await client.PutAsync("/api/monsters/favorites/kraken-of-nowhere", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("MonsterNotFound");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~SrdTests"`
Expected: FAIL — the routes return 404 (`Anonymous_callers_get_401` fails with 404 instead of 401).

- [ ] **Step 3: Implement**

In `src/DmCompanionLive.Domain/DomainException.cs`, append `MonsterNotFound,` after `Conflict,` in `DomainErrorCode`.

In `DomainExceptionHandler.StatusFor`, change the not-found arm to:

```csharp
        DomainErrorCode.CombatantNotFound or DomainErrorCode.CharacterNotFound or DomainErrorCode.MonsterNotFound
            => StatusCodes.Status404NotFound,
```

`src/DmCompanionLive.Api/Features/Srd/Languages.cs`:

```csharp
using DmCompanionLive.Domain;
using DmCompanionLive.Infrastructure.Srd;

namespace DmCompanionLive.Api.Features.Srd;

/// <summary>Which language SRD content is served in.</summary>
public static class Languages
{
    /// <summary>
    /// An explicit <paramref name="lang"/> must be supported (else <c>InvalidValue</c>); without it the first
    /// supported language of <c>Accept-Language</c> (by quality) wins, then English.
    /// </summary>
    public static SrdLanguage Resolve(string? lang, HttpRequest request)
    {
        if (!string.IsNullOrEmpty(lang))
        {
            return TryParse(lang)
                ?? throw new DomainException(DomainErrorCode.InvalidValue, "Supported languages are 'en' and 'es'.");
        }

        foreach (var value in request.GetTypedHeaders().AcceptLanguage.OrderByDescending(v => v.Quality ?? 1))
        {
            if (TryParse(value.Value.Value) is { } language)
            {
                return language;
            }
        }

        return SrdLanguage.En;
    }

    /// <summary>"es", "ES", "es-ES" → Spanish; "en…" → English; anything else → null.</summary>
    public static SrdLanguage? TryParse(string? tag) => tag?.Split('-')[0].ToLowerInvariant() switch
    {
        "en" => SrdLanguage.En,
        "es" => SrdLanguage.Es,
        _ => null,
    };
}
```

`src/DmCompanionLive.Api/Features/Srd/SrdEndpoints.cs`:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Srd;

public static class SrdEndpoints
{
    public const int MaxQueryLength = 100;

    public sealed record SrdMonsterSummaryDto(
        string Slug,
        string Name,
        string Type,
        IReadOnlyList<string> Sizes,
        string Cr,
        int Ac,
        int HpAverage,
        bool IsFavorite)
    {
        public static SrdMonsterSummaryDto From(SrdMonster monster, SrdLanguage language, bool isFavorite) => new(
            monster.Slug, monster.Name.For(language), monster.Type, monster.Sizes, monster.Cr, monster.Ac,
            monster.HpAverage, isFavorite);
    }

    public sealed record SrdSearchDto(IReadOnlyList<SrdMonsterSummaryDto> Items, int Page, int Total);

    public sealed record SrdMonsterDto(
        string Slug,
        string Name,
        string Type,
        IReadOnlyList<string> Sizes,
        string Cr,
        int Ac,
        int HpAverage,
        string HpFormula,
        int InitiativeBonus,
        IReadOnlyList<int> Abilities,
        bool IsFavorite,
        StatBlock StatBlock);

    public static IEndpointRouteBuilder MapSrd(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/srd/monsters").RequireAuthorization(AuthSetup.DmPolicy).WithTags("SRD");
        group.MapGet("/", Search);
        group.MapGet("/{slug}", Get);
        return app;
    }

    public static DomainException MonsterNotFound(string slug) =>
        new(DomainErrorCode.MonsterNotFound, $"There is no SRD monster '{slug}'.");

    private static async Task<Ok<SrdSearchDto>> Search(
        string? q, string? lang, int? page, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog,
        AppDbContext db, CancellationToken ct)
    {
        if (q is { Length: > MaxQueryLength } || page < 1)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"The query has at most {MaxQueryLength} characters and pages start at 1.");
        }

        var language = Languages.Resolve(lang, request);
        var result = catalog.Search(q, language, page ?? 1);
        var favorites = await FavoriteSlugsAsync(db, user.GetDmUserId(), result.Items.Select(m => m.Slug).ToList(), ct);
        return TypedResults.Ok(new SrdSearchDto(
            result.Items.Select(m => SrdMonsterSummaryDto.From(m, language, favorites.Contains(m.Slug))).ToList(),
            result.Page,
            result.Total));
    }

    private static async Task<Ok<SrdMonsterDto>> Get(
        string slug, string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db,
        CancellationToken ct)
    {
        var monster = catalog.Find(slug) ?? throw MonsterNotFound(slug);
        var language = Languages.Resolve(lang, request);
        var favorites = await FavoriteSlugsAsync(db, user.GetDmUserId(), [monster.Slug], ct);
        return TypedResults.Ok(new SrdMonsterDto(
            monster.Slug,
            monster.Name.For(language),
            monster.Type,
            monster.Sizes,
            monster.Cr,
            monster.Ac,
            monster.HpAverage,
            monster.HpFormula,
            monster.InitiativeBonus,
            monster.Abilities,
            favorites.Contains(monster.Slug),
            monster.StatBlock.For(language)));
    }

    private static async Task<HashSet<string>> FavoriteSlugsAsync(
        AppDbContext db, Guid ownerId, List<string> slugs, CancellationToken ct) =>
        (await db.FavoriteMonsters
            .Where(f => f.OwnerId == ownerId && slugs.Contains(f.SrdSlug))
            .Select(f => f.SrdSlug)
            .ToListAsync(ct))
        .ToHashSet(StringComparer.Ordinal);
}
```

`src/DmCompanionLive.Api/Features/Monsters/FavoritesEndpoints.cs`:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Features.Srd;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DmCompanionLive.Api.Features.Srd.SrdEndpoints;

namespace DmCompanionLive.Api.Features.Monsters;

public static class FavoritesEndpoints
{
    public static IEndpointRouteBuilder MapFavorites(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monsters/favorites").RequireAuthorization(AuthSetup.DmPolicy).WithTags("Favorites");
        group.MapGet("/", List);
        group.MapPut("/{slug}", Add);
        group.MapDelete("/{slug}", Remove);
        return app;
    }

    private static async Task<Ok<List<SrdMonsterSummaryDto>>> List(
        string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db, CancellationToken ct)
    {
        var language = Languages.Resolve(lang, request);
        var ownerId = user.GetDmUserId();
        var slugs = await db.FavoriteMonsters.Where(f => f.OwnerId == ownerId).Select(f => f.SrdSlug).ToListAsync(ct);

        // A favourite whose monster left the catalogue (after a new SRD import) is skipped, not an error.
        var items = slugs
            .Select(catalog.Find)
            .OfType<SrdMonster>()
            .Select(m => SrdMonsterSummaryDto.From(m, language, isFavorite: true))
            .OrderBy(m => SearchText.Normalize(m.Name), StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(items);
    }

    private static async Task<NoContent> Add(
        string slug, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (catalog.Find(slug) is null)
        {
            throw MonsterNotFound(slug);
        }

        var ownerId = user.GetDmUserId();
        if (await db.FavoriteMonsters.AnyAsync(f => f.OwnerId == ownerId && f.SrdSlug == slug, ct))
        {
            return TypedResults.NoContent();
        }

        db.FavoriteMonsters.Add(FavoriteMonster.Create(ownerId, slug, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same favourite was added concurrently: the outcome is the one asked for.
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Remove(string slug, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        await db.FavoriteMonsters.Where(f => f.OwnerId == ownerId && f.SrdSlug == slug).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
```

In `Program.cs`, add `using DmCompanionLive.Api.Features.Monsters;` and `using DmCompanionLive.Api.Features.Srd;`, and after `app.MapJoinSession();`:

```csharp
app.MapSrd();
app.MapFavorites();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS (new `SrdTests` and every existing test).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: search SRD monsters and manage favourites over REST

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: REST — monster templates

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Monsters/MonsterTemplatesEndpoints.cs`
- Modify: `src/DmCompanionLive.Api/Program.cs` (`MapMonsterTemplates()`)
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Features/MonsterTemplatesTests.cs`

**Interfaces:**
- Consumes: `MonsterTemplate`, `MonsterTemplateData`, `AppDbContext.MonsterTemplates` (Task 5); `ISrdCatalog` (Task 2); `Languages`, `SrdEndpoints.MonsterNotFound` (Task 6).
- Produces: routes `GET /api/monsters/templates`, `GET /api/monsters/templates/{id}`, `POST /api/monsters/templates`, `POST /api/monsters/templates/from-srd/{slug}?lang=`, `PUT /api/monsters/templates/{id}`, `DELETE /api/monsters/templates/{id}`; DTO `TemplateDto(Guid Id, string Name, int MaxHp, int Ac, int InitiativeBonus, string? HpFormula, string Notes)`; request `TemplateRequest(string Name, int MaxHp, int Ac, int InitiativeBonus, string? HpFormula = null, string? Notes = null)`.

- [ ] **Step 1: Write the failing tests**

`tests/DmCompanionLive.Api.IntegrationTests/Features/MonsterTemplatesTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class MonsterTemplatesTests(PostgresFixture postgres)
{
    private sealed record TemplateDto(Guid Id, string Name, int MaxHp, int Ac, int InitiativeBonus, string? HpFormula, string Notes);

    private static readonly object Veteran = new
    {
        name = " Bandido veterano ",
        maxHp = 30,
        ac = 14,
        initiativeBonus = 3,
        hpFormula = "4d8 + 12",
        notes = "Lleva una ballesta.",
    };

    private static async Task<TemplateDto> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = (await response.Content.ReadFromJsonAsync<TemplateDto>(TestJson.Options))!;
        response.Headers.Location!.ToString().Should().Be($"/api/monsters/templates/{template.Id}");
        return template;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/monsters/templates")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dm_creates_lists_edits_and_deletes_templates()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var created = await CreatedAsync(await client.PostAsJsonAsync("/api/monsters/templates", Veteran));
        created.Should().BeEquivalentTo(new
        {
            Name = "Bandido veterano", MaxHp = 30, Ac = 14, InitiativeBonus = 3, HpFormula = "4d8+12", Notes = "Lleva una ballesta.",
        });

        var update = await client.PutAsJsonAsync(
            $"/api/monsters/templates/{created.Id}", new { name = "Capitán bandido", maxHp = 52, ac = 15, initiativeBonus = 3 });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await client.GetFromJsonAsync<List<TemplateDto>>("/api/monsters/templates", TestJson.Options))!;
        list.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            created.Id, Name = "Capitán bandido", MaxHp = 52, HpFormula = (string?)null, Notes = string.Empty,
        });

        (await client.DeleteAsync($"/api/monsters/templates/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/monsters/templates/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Templates_of_another_dm_are_not_found()
    {
        var ana = await postgres.Factory.CreateDmClientAsync();
        var bea = await postgres.Factory.CreateDmClientAsync();
        var template = await CreatedAsync(await ana.PostAsJsonAsync("/api/monsters/templates", Veteran));

        (await bea.GetAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.PutAsJsonAsync($"/api/monsters/templates/{template.Id}", Veteran)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.DeleteAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.GetFromJsonAsync<List<TemplateDto>>("/api/monsters/templates", TestJson.Options)).Should().BeEmpty();
        (await ana.GetAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_templates_are_rejected()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var badFormula = await client.PostAsJsonAsync(
            "/api/monsters/templates", new { name = "Lobo", maxHp = 11, ac = 12, initiativeBonus = 2, hpFormula = "2d7" });
        var noName = await client.PostAsJsonAsync("/api/monsters/templates", new { maxHp = 11, ac = 12, initiativeBonus = 2 });

        badFormula.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(badFormula)).Should().Be("InvalidValue");
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_template_can_start_as_a_copy_of_an_srd_monster()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var copy = await CreatedAsync(await client.PostAsync("/api/monsters/templates/from-srd/owlbear?lang=es", null));
        var missing = await client.PostAsync("/api/monsters/templates/from-srd/kraken-of-nowhere", null);

        copy.Should().BeEquivalentTo(new
        {
            Name = "Oso lechuza", MaxHp = 59, Ac = 13, InitiativeBonus = 1, HpFormula = "7d10+21", Notes = string.Empty,
        });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).Should().Be("MonsterNotFound");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~MonsterTemplatesTests"`
Expected: FAIL — the routes do not exist.

- [ ] **Step 3: Implement**

`src/DmCompanionLive.Api/Features/Monsters/MonsterTemplatesEndpoints.cs`:

```csharp
using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Features.Srd;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Monsters;

public static class MonsterTemplatesEndpoints
{
    public sealed record TemplateRequest(
        string Name,
        int MaxHp,
        int Ac,
        int InitiativeBonus,
        string? HpFormula = null,
        string? Notes = null)
    {
        public MonsterTemplateData ToData() => new(Name, MaxHp, Ac, InitiativeBonus, HpFormula, Notes);
    }

    public sealed record TemplateDto(
        Guid Id,
        string Name,
        int MaxHp,
        int Ac,
        int InitiativeBonus,
        string? HpFormula,
        string Notes)
    {
        public static TemplateDto From(MonsterTemplate t) =>
            new(t.Id, t.Name, t.MaxHp, t.Ac, t.InitiativeBonus, t.HpFormula, t.Notes);
    }

    public static IEndpointRouteBuilder MapMonsterTemplates(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monsters/templates").RequireAuthorization(AuthSetup.DmPolicy).WithTags("Monster templates");
        group.MapGet("/", List);
        group.MapGet("/{id:guid}", Get);
        group.MapPost("/", Create);
        group.MapPost("/from-srd/{slug}", CreateFromSrd);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        return app;
    }

    private static async Task<Ok<List<TemplateDto>>> List(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var templates = await db.MonsterTemplates.Where(t => t.OwnerId == ownerId).OrderBy(t => t.Name).ToListAsync(ct);
        return TypedResults.Ok(templates.Select(TemplateDto.From).ToList());
    }

    private static async Task<Results<Ok<TemplateDto>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        return template is null ? TypedResults.NotFound() : TypedResults.Ok(TemplateDto.From(template));
    }

    private static Task<Created<TemplateDto>> Create(
        TemplateRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        SaveNewAsync(db, MonsterTemplate.Create(user.GetDmUserId(), request.ToData(), clock.GetUtcNow()), ct);

    /// <summary>A new template with the SRD monster's name (in <c>lang</c>) and numbers, ready to edit.</summary>
    private static Task<Created<TemplateDto>> CreateFromSrd(
        string slug, string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var monster = catalog.Find(slug) ?? throw SrdEndpoints.MonsterNotFound(slug);
        var language = Languages.Resolve(lang, request);
        var data = new MonsterTemplateData(
            monster.Name.For(language), monster.HpAverage, monster.Ac, monster.InitiativeBonus, monster.HpFormula);
        return SaveNewAsync(db, MonsterTemplate.Create(user.GetDmUserId(), data, clock.GetUtcNow()), ct);
    }

    private static async Task<Results<NoContent, NotFound>> Update(
        Guid id, TemplateRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        template.Update(request.ToData());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        db.MonsterTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Created<TemplateDto>> SaveNewAsync(AppDbContext db, MonsterTemplate template, CancellationToken ct)
    {
        db.MonsterTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/monsters/templates/{template.Id}", TemplateDto.From(template));
    }

    /// <summary>Templates of other DMs are reported as not found, never as forbidden.</summary>
    private static Task<MonsterTemplate?> FindOwnedAsync(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        return db.MonsterTemplates.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);
    }
}
```

In `Program.cs`, after `app.MapFavorites();`:

```csharp
app.MapMonsterTemplates();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: manage monster templates over REST

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Hub — `AddMonster` from the SRD, a template or ad-hoc stats

**Files:**
- Create: `src/DmCompanionLive.Api/Features/Combat/MonsterSources.cs`
- Modify: `src/DmCompanionLive.Api/Features/Combat/CombatContracts.cs` (replace `MonsterInput`)
- Modify: `src/DmCompanionLive.Api/Features/Combat/CombatHub.cs` (`AddMonster`, `SetConditions`, `Language`)
- Modify: `src/DmCompanionLive.Api/Program.cs` (register `MonsterSources`)
- Modify: `tests/DmCompanionLive.Api.IntegrationTests/Infrastructure/HubClients.cs` (optional `lang`)
- Test: `tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs`

**Interfaces:**
- Consumes: `CombatSession.AddMonsters`, `MonsterSeed`, `MonsterSource`, `HpFormula` (Tasks 3–4); `MonsterTemplate.ToSeed` (Task 5); `ISrdCatalog` (Task 2); `Languages.TryParse` (Task 6); `DomainErrorCode.MonsterNotFound` (Task 6).
- Produces:
  - `AddMonsterInput(MonsterSourceInput? Source, int Count = 1, bool RollHp = false, bool IsHidden = false)`, `MonsterSourceInput(string? SrdSlug = null, Guid? TemplateId = null, CustomMonsterInput? Custom = null)`, `CustomMonsterInput(string? Name, int MaxHp, int Ac, int InitiativeBonus = 0, string? HpFormula = null)` in `DmCompanionLive.Api.Features.Combat`. `MonsterInput` is removed.
  - `MonsterSources.ResolveAsync(AddMonsterInput? input, Actor actor, SrdLanguage language, CancellationToken ct)` → `Task<MonsterSeed>`; throws `DomainException`.
  - Hub methods `AddMonster(AddMonsterInput? input, int expectedVersion)` and `SetConditions(Guid combatantId, IReadOnlyList<Condition>? conditions, int expectedVersion)`.
  - `HubClients.ConnectAsync(factory, cookie, guestToken, lang)`.

- [ ] **Step 1: Let test connections pass a language**

In `HubClients.ConnectAsync`, add a parameter `string? lang = null` after `guestToken` and build the URL with it:

```csharp
        var url = lang is null ? "https://localhost/hubs/combat" : $"https://localhost/hubs/combat?lang={lang}";
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
```

(the rest of the method is unchanged).

- [ ] **Step 2: Write the failing tests**

In `CombatHubCommandTests`:

1. Add `using System.Net.Http.Json;` at the top.
2. Replace every Plan 2 `MonsterInput` by the new helper:

```bash
sed -i 's/new MonsterInput(/Monster(/g; s/IsHidden: true)/isHidden: true)/g' \
  tests/DmCompanionLive.Api.IntegrationTests/Features/CombatHubCommandTests.cs
```

3. Change the private `ConnectAsync` helper to take and forward a language:

```csharp
    private async Task<Participant> ConnectAsync(string code, string? cookie = null, string? guestToken = null, string? lang = null)
    {
        var hub = await postgres.Factory.ConnectAsync(cookie, guestToken, lang);
```

(the rest of the helper is unchanged).

4. Add these helpers next to the other private helpers:

```csharp
    private sealed record CreatedTemplate(Guid Id);

    private static AddMonsterInput Monster(string name, int maxHp, int ac, bool isHidden = false) =>
        new(new MonsterSourceInput(Custom: new CustomMonsterInput(name, maxHp, ac)), IsHidden: isHidden);

    private static async Task<CreatedTemplate> CreateTemplateAsync(HttpClient dm, object template)
    {
        var response = await dm.PostAsJsonAsync("/api/monsters/templates", template);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedTemplate>(TestJson.Options))!;
    }
```

5. Add these tests:

```csharp
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
            name = "Bandido veterano", maxHp = 30, ac = 14, initiativeBonus = 3, hpFormula = "4d8+12",
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

        async Task<string?> ErrorAsync(Participant who, AddMonsterInput? input) =>
            (await who.Hub.InvokeAsync<CommandResult>("AddMonster", input, version)).Error;

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
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "kraken-of-nowhere"))))
            .Should().Be("MonsterNotFound");
        (await ErrorAsync(dm, new AddMonsterInput(new MonsterSourceInput(TemplateId: foreign.Id))))
            .Should().Be("MonsterNotFound");
        (await ErrorAsync(ana, new AddMonsterInput(new MonsterSourceInput(SrdSlug: "goblin-warrior"))))
            .Should().Be("NotAllowed");

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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests --filter "FullyQualifiedName~CombatHubCommandTests"`
Expected: build FAILS — `AddMonsterInput`, `MonsterSourceInput`, `CustomMonsterInput` do not exist.

- [ ] **Step 4: Implement**

In `CombatContracts.cs`, replace the `MonsterInput` record (and its doc comment) with:

```csharp
/// <summary>
/// A monster to add: exactly one of <see cref="MonsterSourceInput.SrdSlug"/>, <see cref="MonsterSourceInput.TemplateId"/>
/// or <see cref="MonsterSourceInput.Custom"/>. Members are nullable so a missing value is answered with
/// <c>InvalidValue</c> instead of a generic hub error.
/// </summary>
public sealed record AddMonsterInput(MonsterSourceInput? Source, int Count = 1, bool RollHp = false, bool IsHidden = false);

public sealed record MonsterSourceInput(string? SrdSlug = null, Guid? TemplateId = null, CustomMonsterInput? Custom = null);

public sealed record CustomMonsterInput(string? Name, int MaxHp, int Ac, int InitiativeBonus = 0, string? HpFormula = null);
```

`src/DmCompanionLive.Api/Features/Combat/MonsterSources.cs`:

```csharp
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Turns an <see cref="AddMonsterInput"/> into the stats of the monster to add, on the server.</summary>
public sealed class MonsterSources(AppDbContext db, ISrdCatalog catalog)
{
    /// <exception cref="DomainException">
    /// <c>InvalidValue</c> for a missing or ambiguous source or bad stats; <c>NotAllowed</c> for players;
    /// <c>MonsterNotFound</c> for an unknown slug or a template that is missing or another DM's.
    /// </exception>
    public async Task<MonsterSeed> ResolveAsync(AddMonsterInput? input, Actor actor, SrdLanguage language, CancellationToken ct)
    {
        if (input?.Source is not { } source)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "A monster source is required.");
        }

        var given = (source.SrdSlug is null ? 0 : 1) + (source.TemplateId is null ? 0 : 1) + (source.Custom is null ? 0 : 1);
        if (given != 1)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Give exactly one of srdSlug, templateId or custom.");
        }

        if (actor is not Actor.Dm dm)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only the DM of this session can do this.");
        }

        if (source.SrdSlug is { } slug)
        {
            var monster = catalog.Find(slug) ?? throw NotFound();
            return new MonsterSeed(
                monster.Name.For(language),
                monster.HpAverage,
                monster.Ac,
                monster.DexModifier,
                input.IsHidden,
                MonsterSource.Srd(monster.Slug),
                monster.InitiativeBonus,
                HpFormula.Parse(monster.HpFormula));
        }

        if (source.TemplateId is { } templateId)
        {
            var template = await db.MonsterTemplates.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == templateId && t.OwnerId == dm.UserId, ct)
                ?? throw NotFound();
            return template.ToSeed(input.IsHidden);
        }

        var custom = source.Custom!;
        return new MonsterSeed(
            custom.Name ?? string.Empty,
            custom.MaxHp,
            custom.Ac,
            custom.InitiativeBonus,
            input.IsHidden,
            MonsterSource.None,
            custom.InitiativeBonus,
            string.IsNullOrWhiteSpace(custom.HpFormula) ? null : HpFormula.Parse(custom.HpFormula));
    }

    private static DomainException NotFound() =>
        new(DomainErrorCode.MonsterNotFound, "There is no such monster or template.");
}
```

In `CombatHub.cs`:
- add `using DmCompanionLive.Api.Features.Srd;`, `using DmCompanionLive.Domain;` and `using DmCompanionLive.Infrastructure.Srd;`;
- add a constructor parameter `MonsterSources monsterSources` after `IDiceRoller dice`;
- add below `JoinedSessionId`:

```csharp
    /// <summary>Language for SRD monster names, from the connection URL (<c>/hubs/combat?lang=es</c>); English by default.</summary>
    private SrdLanguage Language =>
        Languages.TryParse(Context.GetHttpContext()?.Request.Query["lang"].ToString()) ?? SrdLanguage.En;
```

- replace the `AddMonster` method with:

```csharp
    /// <summary>
    /// Adds monsters from an SRD slug, one of the DM's templates or ad-hoc stats. The source is resolved
    /// before the session is loaded; its errors report the caller's <paramref name="expectedVersion"/>.
    /// </summary>
    public async Task<CommandResult> AddMonster(AddMonsterInput? input, int expectedVersion)
    {
        if (JoinedSessionId is null || CombatIdentity.From(Context.User) is not { } identity)
        {
            return CommandResult.Fail(CombatErrors.NotJoined, 0);
        }

        MonsterSeed seed;
        try
        {
            seed = await monsterSources.ResolveAsync(input, identity.Actor, Language, Context.ConnectionAborted);
        }
        catch (DomainException e)
        {
            return CommandResult.Fail(e.Code.ToString(), expectedVersion);
        }

        var request = input!;
        return await Run(expectedVersion, (s, actor, now) =>
            s.AddMonsters(actor, seed, request.Count, request.RollHp ? dice : null, now));
    }
```

- replace the `SetConditions` method with:

```csharp
    public Task<CommandResult> SetConditions(Guid combatantId, IReadOnlyList<Condition>? conditions, int expectedVersion) =>
        conditions is null
            ? Task.FromResult(CommandResult.Fail(nameof(DomainErrorCode.InvalidValue), expectedVersion))
            : Run(expectedVersion, (s, actor, now) => s.SetConditions(
                actor, combatantId, conditions.Aggregate(Condition.None, (all, c) => all | c), now));
```

In `Program.cs`, after `builder.Services.AddScoped<CombatCommandRunner>();`:

```csharp
builder.Services.AddScoped<MonsterSources>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `~/.claude/bin/heavy.sh dotnet test tests/DmCompanionLive.Api.IntegrationTests`
Expected: PASS (new hub tests and every existing test, including the Plan 2 hub tests now using `Monster(...)`).

Run: `~/.claude/bin/heavy.sh dotnet test DmCompanionLive.slnx`
Expected: PASS for all three test projects.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add monsters from the SRD, templates or ad-hoc stats over the hub

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Docs — attribution, README, specs and carry-over

**Files:**
- Create: `NOTICE.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-11-27-plan-3-srd-monsters-design.md`
- Modify: `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md`
- Modify: `docs/superpowers/plans/2026-11-24-plan-1-carryover.md`

**Interfaces:**
- Consumes: everything above. Produces no code.

- [ ] **Step 1: `NOTICE.md`**

Create `NOTICE.md` at the repository root:

```markdown
# Notices and attribution

The code of dm-companion-live is under the MIT licence (see `LICENSE`). The SRD monster data in
`src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json` is not code and is licensed as follows.

## System Reference Document 5.2.1

### English

This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

### Español

Esta obra incluye material procedente del documento de referencia del sistema 5.2.1 ("SRD 5.2.1") de Wizards of the Coast LLC, disponible en https://www.dndbeyond.com/srd. La licencia sobre el SRD 5.2.1 se concede de acuerdo con la licencia internacional de atribución/reconocimiento 4.0 de Creative Commons, disponible en https://creativecommons.org/licenses/by/4.0/legalcode.

dm-companion-live is not affiliated with, endorsed, sponsored or approved by Wizards of the Coast LLC.

## Sources and modifications

- **Spanish text and all numbers:** compendio-srd (https://github.com/Ismaelbase/compendio-srd, MIT tooling), commit `2d61b5dcabe91ab69d5c4a5a41822eb83c2038cc`, folder `datos/`, as normalised by dm-companion (`src/DmCompanion.Core/Glossary/Packs/criaturas.json` at commit `b95f1e65795d752d8ab167c60ff55751b38ec75b`).
- **English text:** TidyTuesday 2025-05-27 monsters dataset (https://github.com/rfordatascience/tidytuesday/tree/main/data/2025/2025-05-27), commit `97a250500c85463e590b23f9e195c58232a04584`, extracted from the SRD 5.2.1 PDF.
- **Modifications:** both sources are merged by English name (330 creatures, all present in both), converted to plain text (formatting and wrapped lines removed, hyphenated line breaks joined), split into language-neutral sections, and stored as JSON. AC, HP, hit dice, initiative and CR come from the Spanish source.
- Regenerate with `tools/DmCompanionLive.SrdImport` (see `README.md`).

The compendio-srd tooling is distributed under the MIT licence:

    MIT License

    Copyright (c) 2026 Ismael Sevilla

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
```

Before committing, compare the MIT text with `~/Personal/dm-companion-main/NOTICE.md` (it quotes compendio-srd's licence) and copy it from there verbatim if it differs.

- [ ] **Step 2: README**

Add a section `## SRD monsters, favourites and templates` after the real-time combat section (keep the README's tone and headings) with, in prose and short lists:
- the catalogue: 330 SRD 5.2.1 monsters in English and Spanish, held in memory; `GET /api/srd/monsters?q=&lang=&page=` searches both names ignoring case and accents (no typo tolerance), `GET /api/srd/monsters/{slug}?lang=` returns numbers and the stat block; `lang` (`en`/`es`) falls back to `Accept-Language`, then English;
- favourites: `PUT`/`DELETE /api/monsters/favorites/{slug}` (idempotent), `GET /api/monsters/favorites?lang=`;
- templates: CRUD under `/api/monsters/templates`, plus `POST /api/monsters/templates/from-srd/{slug}?lang=` to start from an SRD monster;
- the hub's `AddMonster(input, expectedVersion)` with `{ source: { srdSlug | templateId | custom }, count (1–20), rollHp, isHidden }`; names are numbered ("Goblin 1", "Goblin 2"…); SRD names use the hub connection's `?lang=`; errors `InvalidValue`, `MonsterNotFound`, `NotAllowed`;
- regenerating the data: the commands of Task 1 Step 7, and a pointer to `NOTICE.md` for attribution.

- [ ] **Step 3: Plan 3 design**

In `docs/superpowers/specs/2026-11-27-plan-3-srd-monsters-design.md`:
- §2: the Spanish input is the dm-companion creature pack generated from compendio-srd (both commits); the English input is pinned to TidyTuesday commit `97a250500c85463e590b23f9e195c58232a04584`; all 330 creatures match, so the importer fails on any mismatch (no Spanish fallback); describe the stat block shape (`subtitle`, `details`, `sections` with `kind`, `entries` with optional `name`).
- §4: `AddMonsters(actor, seed, count, hpDice, now)` where `hpDice` null means average HP; `AddMonster` kept as a wrapper; initiative bonus bounded to ±30; log type `MonstersAdded` with its parameters.
- §5: error code `MonsterNotFound` (not `NotFound`); source errors report the caller's `expectedVersion`; `SetConditions` with null conditions → `InvalidValue`.
- §6: the from-SRD route is `POST /api/monsters/templates/from-srd/{slug}?lang=`; remove `statBlockLang`; unknown slug → 404 `MonsterNotFound`, unknown or foreign template → bare 404.

- [ ] **Step 4: General spec**

In `docs/superpowers/specs/2026-11-24-dm-companion-live-design.md`:
- §4 REST list: replace the templates line with `GET/POST/PUT/DELETE /api/monsters/templates` (+ `POST /api/monsters/templates/from-srd/{slug}`), and the favourites line with `GET /api/monsters/favorites`, `PUT/DELETE /api/monsters/favorites/{slug}`.
- §4 rejections: add `MonsterNotFound` to the listed error codes.
- §7: the Spanish source line names the dm-companion pack and both commits; the English source line names the TidyTuesday commit.

- [ ] **Step 5: Carry-over**

In `docs/superpowers/plans/2026-11-24-plan-1-carryover.md`:
- the two Plan 3 items: replace `(Plan 3 — assigned, see …)` with `(done in Plan 3)`;
- under `## Opportunistic`, change the `Magic 80…` line to end with `(done in Plan 3: Names.MaxLength and Names.Validate)`;
- add at the end a section `## Plan 3 — deferred` with: "SRD search has no typo tolerance (in-memory prefix/substring match)"; "Favourites whose slug disappears after a new SRD import are skipped silently, never deleted"; "Templates copy stats into combat: editing a template does not change monsters already added" (Plan 4 must say so in the UI); "Footer and About page attribution (Plan 4)".

- [ ] **Step 6: Commit**

```bash
git add NOTICE.md README.md docs
git commit -m "docs: document SRD monsters, attribution and the Plan 3 carry-over

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Coverage

| Requirement | Task |
|---|---|
| Design §2 — import from both sources, merge by English name, deterministic `srd-monsters.json` | 1 |
| Design §3 — in-memory catalogue, accent-insensitive search in both languages, ranking, paging, startup failure | 2 |
| Design §4 — `InitiativeBonus`, `MonsterSource`, `HpFormula`, `AddMonsters` (naming, HP, one log entry), `MonsterTemplate`, `FavoriteMonster`, shared name validation | 3, 4, 5 |
| Design §5 — hub `AddMonster` with three sources, `lang`, errors | 8 |
| Design §6 — SRD, favourites and template REST endpoints; persistence and migrations | 4, 5, 6, 7 |
| Design §7 — attribution in `NOTICE.md` | 9 |
| Design §8 — domain, catalogue, importer and integration tests | 1–8 |
| Carry-over — null `AddMonster` / `SetConditions` arguments → `InvalidValue` | 8 |
| Carry-over — combatant added mid-round with a higher initiative acts next | 4 |
| Carry-over (opportunistic) — magic `80`, name validation repeated | 3 |
