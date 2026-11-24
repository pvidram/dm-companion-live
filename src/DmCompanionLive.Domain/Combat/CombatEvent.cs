namespace DmCompanionLive.Domain.Combat;

/// <summary>
/// One entry of the combat log. Stores a type and parameters, never rendered text,
/// so each client can translate it into its own language.
/// </summary>
public sealed record CombatEvent(
    int Version,
    DateTimeOffset At,
    string ActorName,
    string Type,
    IReadOnlyDictionary<string, string> Parameters);
