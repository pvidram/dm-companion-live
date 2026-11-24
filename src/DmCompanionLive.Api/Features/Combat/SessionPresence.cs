using System.Collections.Concurrent;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>
/// Which guests are connected to which session, so a change can send each guest their own view.
/// In memory: valid for a single replica only (a documented limitation).
/// </summary>
public sealed class SessionPresence
{
    private readonly ConcurrentDictionary<string, (Guid SessionId, Guid GuestId)> _guests = new();

    public void AddGuest(string connectionId, Guid sessionId, Guid guestId) => _guests[connectionId] = (sessionId, guestId);

    public void Remove(string connectionId) => _guests.TryRemove(connectionId, out _);

    public IReadOnlyList<Guid> GuestsIn(Guid sessionId) =>
        _guests.Values.Where(g => g.SessionId == sessionId).Select(g => g.GuestId).Distinct().ToList();
}
