using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Runs one command: load the session → check <c>expectedVersion</c> → domain method → save the new
/// state and its log entries in one transaction (optimistic concurrency on <c>Version</c>) → broadcast.
/// </summary>
public sealed class CombatCommandRunner(AppDbContext db, TimeProvider clock, CombatBroadcaster broadcaster)
{
    public async Task<CommandResult> RunAsync(
        Guid sessionId,
        Actor actor,
        int expectedVersion,
        Action<CombatSession, DateTimeOffset> command,
        CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return CommandResult.Fail(CombatErrors.SessionNotFound, 0);
        }

        if (session.Version != expectedVersion)
        {
            return CommandResult.Fail(CombatErrors.Conflict, session.Version);
        }

        try
        {
            command(session, clock.GetUtcNow());
        }
        catch (DomainException e)
        {
            return CommandResult.Fail(e.Code.ToString(), session.Version);
        }

        var events = session.UncommittedEvents.ToList();
        if (events.Count > 0)
        {
            db.CombatLog.AddRange(events.Select(e => CombatLogEntry.From(session.Id, e)));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (
                e is DbUpdateConcurrencyException
                || e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another command saved this version first: either the Version check on the session row or
                // the unique (SessionId, Version) index on the log caught it, depending on statement order.
                return CommandResult.Fail(CombatErrors.Conflict, expectedVersion);
            }

            session.ClearUncommittedEvents();
        }

        // A command that changed nothing (re-claiming your own character) still re-sends the views,
        // so the caller always ends up with a fresh snapshot.
        await broadcaster.BroadcastAsync(session, events);
        return CommandResult.Ok(session.Version);
    }
}
