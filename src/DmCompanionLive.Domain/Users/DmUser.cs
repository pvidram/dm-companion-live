namespace DmCompanionLive.Domain.Users;

/// <summary>A signed-in DM. Only an external id (e.g. "google:&lt;sub&gt;") and a display name are stored.</summary>
public sealed class DmUser
{
    public const int MaxDisplayNameLength = 80;

    private DmUser()
    {
        ExternalId = string.Empty;
        DisplayName = string.Empty;
    }

    public Guid Id { get; private set; }
    public string ExternalId { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static DmUser Create(string externalId, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > MaxDisplayNameLength)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"Display name must have 1 to {MaxDisplayNameLength} characters.");
        }

        return new DmUser
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            DisplayName = displayName.Trim(),
            CreatedAt = now,
        };
    }
}
