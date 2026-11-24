using System.Security.Claims;
using DmCompanionLive.Domain.Users;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DmCompanionLive.Api.Auth;

public static class DmPrincipal
{
    public static ClaimsPrincipal Create(DmUser user) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(AppClaims.Role, AppClaims.DmRole),
        ],
        CookieAuthenticationDefaults.AuthenticationScheme));

    public static Guid GetDmUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The caller is not an authenticated DM."));
}
