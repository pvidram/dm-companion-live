namespace DmCompanionLive.Domain.Combat;

public sealed class Combatant
{
    public const int MaxNameLength = Names.MaxLength;
    public const int MaxInitiativeBonus = 30;

    private Combatant()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public CombatantKind Kind { get; private set; }
    public string Name { get; private set; }
    public int? Initiative { get; private set; }
    public int DexModifier { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int TempHp { get; private set; }
    public int Ac { get; private set; }
    public Condition Conditions { get; private set; }
    public bool IsHidden { get; private set; }
    public int InitiativeBonus { get; private set; }
    public MonsterSourceKind SourceKind { get; private set; }
    public string? SourceId { get; private set; }
    public Guid? ClaimedBy { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>Round in which this combatant last began a turn; null before its first turn.</summary>
    public int? LastTurnRound { get; private set; }

    /// <summary>Only monsters are defeated at 0 HP; PCs fall unconscious and keep their turn.</summary>
    public bool IsDefeated => Kind == CombatantKind.Monster && CurrentHp == 0;

    internal static Combatant CreatePc(PcSeed seed, int sortOrder)
    {
        Validate(seed.Name, seed.MaxHp, seed.Ac);
        return new Combatant
        {
            Id = Guid.NewGuid(),
            Kind = CombatantKind.Pc,
            Name = seed.Name.Trim(),
            MaxHp = seed.MaxHp,
            CurrentHp = seed.MaxHp,
            Ac = seed.Ac,
            DexModifier = seed.DexModifier,
            InitiativeBonus = seed.DexModifier,
            SortOrder = sortOrder,
        };
    }

    internal static Combatant CreateMonster(MonsterSeed seed, string name, int maxHp, int sortOrder)
    {
        Validate(name, maxHp, seed.Ac);
        var initiativeBonus = seed.InitiativeBonus ?? seed.DexModifier;
        if (Math.Abs(initiativeBonus) > MaxInitiativeBonus)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"Initiative bonus must be between -{MaxInitiativeBonus} and +{MaxInitiativeBonus}.");
        }

        var source = seed.Source ?? MonsterSource.None;
        return new Combatant
        {
            Id = Guid.NewGuid(),
            Kind = CombatantKind.Monster,
            Name = name.Trim(),
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Ac = seed.Ac,
            DexModifier = seed.DexModifier,
            InitiativeBonus = initiativeBonus,
            IsHidden = seed.IsHidden,
            SourceKind = source.Kind,
            SourceId = source.Id,
            SortOrder = sortOrder,
        };
    }

    internal void TakeDamage(int amount)
    {
        var absorbed = Math.Min(TempHp, amount);
        TempHp -= absorbed;
        CurrentHp = Math.Max(0, CurrentHp - (amount - absorbed));
        if (Kind == CombatantKind.Pc && CurrentHp == 0)
        {
            Conditions |= Condition.Unconscious;
        }
    }

    internal void Heal(int amount)
    {
        if (Kind == CombatantKind.Pc && CurrentHp == 0)
        {
            Conditions &= ~Condition.Unconscious;
        }

        CurrentHp = (int)Math.Min(MaxHp, (long)CurrentHp + amount);
    }

    internal void SetTempHp(int amount) => TempHp = amount;

    internal void BeginTurn(int round) => LastTurnRound = round;

    internal void SetInitiative(int value) => Initiative = value;

    internal void SetConditions(Condition conditions) => Conditions = conditions;

    internal void Reveal() => IsHidden = false;

    internal void ClaimBy(Guid guestId) => ClaimedBy = guestId;

    private static void Validate(string name, int maxHp, int ac)
    {
        Names.Validate(name);

        if (maxHp < 1)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Max HP must be at least 1.");
        }

        if (ac < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "AC cannot be negative.");
        }
    }
}
