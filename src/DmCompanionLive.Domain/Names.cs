namespace DmCompanionLive.Domain;

/// <summary>Display names of tables, characters, combatants and monster templates.</summary>
public static class Names
{
    public const int MaxLength = 80;

    /// <summary>The trimmed name, or <see cref="DomainErrorCode.InvalidValue"/> when it is blank or too long.</summary>
    public static string Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxLength} characters.");
        }

        return name.Trim();
    }
}
