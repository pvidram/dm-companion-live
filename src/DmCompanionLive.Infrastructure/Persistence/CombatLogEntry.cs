using System.Text.Json;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Infrastructure.Persistence;

/// <summary>A persisted <see cref="CombatEvent"/>. Append-only: rows are never updated.</summary>
public sealed class CombatLogEntry
{
    private CombatLogEntry()
    {
        ActorName = string.Empty;
        Type = string.Empty;
        Parameters = "{}";
    }

    public long Id { get; private set; }
    public Guid SessionId { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string ActorName { get; private set; }
    public string Type { get; private set; }

    /// <summary>The event parameters as a JSON object of strings (a <c>jsonb</c> column).</summary>
    public string Parameters { get; private set; }

    public bool VisibleToPlayers { get; private set; }

    public static CombatLogEntry From(Guid sessionId, CombatEvent e) => new()
    {
        SessionId = sessionId,
        Version = e.Version,
        At = e.At,
        ActorName = e.ActorName,
        Type = e.Type,
        Parameters = JsonSerializer.Serialize(e.Parameters),
        VisibleToPlayers = e.VisibleToPlayers,
    };

    public IReadOnlyDictionary<string, string> ReadParameters() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Parameters) ?? [];
}
