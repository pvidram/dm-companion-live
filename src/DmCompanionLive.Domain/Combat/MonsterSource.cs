namespace DmCompanionLive.Domain.Combat;

public enum MonsterSourceKind
{
    None,
    Srd,
    Template,
}

/// <summary>Where a monster's stats came from. Informational: the stats are copied when it is added.</summary>
public sealed record MonsterSource(MonsterSourceKind Kind, string? Id)
{
    public static readonly MonsterSource None = new(MonsterSourceKind.None, null);

    public static MonsterSource Srd(string slug) => new(MonsterSourceKind.Srd, slug);

    public static MonsterSource Template(Guid id) => new(MonsterSourceKind.Template, id.ToString());
}
