namespace DmCompanionLive.Domain.Combat;

public enum HpBand
{
    Unhurt,
    Wounded,
    Bloodied,
    Defeated,
}

public sealed record DmCombatantView(
    Guid Id,
    CombatantKind Kind,
    string Name,
    int? Initiative,
    int MaxHp,
    int CurrentHp,
    int TempHp,
    int Ac,
    IReadOnlyList<Condition> Conditions,
    bool IsHidden,
    bool IsDefeated,
    Guid? ClaimedBy,
    string? SourceRef);

public sealed record DmView(
    Guid SessionId,
    string JoinCode,
    SessionStatus Status,
    int Round,
    Guid? CurrentTurnId,
    int Version,
    IReadOnlyList<DmCombatantView> Combatants);

public sealed record PlayerCombatantView(
    Guid Id,
    CombatantKind Kind,
    string Name,
    int? Initiative,
    int? MaxHp,
    int? CurrentHp,
    int? TempHp,
    HpBand? Band,
    IReadOnlyList<Condition> Conditions,
    bool IsDefeated,
    bool IsClaimed,
    bool IsMine);

/// <remarks>
/// <see cref="Version"/> also moves when the DM changes hidden combatants. Players need it as the
/// <c>expectedVersion</c> of their commands; it reveals that something happened, never what.
/// </remarks>
public sealed record PlayerView(
    Guid SessionId,
    SessionStatus Status,
    int Round,
    Guid? CurrentTurnId,
    int Version,
    IReadOnlyList<PlayerCombatantView> Combatants);

/// <summary>
/// Projections of a session. The player view is computed on the server so a player's
/// client never receives hidden combatants or exact monster hit points.
/// </summary>
public static class CombatViews
{
    public static DmView ForDm(CombatSession session) => new(
        session.Id,
        session.JoinCode,
        session.Status,
        session.Round,
        session.CurrentTurnId,
        session.Version,
        session.TurnOrder.Select(c => new DmCombatantView(
            c.Id, c.Kind, c.Name, c.Initiative, c.MaxHp, c.CurrentHp, c.TempHp, c.Ac,
            ExpandConditions(c.Conditions), c.IsHidden, c.IsDefeated, c.ClaimedBy, c.SourceRef)).ToList());

    public static PlayerView ForPlayer(CombatSession session, Guid? guestId)
    {
        var visible = session.TurnOrder.Where(c => !c.IsHidden).ToList();
        var currentIsVisible = visible.Any(c => c.Id == session.CurrentTurnId);
        return new PlayerView(
            session.Id,
            session.Status,
            session.Round,
            currentIsVisible ? session.CurrentTurnId : null,
            session.Version,
            visible.Select(c => ToPlayer(c, guestId)).ToList());
    }

    public static HpBand BandFor(int currentHp, int maxHp) => currentHp switch
    {
        <= 0 => HpBand.Defeated,
        _ when currentHp >= maxHp => HpBand.Unhurt,
        _ when currentHp * 2 > maxHp => HpBand.Wounded,
        _ => HpBand.Bloodied,
    };

    private static IReadOnlyList<Condition> ExpandConditions(Condition conditions) =>
        Enum.GetValues<Condition>().Where(f => f != Condition.None && conditions.HasFlag(f)).ToList();

    private static PlayerCombatantView ToPlayer(Combatant c, Guid? guestId)
    {
        var isPc = c.Kind == CombatantKind.Pc;
        return new PlayerCombatantView(
            c.Id,
            c.Kind,
            c.Name,
            c.Initiative,
            isPc ? c.MaxHp : null,
            isPc ? c.CurrentHp : null,
            isPc ? c.TempHp : null,
            isPc ? null : BandFor(c.CurrentHp, c.MaxHp),
            ExpandConditions(c.Conditions),
            c.IsDefeated,
            c.ClaimedBy is not null,
            guestId is not null && c.ClaimedBy == guestId);
    }
}
