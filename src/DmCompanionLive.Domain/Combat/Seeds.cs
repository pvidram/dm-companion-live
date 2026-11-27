namespace DmCompanionLive.Domain.Combat;

public sealed record PcSeed(string Name, int MaxHp, int Ac, int DexModifier = 0);

/// <summary>
/// The stats of a monster to add. <see cref="MaxHp"/> is the average used unless HP are rolled with
/// <see cref="HpFormula"/>; <see cref="InitiativeBonus"/> defaults to <see cref="DexModifier"/>, which
/// stays the initiative tie-break.
/// </summary>
public sealed record MonsterSeed(
    string Name,
    int MaxHp,
    int Ac,
    int DexModifier = 0,
    bool IsHidden = false,
    MonsterSource? Source = null,
    int? InitiativeBonus = null,
    HpFormula? HpFormula = null);
