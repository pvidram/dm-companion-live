namespace DmCompanionLive.Domain.Monsters;

/// <summary>An SRD monster a DM marked. The slug is checked against the catalogue by the API.</summary>
public sealed class FavoriteMonster
{
    public const int MaxSlugLength = 120;

    private FavoriteMonster()
    {
        SrdSlug = string.Empty;
    }

    public Guid OwnerId { get; private set; }
    public string SrdSlug { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static FavoriteMonster Create(Guid ownerId, string srdSlug, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(srdSlug) || srdSlug.Length > MaxSlugLength)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, $"A favourite needs an SRD slug of 1 to {MaxSlugLength} characters.");
        }

        return new FavoriteMonster { OwnerId = ownerId, SrdSlug = srdSlug, CreatedAt = now.ToUniversalTime() };
    }
}
