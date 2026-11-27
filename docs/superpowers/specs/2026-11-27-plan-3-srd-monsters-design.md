# Plan 3 — SRD monsters, favourites and templates — Design

Refines §3, §4 and §7 of `2026-11-24-dm-companion-live-design.md` for Plan 3. Where the two disagree, this document wins and the general spec is updated to match.

## 1. Scope

In scope:
- An SRD 5.2.1 monster catalogue in English and Spanish, searchable by name in both languages.
- DM favourites over the catalogue.
- DM monster templates (CRUD, plus "duplicate from SRD").
- Adding monsters to a combat from the SRD, a template or ad-hoc stats, several at once, with average or rolled HP.
- Plan 3 carry-over: `InvalidValue` (not a generic hub error) for null or malformed `AddMonster` and `SetConditions` arguments; a test documenting that a combatant added mid-round with a higher initiative than the current one acts next.

Out of scope: typo-tolerant search; CR/type filters in the API (added if Plan 4 needs them); editable stat blocks; any UI, footer or About page (Plan 4).

## 2. Data import

`tools/SrdImport` is a .NET console run by hand (not in CI).

Inputs:
- **Spanish:** compendio-srd (https://github.com/Ismaelbase/compendio-srd, MIT tooling, CC-BY-4.0 content), commit `2d61b5dcabe91ab69d5c4a5a41822eb83c2038cc`, folder `datos/`, creatures only. It provides the structured numbers (AC, HP, hit dice, initiative bonus, CR, size, type, abilities), the English name (`nameEn`) and the Spanish stat block. Same source and commit as dm-companion.
- **English:** the TidyTuesday 2025-05-27 monsters dataset (https://rfordatascience.github.io/tidytuesday/data/2025/2025-05-27/readme.html), derived from SRD 5.2.1. The plan's first task downloads it and records its row count, columns and whether it carries trait/action text.

Merge: by normalised English name. The numbers come from the Spanish pack; the English dataset gives the English stat block text when present. Unmatched creatures are listed in an import report and in `NOTICE.md`, and keep only the Spanish stat block.

Output: `src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json`, an embedded resource, deterministic (sorted by slug, stable formatting) so diffs stay readable. Each entry:

| Field | Notes |
|---|---|
| `slug` | English name in kebab-case; unique |
| `name { en, es }` | |
| `type`, `size`, `cr` | Language-neutral keys |
| `ac`, `hpAverage`, `hpFormula`, `initiativeBonus`, `dexModifier` | Numbers; `hpFormula` as `NdM±K` |
| `speed`, `abilities[6]` | |
| `statBlock { en?, es }` | Plain-text sections (traits, actions, bonus actions, reactions, legendary actions), no HTML |

## 3. SRD catalogue (in memory)

`ISrdCatalog` is an Infrastructure singleton loaded once at startup from the embedded resource. The data is small (about 330 read-only entries) and the MVP runs a single replica, so there is no database table, seeder, migration or Postgres extension.

- `Search(q, lang, page)`: normalises the query and both names (`NormalizationForm.FormD`, combining marks removed, lower-case), matches against the English **and** Spanish names, ranks exact match, then prefix, then substring, then by name in the requested language. Page size 20. An empty query lists everything in name order.
- `Find(slug)`: the monster or `null`.
- A missing or malformed resource fails startup (no empty catalogue).

Trade-off accepted: no typo tolerance; favourites and templates validate slugs against the catalogue instead of a foreign key.

## 4. Domain

- `Combatant` and `MonsterSeed` gain `InitiativeBonus`, used to roll initiative. `DexModifier` stays only as the tie-break. An ad-hoc monster without `InitiativeBonus` uses its `DexModifier`. PCs keep their current behaviour.
- `SourceRef` becomes a value object: `Srd(slug)`, `Template(id)` or `None`, persisted as `source_kind` + `source_id`.
- `HpFormula` value object: parses `NdM`, `NdM+K`, `NdM-K` (also the Unicode minus) with `1 ≤ N ≤ 100`, `M ∈ {4, 6, 8, 10, 12, 20}`, `|K| ≤ 1000`; anything else is `InvalidValue`. `Roll(IDiceRoller)` returns at least 1.
- `CombatSession.AddMonsters(actor, seed, count, rollHp, dice, now)` replaces `AddMonster`:
  - DM only; open session only; `count` in `1..20`.
  - Names: with `count = 1` and no combatant already using the base name, the name stays plain. Otherwise names are numbered `"<base> N"`, continuing after the highest existing N (a plain `"<base>"` counts as 1).
  - HP: `seed.MaxHp` (average) by default; with `rollHp` and a formula, each monster rolls its own HP; without a formula, `rollHp` is ignored.
  - One `Version` bump and one log entry `MonstersAdded` with base name, count and combatant ids. Log params never carry HP (Plan 2 rule).
- `MonsterTemplate` (`Domain/Monsters`): `Id`, `OwnerId`, `Name`, `MaxHp`, `Ac`, `InitiativeBonus`, `HpFormula?`, `Notes`. Name validation and the `80` max length are shared with tables and PCs through one constant (closes the opportunistic carry-over item). `Notes` is plain text, max 2000.
- `FavoriteMonster`: composite key `(OwnerId, SrdSlug)`.

## 5. Hub

`AddMonster(code, expectedVersion, AddMonsterInput)`:

```
AddMonsterInput { Source, Count = 1, RollHp = false, IsHidden = false }
Source { SrdSlug? | TemplateId? | Custom? { Name, MaxHp, Ac, InitiativeBonus, HpFormula? } }
```

- Exactly one source member must be set; otherwise `InvalidValue`. Null input, null source or out-of-range values → `InvalidValue` (carry-over).
- The command runner resolves the source to a `MonsterSeed` before calling the domain: unknown slug, unknown template or a template owned by another DM → `NotFound` (same code for both template cases).
- SRD monsters use the name in the DM's language: the `lang` of the hub connection (query string `lang`, defaulting to `en`).
- Hub method name stays `AddMonster`; the old `MonsterInput` contract is removed (no client exists yet).

## 6. REST API

All endpoints require the DM cookie. Errors are `ProblemDetails` with `code`.

| Method and route | Result |
|---|---|
| `GET /api/srd/monsters?q=&lang=&page=` | `{ items: [{ slug, name, type, size, cr, ac, hpAverage, isFavorite }], page, total }` |
| `GET /api/srd/monsters/{slug}?lang=` | Full entry plus `statBlockLang` (`es` when the English text is missing); 404 `MonsterNotFound` |
| `GET /api/monsters/favorites?lang=` | Favourites resolved against the catalogue; slugs no longer in it are skipped |
| `PUT /api/monsters/favorites/{slug}` | 204, idempotent; 404 `MonsterNotFound` for an unknown slug |
| `DELETE /api/monsters/favorites/{slug}` | 204, idempotent |
| `GET /api/monsters/templates`, `GET /api/monsters/templates/{id}` | Own templates only |
| `POST /api/monsters/templates` | Body with the fields; or `?fromSrd={slug}&lang=` copies name (in `lang`) and numbers, empty `Notes` |
| `PUT /api/monsters/templates/{id}`, `DELETE /api/monsters/templates/{id}` | |

- `lang` defaults to the first supported language in `Accept-Language`, else `en`. An unsupported `lang` → 400 `InvalidValue`.
- Another DM's template → 404 (as with tables).
- Deleting a template does not touch combatants created from it: their stats were copied; `SourceRef` is informational.

Persistence: tables `monster_templates` and `favorite_monsters`, foreign key to `dm_users` with cascade delete; combatants gain `initiative_bonus` (back-filled from `dex_modifier`), `source_kind`, `source_id`. One migration.

## 7. Attribution

`NOTICE.md` lists both sources (repository, commit or publication date), the CC-BY-4.0 notice in English and Spanish, the modifications made (merged, normalised to JSON, plain text) and the Wizards of the Coast non-affiliation notice. Footer and About page are Plan 4.

## 8. Testing

- **Domain (unit):** `HpFormula` parsing, limits and rolling with a fake roller; `AddMonsters` naming, HP rolling, single log entry without HP, DM-only, ended session, count limits; initiative roll with `InitiativeBonus`; `MonsterTemplate` validation; mid-round higher-initiative combatant acts next (carry-over).
- **Infrastructure (unit):** catalogue search with and without accents, ranking, both languages, paging; the real resource loads (about 330 entries, unique slugs, no zero AC/HP); `SrdImport` against trimmed fixtures of both sources.
- **Integration (Testcontainers):** template CRUD with owner isolation; idempotent favourites; SRD endpoints and `lang` handling; hub `AddMonster` from all three sources plus `NotFound`, `InvalidValue` and another DM's template; the migration over existing combatants.

## 9. Task order (for the plan)

1. `SrdImport` and `srd-monsters.json`.
2. `ISrdCatalog` and SRD endpoints.
3. Templates and favourites (domain, persistence, endpoints).
4. Domain changes: `InitiativeBonus`, `SourceRef`, `HpFormula`, `AddMonsters`.
5. Hub `AddMonster` with source resolution.
6. `NOTICE.md`, README, general spec and carry-over updates.
