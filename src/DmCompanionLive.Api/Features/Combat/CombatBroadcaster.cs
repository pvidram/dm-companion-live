using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Sends every participant of a session the view and log entries they are allowed to see.</summary>
public sealed class CombatBroadcaster(IHubContext<CombatHub, ICombatClient> hub, SessionPresence presence)
{
    public async Task BroadcastAsync(CombatSession session, IReadOnlyList<CombatEvent> events)
    {
        var dm = hub.Clients.Group(CombatGroups.Dm(session.Id));
        await dm.DmSnapshot(CombatViews.ForDm(session));
        if (events.Count > 0)
        {
            await dm.LogAppended(events.Select(LogEntryDto.From).ToList());
        }

        foreach (var guestId in presence.GuestsIn(session.Id))
        {
            await hub.Clients.Group(CombatGroups.Guest(session.Id, guestId))
                .PlayerSnapshot(CombatViews.ForPlayer(session, guestId));
        }

        var visible = events.Where(e => e.VisibleToPlayers).Select(LogEntryDto.From).ToList();
        if (visible.Count > 0)
        {
            await hub.Clients.Group(CombatGroups.Players(session.Id)).LogAppended(visible);
        }
    }
}
