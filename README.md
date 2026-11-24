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

## Tests

```bash
dotnet test DmCompanionLive.slnx   # integration tests start PostgreSQL with Testcontainers (Docker required)
```

## License

[MIT](LICENSE). Not affiliated with, endorsed or sponsored by Wizards of the Coast.
