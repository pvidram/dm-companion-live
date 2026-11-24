# dm-companion-live — Design

**Date:** 2026-11-24 · **Status:** draft, pending author review

## 1. Purpose and scope

`dm-companion-live` is the online, multi-user companion to [dm-companion](https://github.com/pvidram/dm-companion) (an offline tablet app for Dungeon Masters). The DM runs a live D&D 5e combat from a PC or tablet; players join from their phones with a room code and see the initiative order, whose turn it is, and manage their own character's HP and conditions — all in real time.

It is a portfolio project: it must show full-stack skills (real-time, auth, multi-user, i18n, testing, IaC, CI/CD) with a public live demo that costs nothing to keep running.

### MVP — in scope
- DM signs in with Google; creates **tables** (campaigns) with their player characters (name, max HP, AC).
- DM starts a **combat session** that gets a 6-char join code (also shown as a QR code).
- Players join as **guests** (code + display name, no account), claim a free PC, and keep their identity across reloads.
- Live combat: initiative (DM sets/rolls for monsters, players set their own), turn and round tracking, damage, healing, temp HP, the 15 SRD conditions, hidden combatants revealed on demand, defeated combatants skipped.
- Players see monsters' HP only as a band (unhurt / wounded / bloodied / defeated), never exact numbers; hidden combatants are never sent to players.
- **Combat log** (append-only), rendered in each viewer's language.
- **Monsters:** search the SRD 5.2.1 bestiary, mark favourites, create custom monsters and save them as templates.
- **i18n:** UI in English and Spanish; SRD data in both languages.
- Responsive web app (PC + mobile), installable as a PWA (no offline mode).

### Out of scope (YAGNI)
Death saves, concentration, legendary actions, maps, full character sheets, dice roller, multiple replicas / Redis backplane, load testing, visual regression, DB branch-per-PR previews.

## 2. Architecture

Hybrid: a shared rich **domain** plus a **vertical-slice API**, server-authoritative state.

```
src/
  DmCompanionLive.Domain/          # pure .NET, no dependencies: CombatSession aggregate & rules, views
  DmCompanionLive.Infrastructure/  # EF Core + Npgsql, DbContext, migrations, SRD seeding
  DmCompanionLive.Api/             # ASP.NET Core 10: Features/{Auth,Tables,Monsters,Srd,Combat}, SignalR hub, serves SPA
web/                               # Vite + React + TypeScript SPA
tools/SrdImport/                   # console tool: SRD sources -> normalized srd-monsters.json
tests/
  DmCompanionLive.Domain.Tests/
  DmCompanionLive.Api.IntegrationTests/
  DmCompanionLive.SrdImport.Tests/
e2e/                               # Playwright
infra/                             # Bicep
```

Rules:
- Dependencies point inward: Api → Infrastructure → Domain. Domain references nothing.
- **Writes** go through the domain (`CombatSession` methods). **Reads** (SRD search, listings) query EF Core directly from the slice — no generic repositories.
- No MediatR / formal CQRS; each slice has its endpoint (or hub method) and a handler.
- Single process: the API serves the built SPA from `wwwroot` (same origin → simple cookie auth, no CORS).

## 3. Domain model

| Entity | Key data | Notes |
|---|---|---|
| `DmUser` | Id, GoogleSubject, DisplayName | No email stored |
| `Table` | Id, OwnerId, Name, `PlayerCharacter[]` (Name, MaxHp, Ac) | Owned by a DM |
| `CombatSession` (aggregate root) | Id, TableId, JoinCode, Status (`Lobby`/`Active`/`Ended`), Round, TurnIndex, Version, `Combatant[]`, LastActivityAt | Rules live here |
| `Combatant` | Id, Kind (`Pc`/`Monster`), Name, Initiative, MaxHp, CurrentHp, TempHp, Ac, Conditions (flags enum, 15 SRD), IsHidden, IsDefeated, SourceRef (SRD slug / template id / none), ClaimedBy (guest id) | |
| `MonsterTemplate` | Id, OwnerId, Name, MaxHp, Ac, InitiativeBonus, Notes | DM's custom monsters |
| `FavoriteMonster` | OwnerId, SrdSlug | |
| `SrdMonster` | Slug, Name{en,es}, Type, Size, Cr, Ac, HpAverage, HpFormula, Speed, Abilities, StatBlock{en,es} | Read-only reference data |
| `CombatLogEntry` | SessionId, Seq, At, Actor, Type, Params (JSON) | Append-only; stores type + params, not text |

Domain rules (all unit-tested):
- Initiative order: initiative desc, then Dex modifier / insertion order as tie-break.
- `NextTurn()` skips defeated combatants; wraps to the next round.
- HP clamped to `[0, MaxHp]`; damage consumes TempHp first; reaching 0 marks a monster defeated and a PC `Unconscious`.
- Players may only act on the combatant they claimed; no command succeeds on an `Ended` session.
- Every successful command increments `Version` and yields one `CombatLogEntry`. An idempotent command that changes nothing (re-claiming your own character) neither bumps `Version` nor logs.

Views computed server-side:
- `DmView`: full state.
- `PlayerView`: hidden combatants removed; monster HP replaced by a band (>= 100 % unhurt, > 50 % wounded, > 0 bloodied, 0 defeated); PC HP exact.

## 4. API and real time

REST (OpenAPI via `Microsoft.AspNetCore.OpenApi`, UI via Scalar, `ProblemDetails` errors):
- `GET/POST/PUT/DELETE /api/tables`, `/api/tables/{id}/characters`
- `GET/POST/PUT/DELETE /api/monsters/templates`; `GET/PUT/DELETE /api/monsters/favorites/{slug}`
- `GET /api/srd/monsters?q=&lang=&page=` and `GET /api/srd/monsters/{slug}?lang=`
- `POST /api/tables/{id}/sessions` → join code
- `POST /api/sessions/{code}/join` (name) → `GuestToken` — rate limited
- `GET /health` (includes DB check)

SignalR hub `/hubs/combat` (one group per session):
- Identity is resolved per connection (DM cookie or `GuestToken`); the caller receives its full view when it calls `Join(code)`.
- Commands (each with `expectedVersion`): `AddMonster`, `RollMonsterInitiative`, `Start`, `NextTurn`, `End`, `Reveal`, `Remove` (DM only); `ApplyDamage`, `Heal`, `SetTempHp`, `SetInitiative`, `SetConditions` (DM on anyone, a player on the character they claimed); `Claim` (players). The domain authorizes every command, so both roles share the same hub methods.
- Command pipeline: load session → domain method → save state + log entry in one transaction with optimistic concurrency on `Version` → broadcast `DmView` to the DM and `PlayerView` to players.
- Every command carries `expectedVersion`; mismatch → `Conflict`, client re-fetches the snapshot.
- Rejections return `CommandResult` with a localizable error code (`NotYourCharacter`, `SessionEnded`, `Conflict`, …). Domain exceptions are typed and mapped in one place.
- Reconnect: SignalR automatic reconnect + full snapshot request (no diffs).
- Sessions expire after 12 h of inactivity. One replica max (documented limitation).

## 5. Frontend (`web/`)

- Vite, React 19, TypeScript strict, React Router, Tailwind CSS + shadcn/ui (Radix).
- Feature folders: `src/features/{auth,tables,monsters,srd,combat}`, `src/shared/{api,ui,i18n}`.
- REST client generated from OpenAPI (`openapi-typescript` + `openapi-fetch`), cached with TanStack Query.
- `useCombatHub` hook owns the SignalR connection, sends commands with `expectedVersion`, writes snapshots into a Zustand store; components only read the store.
- Routes: `/` landing; `/tables`, `/tables/:id` (DM); `/monsters` (SRD search, favourites, templates); `/combat/:code` (DM: initiative list, combatant panel, log, join code + QR); `/join`, `/play/:code` (player, mobile-first: turn strip, "your turn" banner, own PC card with large HP buttons and conditions).
- i18n with `i18next` + `react-i18next` (EN/ES, browser detection + switcher); log entries translated client-side from type + params.
- PWA via `vite-plugin-pwa` (installable; no offline mode).
- Accessibility: DM keyboard shortcuts (`N` next turn, `D` damage), `aria-live` turn announcements, WCAG AA contrast.
- Cold start UX: landing shows "waking up the server…" while `/health` responds.

## 6. Authentication and authorization

- **DM:** ASP.NET Core cookie auth + Google provider. Cookie `HttpOnly`, `Secure`, `SameSite=Lax`. Only Google `sub` + display name stored. Antiforgery token sent as a header by the SPA.
- **Development / tests:** `/dev/login` creates a fake DM; registered only in `Development` and test environments, never in production.
- **Guest:** `GuestToken` signed with ASP.NET Core Data Protection (session id + guest id, 12 h expiry), sent as header and via SignalR `accessTokenFactory`, stored in `localStorage` per room code.
- Join codes: 6 chars from an unambiguous alphabet (no `0/O/1/I`); `/join` rate limited.
- Authorization: "table owner" policy on DM endpoints; per-command check in the hub (DM vs. claiming guest).

## 7. SRD data and i18n

- `tools/SrdImport` normalizes sources into `srd-monsters.json` (committed, with attribution). Infrastructure seeds it idempotently at startup, keyed by a content hash.
- Spanish source: the Spanish SRD 5.2.1 creature pack derived from compendio-srd (MIT tooling, CC-BY-4.0 content), as used in dm-companion.
- **Open risk — English source:** a structured, open SRD **5.2.1** English dataset must be found during planning. Fallbacks, in order: extract from the official SRD 5.2.1 PDF in `SrdImport`; or use SRD 5.1 English (also CC-BY-4.0) and document the version mismatch.
- Search: Postgres `pg_trgm` + `unaccent` on the name in the requested language.
- Attribution (CC-BY-4.0, modifications stated, WotC non-affiliation notice) in footer, About page and `NOTICE.md`. Code under MIT.
- User-created content (templates, PC names) is not translated.

## 8. Testing

- **Domain tests** (xUnit + AwesomeAssertions): all rules in §3; the bulk of the suite; TDD starts here. Target ≥ 90 % coverage on Domain.
- **Integration tests** (`WebApplicationFactory` + Testcontainers PostgreSQL): REST slices with real migrations; SRD search with `pg_trgm`/`unaccent`; optimistic concurrency conflict; SignalR with real `HubConnection` clients (1 DM + 2 players) asserting each receives its view and **players never receive monster HP or hidden combatants**; auth via `/dev/login` and `GuestToken`.
- **OpenAPI contract test:** generated document must match the committed one.
- **SrdImport tests:** small fixtures → snapshot of normalized output.
- **Frontend:** Vitest + Testing Library for components and `useCombatHub` (fake connection: snapshot apply, conflict and reconnect re-fetch); MSW for REST mocks.
- **E2E (Playwright, docker-compose):** (1) DM dev-login → table + PCs → add SRD monster → start combat; (2) mobile-viewport player joins, claims PC, sees turn, takes damage, DM sees it live (two browser contexts); (3) EN → ES switch in player view.
- Locally, heavy test runs go through the machine's global lock script and are run by subagents.

## 9. Infrastructure, CI/CD and cost

- **Local:** `docker-compose.yml` (Postgres 17 + app; `e2e` profile). Dev loop: `dotnet watch` + `pnpm dev` with Vite proxy to the API.
- **Image:** multi-stage Dockerfile (node build → .NET publish → `aspnet` chiseled, non-root), `HEALTHCHECK` on `/health`.
- **Production target (prepared, not deployed):**
  - Azure Container Apps (Consumption, scale 0–1, HTTPS ingress, sticky sessions) via Bicep in `infra/`; secrets (Neon connection strings, Google OAuth) as Container App secrets passed as parameters.
  - PostgreSQL on **Neon free tier** (region close to the Azure region); app uses the pooled connection, migrations use the direct connection.
  - Images in GitHub Container Registry.
- **CI (`ci.yml`, PRs and `main`):** build back + front; domain, integration (Testcontainers), Vitest; OpenAPI contract; ESLint + `dotnet format --verify-no-changes`; Playwright E2E (traces uploaded on failure); `az bicep build`; coverage summary.
- **Release (`release.yml`, `main`):** build and push image to GHCR with `GITHUB_TOKEN`; Azure deploy step present but gated by `vars.AZURE_ENABLED == 'true'`, using GitHub → Azure OIDC federation (no stored Azure credentials).
- Dependabot for NuGet, npm and Actions.
- `docs/deploy.md`: step-by-step for when accounts exist (Google Cloud OAuth client, Neon project, Azure subscription + OIDC federation, enable `AZURE_ENABLED`).
- **No cloud account is created or used during development.** Expected running cost once deployed: ~0 € (Container Apps free grant at demo traffic, Neon free, GHCR and Actions free for public repos); verify current free-tier limits before deploying.

## 10. Repository conventions

- Public repo `pvidram/dm-companion-live`, MIT license, English README with live-demo link (once deployed), `NOTICE.md`.
- Conventional commits; feature branches and pull requests from the first feature onward.
