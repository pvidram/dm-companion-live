using System.Collections.Immutable;
using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

/// <summary>Aggregate root of a live combat. Every rule of the combat lives here.</summary>
public sealed class CombatSession
{
    /// <summary>A session nobody touches for this long expires and accepts no more commands.</summary>
    public static readonly TimeSpan InactivityLimit = TimeSpan.FromHours(12);

    private readonly List<Combatant> _combatants = [];
    private readonly List<CombatEvent> _uncommittedEvents = [];

    private CombatSession()
    {
        JoinCode = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid TableId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string JoinCode { get; private set; }
    public SessionStatus Status { get; private set; }
    public int Round { get; private set; }
    public Guid? CurrentTurnId { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public IReadOnlyList<Combatant> Combatants => _combatants;
    public IReadOnlyList<CombatEvent> UncommittedEvents => _uncommittedEvents;
    public IReadOnlyList<Combatant> TurnOrder => _combatants
        .OrderByDescending(c => c.Initiative ?? int.MinValue)
        .ThenByDescending(c => c.DexModifier)
        .ThenBy(c => c.SortOrder)
        .ToList();

    public static CombatSession Create(
        Guid tableId,
        Guid ownerId,
        string joinCode,
        IEnumerable<PcSeed> characters,
        DateTimeOffset now)
    {
        var session = new CombatSession
        {
            Id = Guid.NewGuid(),
            TableId = tableId,
            OwnerId = ownerId,
            JoinCode = joinCode,
            Status = SessionStatus.Lobby,
            LastActivityAt = now.ToUniversalTime(),
        };

        foreach (var character in characters)
        {
            session._combatants.Add(Combatant.CreatePc(character, session.NextSortOrder()));
        }

        return session;
    }

    /// <summary>True when the session is not ended and nobody has acted for longer than <see cref="InactivityLimit"/>.</summary>
    public bool IsExpiredAt(DateTimeOffset now) =>
        Status != SessionStatus.Ended && now - LastActivityAt > InactivityLimit;

    public Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var monster = Combatant.CreateMonster(seed, NextSortOrder());
        _combatants.Add(monster);
        Record(actor, now, "MonsterAdded", monster);
        return monster.Id;
    }

    public void ApplyDamage(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        EnsurePositive(amount);
        target.TakeDamage(amount);
        Record(actor, now, "DamageApplied", target, ("amount", amount));
    }

    public void Heal(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        EnsurePositive(amount);
        target.Heal(amount);
        Record(actor, now, "Healed", target, ("amount", amount));
    }

    public void SetTempHp(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        if (amount < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Temporary HP cannot be negative.");
        }

        target.SetTempHp(amount);
        Record(actor, now, "TempHpSet", target, ("amount", amount));
    }

    public void SetInitiative(Actor actor, Guid combatantId, int value, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        target.SetInitiative(value);
        Record(actor, now, "InitiativeSet", target, ("value", value));
    }

    public void RollMonsterInitiative(Actor actor, IDiceRoller dice, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        foreach (var monster in _combatants.Where(c => c.Kind == CombatantKind.Monster && c.Initiative is null))
        {
            monster.SetInitiative(dice.D20() + monster.DexModifier);
        }

        Record(actor, now, "MonsterInitiativeRolled");
    }

    public void Start(Actor actor, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        if (Status != SessionStatus.Lobby)
        {
            throw new DomainException(DomainErrorCode.InvalidState, "The combat has already started.");
        }

        if (!_combatants.Any(c => !c.IsDefeated))
        {
            throw new DomainException(DomainErrorCode.NoCombatants, "Nobody can act in this combat.");
        }

        Status = SessionStatus.Active;
        Round = 1;
        CurrentTurnId = null;
        TryAdvance(excluded: null);
        Record(actor, now, "CombatStarted", Current());
    }

    public void NextTurn(Actor actor, DateTimeOffset now)
    {
        EnsureActive(now);
        EnsureDm(actor);
        if (!TryAdvance(excluded: null))
        {
            throw new DomainException(DomainErrorCode.NoCombatants, "Nobody can act in this combat.");
        }

        Record(actor, now, "TurnAdvanced", Current(), ("round", Round));
    }

    public void Claim(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        if (actor is not Actor.Guest guest)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only players claim characters.");
        }

        // Monsters, hidden or not, answer exactly like unknown ids: a player learns nothing about them.
        var character = _combatants.SingleOrDefault(c => c.Id == combatantId && c.Kind == CombatantKind.Pc)
            ?? throw new DomainException(DomainErrorCode.CharacterNotFound, "There is no player character with that id.");

        if (character.ClaimedBy == guest.GuestId)
        {
            return;
        }

        if (character.ClaimedBy is not null)
        {
            throw new DomainException(DomainErrorCode.AlreadyClaimed, "Another player already controls this character.");
        }

        if (_combatants.Any(c => c.ClaimedBy == guest.GuestId))
        {
            throw new DomainException(DomainErrorCode.AlreadyHasCharacter, "You already control a character.");
        }

        character.ClaimBy(guest.GuestId);
        Record(actor, now, "CharacterClaimed", character);
    }

    public void SetConditions(Actor actor, Guid combatantId, Condition conditions, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId, now);
        if (!ConditionSet.IsValid(conditions))
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Unknown condition.");
        }

        target.SetConditions(conditions);
        Record(actor, now, "ConditionsSet", target, ("conditions", conditions));
    }

    public void Reveal(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var target = Find(combatantId);
        target.Reveal();
        Record(actor, now, "Revealed", target);
    }

    public void Remove(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        var target = Find(combatantId);
        if (Status == SessionStatus.Active && CurrentTurnId == target.Id && !TryAdvance(excluded: target.Id))
        {
            CurrentTurnId = null;
        }

        _combatants.Remove(target);
        Record(actor, now, "Removed", target);
    }

    public void End(Actor actor, DateTimeOffset now)
    {
        EnsureOpen(now);
        EnsureDm(actor);
        Status = SessionStatus.Ended;
        CurrentTurnId = null;
        Record(actor, now, "CombatEnded");
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>
    /// The combatant <paramref name="actor"/> may change. Guests are authorized before the lookup,
    /// so an error never tells them whether a (hidden) id exists.
    /// </summary>
    private Combatant Controllable(Actor actor, Guid combatantId, DateTimeOffset now)
    {
        EnsureOpen(now);
        return actor switch
        {
            Actor.Dm dm when dm.UserId == OwnerId => Find(combatantId),
            Actor.Guest guest => _combatants.SingleOrDefault(c => c.Id == combatantId && c.ClaimedBy == guest.GuestId)
                ?? throw new DomainException(
                    DomainErrorCode.NotYourCharacter, "You can only act on your own character."),
            _ => throw NotAllowed(),
        };
    }

    private Combatant Find(Guid combatantId) =>
        _combatants.SingleOrDefault(c => c.Id == combatantId)
        ?? throw new DomainException(DomainErrorCode.CombatantNotFound, $"Combatant {combatantId} not found.");

    private void EnsureDm(Actor actor)
    {
        if (actor is not Actor.Dm dm || dm.UserId != OwnerId)
        {
            throw NotAllowed();
        }
    }

    private void EnsureOpen(DateTimeOffset now)
    {
        if (Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }

        if (IsExpiredAt(now))
        {
            throw new DomainException(
                DomainErrorCode.SessionExpired, "The combat expired after 12 hours without activity.");
        }
    }

    private void EnsureActive(DateTimeOffset now)
    {
        EnsureOpen(now);
        if (Status != SessionStatus.Active)
        {
            throw new DomainException(DomainErrorCode.SessionNotActive, "The combat has not started.");
        }
    }

    private Combatant? Current() => _combatants.SingleOrDefault(c => c.Id == CurrentTurnId);

    /// <summary>
    /// Gives the turn to the first combatant in turn order that can act and has not begun a
    /// turn this round, skipping defeated ones and <paramref name="excluded"/>. When everybody
    /// has acted, a new round starts from the top of the order. Tracking turns per round instead
    /// of a position in the order keeps initiative changes during combat from skipping or
    /// repeating anyone's turn.
    /// </summary>
    private bool TryAdvance(Guid? excluded)
    {
        var able = TurnOrder.Where(c => !c.IsDefeated && c.Id != excluded).ToList();
        if (able.Count == 0)
        {
            return false;
        }

        var next = able.FirstOrDefault(c => c.LastTurnRound != Round);
        if (next is null)
        {
            Round++;
            next = able[0];
        }

        next.BeginTurn(Round);
        CurrentTurnId = next.Id;
        return true;
    }

    private static void EnsurePositive(int amount)
    {
        if (amount <= 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Amount must be greater than zero.");
        }
    }

    private static DomainException NotAllowed() =>
        new(DomainErrorCode.NotAllowed, "Only the DM of this session can do this.");

    private int NextSortOrder() => _combatants.Count == 0 ? 0 : _combatants.Max(c => c.SortOrder) + 1;

    private void Record(
        Actor actor,
        DateTimeOffset now,
        string type,
        Combatant? subject = null,
        params (string Key, object Value)[] data)
    {
        var at = now.ToUniversalTime();
        Version++;
        LastActivityAt = at;

        var parameters = new Dictionary<string, string>();
        if (subject is not null)
        {
            parameters["combatantId"] = subject.Id.ToString();
            parameters["name"] = subject.Name;
            parameters["kind"] = subject.Kind.ToString();
        }

        foreach (var (key, value) in data)
        {
            parameters[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _uncommittedEvents.Add(new CombatEvent(
            Version,
            at,
            ActorName(actor),
            type,
            parameters.ToImmutableDictionary(),
            VisibleToPlayers: subject is null || !subject.IsHidden));
    }

    private static string ActorName(Actor actor) => actor is Actor.Guest guest ? guest.Name : "DM";
}
