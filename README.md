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
# API docs: http://localhost:5198/scalar/v1 · dev sign-in: POST /dev/login { "displayName": "..." }
```

## Real-time combat

The flow:

1. The DM starts a session (`POST /api/tables/{id}/sessions`) and shares its join code.
2. Players `POST /api/sessions/{code}/join` with a name and get `{ sessionId, guestId, name, guestToken }`.
3. Everyone connects to `/hubs/combat` (the DM with the cookie, players with the token via `accessTokenFactory`) and calls `Join(code)`.

Hub methods: `Join(code)`, `GetSnapshot()` and the commands `AddMonster`, `RollMonsterInitiative`, `Start`, `NextTurn`, `End`, `Reveal`, `Remove`, `ApplyDamage`, `Heal`, `SetTempHp`, `SetInitiative`, `SetConditions` and `Claim`. Every command takes `expectedVersion` as its last argument and returns `{ error, version }`; on `Conflict` the client calls `GetSnapshot()`. The domain decides who may do what, so DM and players share the same methods.

The server pushes `DmSnapshot`, `PlayerSnapshot` and `LogAppended`. Players never receive hidden combatants, exact monster HP or log entries about hidden combatants.

Limits:

- One replica: presence and SignalR groups are in memory.
- Sessions expire after 12 h without activity (`/join` returns 410, commands return `SessionExpired`).
- `/join` is rate limited per remote IP (`RateLimiting:JoinPermitsPerMinute`, default 10).

## SRD monsters, favourites and templates

The catalogue holds the 330 monsters of the SRD 5.2.1 in English and Spanish, loaded in memory at startup from an embedded `srd-monsters.json`. All endpoints below are DM-only.

- `GET /api/srd/monsters?q=&lang=&page=` searches both the English and Spanish names, ignoring case and accents (no typo tolerance), 20 per page. `GET /api/srd/monsters/{slug}?lang=` returns the numbers and the stat block. `lang` (`en`/`es`) falls back to `Accept-Language`, then English; an unknown `lang` is a 400 `InvalidValue`, an unknown slug a 404 `MonsterNotFound`.
- Favourites: `PUT` and `DELETE /api/monsters/favorites/{slug}` (both idempotent) and `GET /api/monsters/favorites?lang=`.
- Templates: CRUD under `/api/monsters/templates`, plus `POST /api/monsters/templates/from-srd/{slug}?lang=` to start from an SRD monster. A template is copied into the combat when used, so editing it later does not change monsters already added.

To add monsters to a combat, connect to `/hubs/combat?lang=es` (the language used for SRD names) and call `AddMonster(input, expectedVersion)` with `{ source: { srdSlug | templateId | custom }, count (1-20), rollHp, isHidden }`. `custom` is `{ name, maxHp, ac, initiativeBonus, hpFormula? }`. A single monster whose name nobody uses keeps the plain name; otherwise names are numbered ("Goblin 1", "Goblin 2"...), counting only visible monsters when adding visible ones, HP is the average unless `rollHp` is set and the monster has a formula. Errors: `InvalidValue`, `MonsterNotFound`, `NotAllowed`.

Regenerating the data (only when the sources change; the Spanish pack comes from a local clone of dm-companion):

```bash
WORK="${TMPDIR:-/tmp}/srd-import" && mkdir -p "$WORK"
git -C ~/Personal/dm-companion-main show \
  b95f1e65795d752d8ab167c60ff55751b38ec75b:src/DmCompanion.Core/Glossary/Packs/criaturas.json > "$WORK/criaturas.json"
curl -sSfL -o "$WORK/monsters.csv" \
  https://raw.githubusercontent.com/rfordatascience/tidytuesday/97a250500c85463e590b23f9e195c58232a04584/data/2025/2025-05-27/monsters.csv
echo "0f3ba32d721af74e66a92bbb2d5eae6cb394796c2887da81c88af571a82cedf4  $WORK/monsters.csv" | sha256sum -c
dotnet run --project tools/DmCompanionLive.SrdImport -- \
  "$WORK/criaturas.json" "$WORK/monsters.csv" src/DmCompanionLive.Infrastructure/Srd/srd-monsters.json
```

The data is licensed CC-BY-4.0; see [NOTICE.md](NOTICE.md) for the attribution and the sources.

## Google sign-in (optional)

Off unless both `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are set, e.g. locally with

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>" --project src/DmCompanionLive.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>" --project src/DmCompanionLive.Api
```

The authorized redirect URI is `https://<host>/signin-google`. Login starts at `GET /api/auth/google/login?returnUrl=/…` (only local return URLs are kept). Only the Google subject and display name are stored.

## Tests

```bash
dotnet test DmCompanionLive.slnx   # integration tests start PostgreSQL with Testcontainers (Docker required)
```

## License

[MIT](LICENSE). Not affiliated with, endorsed or sponsored by Wizards of the Coast.
