using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Who is on the other end of a hub connection, resolved from the authenticated identities.</summary>
public sealed record CombatIdentity(Actor Actor, Guid? TokenSessionId)
{
    /// <summary>A guest token wins over a DM cookie: on the player page the token is sent explicitly.</summary>
    public static CombatIdentity? From(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var guest = user.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType == GuestTokenHandler.Scheme);
        if (guest is not null)
        {
            return new CombatIdentity(
                new Actor.Guest(Guid.Parse(guest.FindFirst(AppClaims.GuestId)!.Value), guest.Name ?? string.Empty),
                Guid.Parse(guest.FindFirst(AppClaims.SessionId)!.Value));
        }

        var dm = user.Identities.FirstOrDefault(i => i.IsAuthenticated && i.HasClaim(AppClaims.Role, AppClaims.DmRole));
        return dm is null
            ? null
            : new CombatIdentity(new Actor.Dm(Guid.Parse(dm.FindFirst(ClaimTypes.NameIdentifier)!.Value)), null);
    }

    public bool CanJoin(CombatSession session) => Actor switch
    {
        Actor.Dm dm => dm.UserId == session.OwnerId,
        Actor.Guest => TokenSessionId == session.Id,
        _ => false,
    };
}
