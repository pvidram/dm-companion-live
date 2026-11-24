using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Domain.Tables;

/// <summary>A DM's campaign: its name and the party's player characters.</summary>
public sealed class Table
{
    public const int MaxNameLength = 80;

    private readonly List<PlayerCharacter> _characters = [];

    private Table()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<PlayerCharacter> Characters => _characters;

    public static Table Create(Guid ownerId, string name, DateTimeOffset now)
    {
        var table = new Table { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = now };
        table.Rename(name);
        return table;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxNameLength} characters.");
        }

        Name = name.Trim();
    }

    public PlayerCharacter AddCharacter(string name, int maxHp, int ac, int dexModifier)
    {
        var character = PlayerCharacter.Create(name, maxHp, ac, dexModifier);
        _characters.Add(character);
        return character;
    }

    public void UpdateCharacter(Guid id, string name, int maxHp, int ac, int dexModifier) =>
        FindCharacter(id).Update(name, maxHp, ac, dexModifier);

    public void RemoveCharacter(Guid id) => _characters.Remove(FindCharacter(id));

    public CombatSession StartSession(string joinCode, DateTimeOffset now) =>
        CombatSession.Create(
            Id,
            OwnerId,
            joinCode,
            _characters.Select(c => new PcSeed(c.Name, c.MaxHp, c.Ac, c.DexModifier)),
            now);

    private PlayerCharacter FindCharacter(Guid id) =>
        _characters.SingleOrDefault(c => c.Id == id)
        ?? throw new DomainException(DomainErrorCode.CharacterNotFound, $"Character {id} not found.");
}
