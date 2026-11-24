using System.Globalization;

namespace DmCompanionLive.Domain.Combat;

/// <summary>Aggregate root of a live combat. Every rule of the combat lives here.</summary>
public sealed class CombatSession
{
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
            LastActivityAt = now,
        };

        foreach (var character in characters)
        {
            session._combatants.Add(Combatant.CreatePc(character, session.NextSortOrder()));
        }

        return session;
    }

    public Guid AddMonster(Actor actor, MonsterSeed seed, DateTimeOffset now)
    {
        EnsureNotEnded();
        EnsureDm(actor);
        var monster = Combatant.CreateMonster(seed, NextSortOrder());
        _combatants.Add(monster);
        Record(actor, now, "MonsterAdded", monster);
        return monster.Id;
    }

    public void ApplyDamage(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        EnsurePositive(amount);
        target.TakeDamage(amount);
        Record(actor, now, "DamageApplied", target, ("amount", amount));
    }

    public void Heal(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        EnsurePositive(amount);
        target.Heal(amount);
        Record(actor, now, "Healed", target, ("amount", amount));
    }

    public void SetTempHp(Actor actor, Guid combatantId, int amount, DateTimeOffset now)
    {
        var target = Controllable(actor, combatantId);
        if (amount < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Temporary HP cannot be negative.");
        }

        target.SetTempHp(amount);
        Record(actor, now, "TempHpSet", target, ("amount", amount));
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    private Combatant Controllable(Actor actor, Guid combatantId)
    {
        EnsureNotEnded();
        var combatant = Find(combatantId);
        return actor switch
        {
            Actor.Dm dm when dm.UserId == OwnerId => combatant,
            Actor.Guest guest when combatant.ClaimedBy == guest.GuestId => combatant,
            Actor.Guest => throw new DomainException(
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

    private void EnsureNotEnded()
    {
        if (Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }
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
        Version++;
        LastActivityAt = now;

        var parameters = new Dictionary<string, string>();
        if (subject is not null)
        {
            parameters["combatantId"] = subject.Id.ToString();
            parameters["name"] = subject.Name;
        }

        foreach (var (key, value) in data)
        {
            parameters[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _uncommittedEvents.Add(new CombatEvent(Version, now, ActorName(actor), type, parameters));
    }

    private static string ActorName(Actor actor) => actor is Actor.Guest guest ? guest.Name : "DM";
}
