using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Messages the server pushes to connected clients.</summary>
public interface ICombatClient
{
    Task DmSnapshot(DmView view);

    Task PlayerSnapshot(PlayerView view);

    Task LogAppended(IReadOnlyList<LogEntryDto> entries);
}

/// <summary>A log entry as clients see it: a type and parameters they translate themselves.</summary>
public sealed record LogEntryDto(
    int Version,
    DateTimeOffset At,
    string Actor,
    string Type,
    IReadOnlyDictionary<string, string> Parameters)
{
    public static LogEntryDto From(CombatLogEntry entry) =>
        new(entry.Version, entry.At, entry.ActorName, entry.Type, entry.ReadParameters());

    public static LogEntryDto From(CombatEvent e) =>
        new(e.Version, e.At, e.ActorName, e.Type, e.Parameters);

    /// <summary>
    /// The entry as players may see it: players never learn exact monster hit points, so heals and
    /// temporary HP about a monster lose their amount. Damage keeps it (players know what they dealt).
    /// </summary>
    public LogEntryDto ForPlayers()
    {
        if (Parameters.TryGetValue("kind", out var kind)
            && kind == nameof(CombatantKind.Monster)
            && Type is "Healed" or "TempHpSet"
            && Parameters.ContainsKey("amount"))
        {
            return this with
            {
                Parameters = Parameters.Where(p => p.Key != "amount").ToDictionary(p => p.Key, p => p.Value),
            };
        }

        return this;
    }
}

/// <summary>The caller's view of the session plus the latest log entries it may see.</summary>
public sealed record SnapshotResult(string? Error, DmView? Dm, PlayerView? Player, IReadOnlyList<LogEntryDto> Log)
{
    public bool Succeeded => Error is null;

    public static SnapshotResult ForDm(DmView view, IReadOnlyList<LogEntryDto> log) => new(null, view, null, log);

    public static SnapshotResult ForPlayer(PlayerView view, IReadOnlyList<LogEntryDto> log) => new(null, null, view, log);

    public static SnapshotResult Fail(string error) => new(error, null, null, []);
}

/// <summary>
/// Outcome of a command. <see cref="Version"/> is the session version after a success, or the
/// version the server knows after a failure; on <c>Conflict</c> the client re-fetches the snapshot.
/// </summary>
public sealed record CommandResult(string? Error, int Version)
{
    public bool Succeeded => Error is null;

    public static CommandResult Ok(int version) => new(null, version);

    public static CommandResult Fail(string error, int version) => new(error, version);
}

/// <summary>Error codes the hub adds to the domain's <see cref="DomainErrorCode"/> names.</summary>
public static class CombatErrors
{
    public const string SessionNotFound = "SessionNotFound";
    public const string NotJoined = "NotJoined";
    public const string Conflict = nameof(DomainErrorCode.Conflict);
}

/// <summary>A monster the DM adds by hand (SRD monsters and templates come in Plan 3).</summary>
public sealed record MonsterInput(string Name, int MaxHp, int Ac, int DexModifier = 0, bool IsHidden = false)
{
    public MonsterSeed ToSeed() => new(Name, MaxHp, Ac, DexModifier, IsHidden);
}
