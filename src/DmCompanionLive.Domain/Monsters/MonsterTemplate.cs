using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Monsters;

public sealed record MonsterTemplateData(
    string Name,
    int MaxHp,
    int Ac,
    int InitiativeBonus,
    string? HpFormula = null,
    string? Notes = null);

/// <summary>A DM's own monster. Its stats are copied into a combat when it is added, never linked.</summary>
public sealed class MonsterTemplate
{
    public const int MaxNotesLength = 2000;

    private MonsterTemplate()
    {
        Name = string.Empty;
        Notes = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public int MaxHp { get; private set; }
    public int Ac { get; private set; }
    public int InitiativeBonus { get; private set; }

    /// <summary>Normalised, e.g. "4d8+12"; null when HP are always the fixed <see cref="MaxHp"/>.</summary>
    public string? HpFormula { get; private set; }

    public string Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MonsterTemplate Create(Guid ownerId, MonsterTemplateData data, DateTimeOffset now)
    {
        var template = new MonsterTemplate { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = now.ToUniversalTime() };
        template.Update(data);
        return template;
    }

    public void Update(MonsterTemplateData data)
    {
        var name = Names.Validate(data.Name);
        if (data.MaxHp < 1)
        {
            throw Invalid("Max HP must be at least 1.");
        }

        if (data.Ac < 0)
        {
            throw Invalid("AC cannot be negative.");
        }

        if (Math.Abs(data.InitiativeBonus) > Combatant.MaxInitiativeBonus)
        {
            throw Invalid($"Initiative bonus must be between -{Combatant.MaxInitiativeBonus} and +{Combatant.MaxInitiativeBonus}.");
        }

        var formula = string.IsNullOrWhiteSpace(data.HpFormula) ? null : Combat.HpFormula.Parse(data.HpFormula).ToString();
        var notes = data.Notes?.Trim() ?? string.Empty;
        if (notes.Length > MaxNotesLength)
        {
            throw Invalid($"Notes have at most {MaxNotesLength} characters.");
        }

        Name = name;
        MaxHp = data.MaxHp;
        Ac = data.Ac;
        InitiativeBonus = data.InitiativeBonus;
        HpFormula = formula;
        Notes = notes;
    }

    /// <summary>Templates have no Dex score: the initiative bonus is also the tie-break.</summary>
    public MonsterSeed ToSeed(bool isHidden) => new(
        Name,
        MaxHp,
        Ac,
        DexModifier: InitiativeBonus,
        IsHidden: isHidden,
        Source: MonsterSource.Template(Id),
        InitiativeBonus: InitiativeBonus,
        HpFormula: HpFormula is null ? null : Combat.HpFormula.Parse(HpFormula));

    private static DomainException Invalid(string message) => new(DomainErrorCode.InvalidValue, message);
}
