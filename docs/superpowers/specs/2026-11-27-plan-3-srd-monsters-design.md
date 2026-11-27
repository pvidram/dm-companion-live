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

`tools/DmCompanionLive.SrdImport` is a .NET console run by hand (not in CI).

Inputs:
- **Spanish:** the dm-companion creature pack (`src/DmCompanion.Core/Glossary/Packs/criaturas.json` at dm-companion commit `b95f1e65795d752d8ab167c60ff55751b38ec75b`), generated from compendio-srd (https://github.com/Ismaelbase/compendio-srd, MIT tooling, CC-BY-4.0 content), commit `2d61b5dcabe91ab69d5c4a5a41822eb83c2038cc`, folder `datos/`. It provides the structured numbers (AC, HP, hit dice, initiative bonus, CR, size, type, abilities), the English name (`nameEn`) and the Spanish stat block.
- **English:** the TidyTuesday 2025-05-27 monsters dataset (https://github.com/rfordatascience/tidytuesday/tree/main/data/2025/2025-05-27), derived from SRD 5.2.1, pinned to commit `97a250500c85463e590b23f9e195c58232a04584`.

Merge: by normalised English name. The numbers come from the Spanish pack; the English dataset gives the English stat block text. All 330 creatures match in both sources, so the importer fails on any mismatch (there is no Spanish fallback) and lists the unmatched names.

Stat block shape (per language): `subtitle`, `details` (labelled lines such as senses or languages) and `sections`, each with a language-neutral `kind` (traits, actions, bonus actions, reactions, legendary actions) and `entries`, each with an optional `name` (null for untitled text such as the legendary actions intro) and plain `text`.

Output: `src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json`, an embedded resource, deterministic (sorted by slug, stable formatting) so diffs stay readable. Each entry:

| Field | Notes |
|---|---|
| `slug` | English name in kebab-case; unique |
| `name { en, es }` | |
| `type`, `sizes`, `cr` | Language-neutral keys; `sizes` is a list (some monsters have two sizes) |
| `ac`, `hpAverage`, `hpFormula`, `initiativeBonus`, `dexModifier` | Numbers; `hpFormula` as `NdM±K` |
| `abilities[6]` | Speed is not a field: it lives in the stat block `details` |
| `statBlock { en, es }` | `subtitle`, `details` and `sections` (see above), plain text, no HTML |

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
- `CombatSession.AddMonsters(actor, seed, count, hpDice, now)` replaces `AddMonster`, which stays as a wrapper for one monster with average HP. `hpDice` is an `IDiceRoller?`; null means average HP:
  - DM only; open session only; `count` in `1..20`.
  - Names: with `count = 1` and no combatant already using the base name, the name stays plain. Otherwise names are numbered `"<base> N"`, continuing after the highest existing N (a plain `"<base>"` counts as 1). Numeric suffixes above 1 000 000 are ignored (treated as unrelated names), so the numbering cannot overflow. A visible monster is numbered only against the names of visible combatants, so the numbers never reveal hidden monsters; a hidden monster is numbered against every combatant (only the DM sees it). After a later Reveal two combatants may share a display name (ids differ; renaming is Plan 4).
  - HP: `seed.MaxHp` (average) when `hpDice` is null; otherwise each monster rolls its own HP from the formula; without a formula, the average is used. The hub passes the dice only when `rollHp` is set.
  - `InitiativeBonus` is bounded to ±30.
  - One `Version` bump and one log entry of type `MonstersAdded` with parameters `baseName`, `count` and `combatantIds` (comma-separated). Log params never carry HP (Plan 2 rule).
- `MonsterTemplate` (`Domain/Monsters`): `Id`, `OwnerId`, `Name`, `MaxHp`, `Ac`, `InitiativeBonus`, `HpFormula?`, `Notes`. Name validation and the `80` max length are shared with tables and PCs through one constant (closes the opportunistic carry-over item). `Notes` is plain text, max 2000.
- `FavoriteMonster`: composite key `(OwnerId, SrdSlug)`.

## 5. Hub

`AddMonster(AddMonsterInput? input, int expectedVersion)`:

```
AddMonsterInput { Source, Count = 1, RollHp = false, IsHidden = false }
Source { SrdSlug? | TemplateId? | Custom? { Name, MaxHp, Ac, InitiativeBonus, HpFormula? } }
```

- Exactly one source member must be set; otherwise `InvalidValue`. Null input, null source or out-of-range values → `InvalidValue` (carry-over).
- `MonsterSources`, called from the hub before the command runner, resolves the source to a `MonsterSeed` before calling the domain: unknown slug, unknown template or a template owned by another DM → `MonsterNotFound` (same code for both template cases). Source errors report the caller's `expectedVersion`.
- `SetConditions` with null conditions → `InvalidValue`.
- SRD monsters use the name in the DM's language: the `lang` of the hub connection (query string `lang`, defaulting to `en`). Error codes: `InvalidValue`, `MonsterNotFound`, `NotAllowed`.
- Hub method name stays `AddMonster`; the old `MonsterInput` contract is removed (no client exists yet).

## 6. REST API

All endpoints require the DM cookie. Errors are `ProblemDetails` with `code`.

| Method and route | Result |
|---|---|
| `GET /api/srd/monsters?q=&lang=&page=` | `{ items: [{ slug, name, type, sizes, cr, ac, hpAverage, isFavorite }], page, total }` |
| `GET /api/srd/monsters/{slug}?lang=` | Full entry with the stat block in `lang`; 404 `MonsterNotFound` |
| `GET /api/monsters/favorites?lang=` | Favourites resolved against the catalogue; slugs no longer in it are skipped |
| `PUT /api/monsters/favorites/{slug}` | 204, idempotent; 404 `MonsterNotFound` for an unknown slug |
| `DELETE /api/monsters/favorites/{slug}` | 204, idempotent |
| `GET /api/monsters/templates`, `GET /api/monsters/templates/{id}` | Own templates only |
| `POST /api/monsters/templates` | Body with the fields |
| `POST /api/monsters/templates/from-srd/{slug}?lang=` | Copies name (in `lang`) and numbers, empty `Notes`; unknown slug → 404 `MonsterNotFound` |
| `PUT /api/monsters/templates/{id}`, `DELETE /api/monsters/templates/{id}` | |

- `lang` defaults to the first supported language in `Accept-Language`, else `en`. An unsupported `lang` → 400 `InvalidValue`. `page` must be 1-10 000 and `q` at most 100 characters, otherwise 400 `InvalidValue`.
- Another DM's template, or an unknown template id → bare 404 (as with tables).
- Deleting a template does not touch combatants created from it: their stats were copied; `SourceRef` is informational.

Persistence: two migrations. `MonsterSources`: combatants gain `InitiativeBonus` (back-filled from `DexModifier`), `SourceRef` is renamed to `SourceId` and `SourceKind` is added (legacy `srd:<slug>` and `template:<id>` values are converted). `MonsterTemplates`: tables `monster_templates` and `favorite_monsters`, foreign key to `dm_users` with cascade delete. Column names are PascalCase as EF generates them (tables are snake_case).

## 7. Attribution

`NOTICE.md` lists both sources (repository and pinned commit), the CC-BY-4.0 notice in English and Spanish, the modifications made (merged, normalised to JSON, plain text) and the Wizards of the Coast non-affiliation notice. Footer and About page are Plan 4.

## 8. Testing

- **Domain (unit):** `HpFormula` parsing, limits and rolling with a fake roller; `AddMonsters` naming, HP rolling, single log entry without HP, DM-only, ended session, count limits; initiative roll with `InitiativeBonus`; `MonsterTemplate` validation; mid-round higher-initiative combatant acts next (carry-over).
- **Infrastructure (unit):** catalogue search with and without accents, ranking, both languages, paging; the real resource loads (about 330 entries, unique slugs, no zero AC/HP); `SrdImport` against trimmed fixtures of both sources.
- **Integration (Testcontainers):** template CRUD with owner isolation; idempotent favourites; SRD endpoints and `lang` handling; hub `AddMonster` from all three sources plus `MonsterNotFound`, `InvalidValue` and another DM's template; the migration over existing combatants.

## 9. Task order (for the plan)

1. `DmCompanionLive.SrdImport` and `srd-monsters.json`.
2. `ISrdCatalog` and SRD endpoints.
3. Templates and favourites (domain, persistence, endpoints).
4. Domain changes: `InitiativeBonus`, `SourceRef`, `HpFormula`, `AddMonsters`.
5. Hub `AddMonster` with source resolution.
6. `NOTICE.md`, README, general spec and carry-over updates.
