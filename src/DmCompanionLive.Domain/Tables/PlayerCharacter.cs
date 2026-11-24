namespace DmCompanionLive.Domain.Tables;

public sealed class PlayerCharacter
{
    private PlayerCharacter()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public int MaxHp { get; private set; }
    public int Ac { get; private set; }
    public int DexModifier { get; private set; }

    internal static PlayerCharacter Create(string name, int maxHp, int ac, int dexModifier)
    {
        var character = new PlayerCharacter { Id = Guid.NewGuid() };
        character.Update(name, maxHp, ac, dexModifier);
        return character;
    }

    internal void Update(string name, int maxHp, int ac, int dexModifier)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Name must have 1 to 80 characters.");
        }

        if (maxHp < 1 || ac < 0)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Max HP must be at least 1 and AC cannot be negative.");
        }

        Name = name.Trim();
        MaxHp = maxHp;
        Ac = ac;
        DexModifier = dexModifier;
    }
}
