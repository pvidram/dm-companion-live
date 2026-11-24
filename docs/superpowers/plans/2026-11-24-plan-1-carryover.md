# Plan 1 — carry-over for later plans

Findings from the Plan 1 reviews that were deliberately deferred. Each later plan must pick up its items when it is written.

## Plan 2 (real-time combat)
- Register SignalR `AddJsonProtocol` with `JsonStringEnumConverter` so the hub sends the same view shape as REST (conditions are a list of enum names).
- `SetInitiative` during an active combat can skip or repeat a turn (A20, B15, C10, turn on B; B → 5 makes C lose its turn). Compute the next turn from the order before the change, or restrict guests to the lobby; add a test.
- `Controllable` looks the combatant up before authorizing: a guest can tell whether a hidden id exists. Authorize first for non-DM actors (together with filtering the player log).
- Validate `Condition` values: reject undefined bits with `InvalidValue`.
- Map `DbUpdateConcurrencyException` to the `Conflict` code (`expectedVersion` via `OriginalValue`); retry the join code on unique violation (SqlState 23505) and assert 23505 in the uniqueness test.
- Table deletion leaves sessions orphaned (no FK on `combat_sessions.TableId`/`OwnerId`): decide cascade/restrict/keep together with session expiry; add the FKs in a migration.
- Persistence test: add and remove combatants on an already-loaded session (the hub's main write path).
- `SerializerOptions.RespectNullableAnnotations = true` so `null` in non-nullable request strings is a 400, not a 500 (also protects hub inputs; fixes `/dev/login` with no name).
- `PlayerView.Version` increases when the DM acts on hidden combatants (side channel): accept and document, or don't expose `Version` to players.
- Normalize `now` to UTC (Npgsql rejects non-zero offsets for `timestamptz`).
- Cookie not revalidated against the database (`OnValidatePrincipal`) — with Google sign-in; validate `DmUser.externalId`; test the unique index on `ExternalId`.
- After an idempotent re-claim, still send the guest their snapshot.
- `JoinCode.Normalize(null)` throws: validate in `/join`.
- Decide whether a table can have only one active session; test unknown code → 404 and two sessions per table.
- Tests worth adding: `NextTurn` with everyone defeated, guest on DM-only commands, single-combatant wrap, removing a non-current combatant / in lobby, ended-session guard per command, negative `SetTempHp`, temp HP replaces rather than stacks.
- `Record` exposes a mutable dictionary as `IReadOnlyDictionary`: copy to an immutable one when persisting events.
- Use shared constants (or `ClaimTypes.Role`) for the `role`/`dm` claim literals.

## Plan 4 (frontend)
- Assert the ProblemDetails body (`code`) in invalid-input tests; test a blank rename.
- Antiforgery decision (see Plan 1 notes).

## Plan 5 (delivery)
- Persist ASP.NET Core Data Protection keys (cookie and `GuestToken` depend on them; scale-to-zero regenerates them).
- `ForwardedHeaders` behind the Container Apps ingress (needed by the OAuth redirect).
- Decide whether OpenAPI/Scalar are exposed in Production; set `AllowedHosts`.
- `.env.*` also ignores a future `.env.example` (add `!.env.example` if one is created).

## Opportunistic
- Magic `80` in `TableConfiguration` and `PlayerCharacter`; name validation repeated in three places.
