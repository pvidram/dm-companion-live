namespace DmCompanionLive.Api.Features.Combat;

/// <summary>SignalR group names. Each guest has its own group because their view says which PC is theirs.</summary>
public static class CombatGroups
{
    public static string Dm(Guid sessionId) => $"session:{sessionId}:dm";

    public static string Players(Guid sessionId) => $"session:{sessionId}:players";

    public static string Guest(Guid sessionId, Guid guestId) => $"session:{sessionId}:guest:{guestId}";
}
