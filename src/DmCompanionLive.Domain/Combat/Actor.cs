namespace DmCompanionLive.Domain.Combat;

/// <summary>Who issues a command: the DM who owns the session or a guest player.</summary>
public abstract record Actor
{
    private Actor()
    {
    }

    public sealed record Dm(Guid UserId) : Actor;

    public sealed record Guest(Guid GuestId, string Name) : Actor;
}
