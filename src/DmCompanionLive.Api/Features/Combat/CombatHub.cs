using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Live combat. A connection first calls <see cref="Join"/> with the room code; from then on it
/// receives its view after every change and can send commands.
/// </summary>
[Authorize(Policy = AuthSetup.CombatPolicy)]
public sealed class CombatHub(
    AppDbContext db,
    SessionPresence presence,
    CombatCommandRunner runner,
    IDiceRoller dice) : Hub<ICombatClient>
{
    private const string SessionKey = "sessionId";
    private const int LogTail = 100;

    private Guid? JoinedSessionId =>
        Context.Items.TryGetValue(SessionKey, out var value) && value is Guid id ? id : null;

    public async Task<SnapshotResult> Join(string code)
    {
        if (CombatIdentity.From(Context.User) is not { } identity || !JoinCode.TryNormalize(code, out var normalized))
        {
            return SnapshotResult.Fail(CombatErrors.SessionNotFound);
        }

        var session = await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.JoinCode == normalized, Context.ConnectionAborted);

        // The same answer for "no such code" and "not yours": nobody learns which codes exist.
        if (session is null || !identity.CanJoin(session))
        {
            return SnapshotResult.Fail(CombatErrors.SessionNotFound);
        }

        if (identity.Actor is Actor.Guest guest)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Players(session.Id));
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Guest(session.Id, guest.GuestId));
            presence.AddGuest(Context.ConnectionId, session.Id, guest.GuestId);
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CombatGroups.Dm(session.Id));
        }

        Context.Items[SessionKey] = session.Id;
        return await SnapshotAsync(session, identity.Actor);
    }

    /// <summary>The full current view, e.g. after a reconnect or a <c>Conflict</c>.</summary>
    public async Task<SnapshotResult> GetSnapshot()
    {
        if (JoinedSessionId is not { } sessionId || CombatIdentity.From(Context.User) is not { } identity)
        {
            return SnapshotResult.Fail(CombatErrors.NotJoined);
        }

        var session = await db.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, Context.ConnectionAborted);
        return session is null
            ? SnapshotResult.Fail(CombatErrors.SessionNotFound)
            : await SnapshotAsync(session, identity.Actor);
    }

    public Task<CommandResult> AddMonster(MonsterInput monster, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.AddMonster(actor, monster.ToSeed(), now));

    public Task<CommandResult> RollMonsterInitiative(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.RollMonsterInitiative(actor, dice, now));

    public Task<CommandResult> Start(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Start(actor, now));

    public Task<CommandResult> NextTurn(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.NextTurn(actor, now));

    public Task<CommandResult> End(int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.End(actor, now));

    public Task<CommandResult> ApplyDamage(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.ApplyDamage(actor, combatantId, amount, now));

    public Task<CommandResult> Heal(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Heal(actor, combatantId, amount, now));

    public Task<CommandResult> SetTempHp(Guid combatantId, int amount, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetTempHp(actor, combatantId, amount, now));

    public Task<CommandResult> SetInitiative(Guid combatantId, int value, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetInitiative(actor, combatantId, value, now));

    public Task<CommandResult> SetConditions(Guid combatantId, IReadOnlyList<Condition> conditions, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.SetConditions(
            actor, combatantId, conditions.Aggregate(Condition.None, (all, c) => all | c), now));

    public Task<CommandResult> Reveal(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Reveal(actor, combatantId, now));

    public Task<CommandResult> Remove(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Remove(actor, combatantId, now));

    public Task<CommandResult> Claim(Guid combatantId, int expectedVersion) =>
        Run(expectedVersion, (s, actor, now) => s.Claim(actor, combatantId, now));

    private async Task<CommandResult> Run(int expectedVersion, Action<CombatSession, Actor, DateTimeOffset> command)
    {
        if (JoinedSessionId is not { } sessionId || CombatIdentity.From(Context.User) is not { } identity)
        {
            return CommandResult.Fail(CombatErrors.NotJoined, 0);
        }

        return await runner.RunAsync(
            sessionId,
            identity.Actor,
            expectedVersion,
            (session, now) => command(session, identity.Actor, now),
            Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task<SnapshotResult> SnapshotAsync(CombatSession session, Actor actor)
    {
        var isDm = actor is Actor.Dm;
        var latest = await db.CombatLog.AsNoTracking()
            .Where(e => e.SessionId == session.Id && (isDm || e.VisibleToPlayers))
            .OrderByDescending(e => e.Version)
            .Take(LogTail)
            .ToListAsync(Context.ConnectionAborted);
        var log = latest.OrderBy(e => e.Version).Select(LogEntryDto.From).ToList();

        return actor is Actor.Guest guest
            ? SnapshotResult.ForPlayer(CombatViews.ForPlayer(session, guest.GuestId), log)
            : SnapshotResult.ForDm(CombatViews.ForDm(session), log);
    }
}
