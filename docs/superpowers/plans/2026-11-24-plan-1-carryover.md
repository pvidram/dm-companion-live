# Plan 1 — carry-over for later plans

Findings from the Plan 1 reviews that were deliberately deferred. Each later plan must pick up its items when it is written.

## Plan 2 — deferred
- Clean up expired and ended sessions (and their log) with a periodic job; today they stay stored, readable and closed to commands.
- Presence and SignalR groups are in memory: a second replica needs a backplane (out of scope for the MVP).
- `RateLimiting` partitions by remote IP: needs `ForwardedHeaders` behind the ingress (Plan 5).
- Hub connections with the DM cookie rely on `SameSite=Lax` against cross-site use; revisit together with the antiforgery decision (Plan 4).
- Guest names are free text shown to the DM and other players: the SPA must render them as text (Plan 4).
- Clients must ignore snapshots and log batches older than the version they already hold (broadcasts after commit are not ordered) and de-duplicate log entries by `version` (the snapshot reads the session and the log tail in two queries) (Plan 4).

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
