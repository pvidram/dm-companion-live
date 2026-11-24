using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DmCompanionLive.Api.Features.Auth;

public static class AuthEndpoints
{
    public sealed record DevLoginRequest(string DisplayName);

    public sealed record MeResponse(Guid Id, string DisplayName);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app, bool googleEnabled)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapGet("/me", Me).RequireAuthorization(AuthSetup.DmPolicy);
        group.MapPost("/logout", (Delegate)Logout).RequireAuthorization(AuthSetup.DmPolicy);
        if (googleEnabled)
        {
            group.MapGet("/google/login", GoogleLogin);
        }

        return app;
    }

    /// <summary>Development and test sign-in. Never mapped in production.</summary>
    public static IEndpointRouteBuilder MapDevLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/login", DevLogin).ExcludeFromDescription();
        return app;
    }

    /// <summary>Keeps <paramref name="returnUrl"/> only if it stays on this site (no open redirect).</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.Any(c => c < 0x20 || c == 0x7F || c == '\\')
            ? url
            : "/";

    private static ChallengeHttpResult GoogleLogin(string? returnUrl) =>
        TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
            [GoogleDefaults.AuthenticationScheme]);

    private static async Task<Ok<MeResponse>> DevLogin(
        DevLoginRequest request, AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var user = await DmUsers.FindOrCreateAsync(
            db, $"dev:{request.DisplayName.Trim().ToLowerInvariant()}", request.DisplayName, clock.GetUtcNow(), ct);
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
