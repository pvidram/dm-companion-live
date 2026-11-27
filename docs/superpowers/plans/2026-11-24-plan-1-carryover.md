# Plan 1 — carry-over for later plans

Findings from the Plan 1 reviews that were deliberately deferred. Each later plan must pick up its items when it is written.

## Plan 2 — deferred
- Clean up expired and ended sessions (and their log) with a periodic job; today they stay stored, readable and closed to commands.
- Presence and SignalR groups are in memory: a second replica needs a backplane (out of scope for the MVP).
- `RateLimiting` partitions by remote IP: needs `ForwardedHeaders` behind the ingress (Plan 5).
- Hub connections with the DM cookie rely on `SameSite=Lax` against cross-site use; revisit together with the antiforgery decision (Plan 4).
- Guest names are free text shown to the DM and other players: the SPA must render them as text (Plan 4).
- Clients must ignore snapshots and log batches older than the version they already hold (broadcasts after commit are not ordered) and de-duplicate log entries by `version` (the snapshot reads the session and the log tail in two queries) (Plan 4).
- A DM `ReleaseClaim` command: a lost guest token (expiry, key loss, cleared storage) leaves the PC claimed by a guest who can never come back (Plan 4).
- Open hub connections are not re-checked after guest-token expiry, DM user deletion or session end: use `CloseOnAuthenticationExpiration` and a client reconnect policy (Plan 4).
- A DM re-`Join` on the same connection keeps the previous session's DM group: leave the old groups or reject a second session (Plan 4).
- A broadcast failure after commit reaches the caller as a hub error although the command was saved; the client must treat hub errors as "re-fetch the snapshot" (Plan 4).
- Null `MonsterInput`/conditions arguments surface as a generic hub error instead of `InvalidValue`; revisit when AddMonster inputs change with SRD monsters (done in Plan 3).
- Test (and document as intended) that a combatant added mid-round with a higher initiative than the current one acts next (done in Plan 3).
- Concurrent first Google sign-in of the same account hits the unique index (23505 gives a 500): retry the lookup on conflict. Assert that `returnUrl` round-trips through the real OAuth flow (Plan 5).
- Throttle hub commands per connection: an idempotent `Claim` re-broadcasts to everyone (Plan 5).
- `DmUsers.DisplayNameFrom` may split a surrogate pair when truncating; rename `GuestTokenHandler.Scheme` to `SchemeName` (it hides the base `Scheme` property); remove `JoinCode.Normalize` (only tests use it) (opportunistic).

## Plan 4 (frontend)
- Assert the ProblemDetails body (`code`) in invalid-input tests; test a blank rename.
- Antiforgery decision (see Plan 1 notes).

## Plan 5 (delivery)
- Persist ASP.NET Core Data Protection keys (cookie and `GuestToken` depend on them; scale-to-zero regenerates them).
- `ForwardedHeaders` behind the Container Apps ingress (needed by the OAuth redirect).
- Decide whether OpenAPI/Scalar are exposed in Production; set `AllowedHosts`.
- `.env.*` also ignores a future `.env.example` (add `!.env.example` if one is created).

## Opportunistic
- Magic `80` in `TableConfiguration` and `PlayerCharacter`; name validation repeated in three places (done in Plan 3: `Names.MaxLength` and `Names.Validate`)

## Plan 3 — deferred
- SRD search has no typo tolerance (in-memory prefix/substring match).
- Favourites whose slug disappears after a new SRD import are skipped silently, never deleted.
- Templates copy stats into combat: editing a template does not change monsters already added (Plan 4 must say so in the UI).
- Footer and About page attribution (Plan 4).
