namespace DmCompanionLive.Domain.Combat;

public sealed record PcSeed(string Name, int MaxHp, int Ac, int DexModifier = 0);

public sealed record MonsterSeed(
    string Name,
    int MaxHp,
    int Ac,
    int DexModifier = 0,
    bool IsHidden = false,
    string? SourceRef = null);
