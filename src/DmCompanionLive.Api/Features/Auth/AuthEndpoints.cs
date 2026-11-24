using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Users;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Auth;

public static class AuthEndpoints
{
    public sealed record DevLoginRequest(string DisplayName);

    public sealed record MeResponse(Guid Id, string DisplayName);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapGet("/me", Me).RequireAuthorization(AuthSetup.DmPolicy);
        group.MapPost("/logout", (Delegate)Logout).RequireAuthorization(AuthSetup.DmPolicy);
        return app;
    }

    /// <summary>Development and test sign-in. Never mapped in production.</summary>
    public static IEndpointRouteBuilder MapDevLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/login", DevLogin).ExcludeFromDescription();
        return app;
    }

    private static async Task<Ok<MeResponse>> DevLogin(
        DevLoginRequest request, AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var externalId = $"dev:{request.DisplayName.Trim().ToLowerInvariant()}";
        var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is null)
        {
            user = DmUser.Create(externalId, request.DisplayName, clock.GetUtcNow());
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, DmPrincipal.Create(user));
        return TypedResults.Ok(new MeResponse(user.Id, user.DisplayName));
    }

    private static Ok<MeResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new MeResponse(user.GetDmUserId(), user.Identity?.Name ?? string.Empty));

    private static async Task<NoContent> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }
}
