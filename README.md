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
