namespace DmCompanionLive.Domain.Combat;

/// <summary>The display name a player types when joining a combat.</summary>
public static class GuestName
{
    public const int MaxLength = 40;

    public static string Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"Name must have 1 to {MaxLength} characters.");
        }

        return trimmed;
    }
}
