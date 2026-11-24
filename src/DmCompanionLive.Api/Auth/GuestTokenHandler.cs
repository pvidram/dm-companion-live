using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DmCompanionLive.Api.Auth;

/// <summary>Authenticates a player from their guest token.</summary>
public sealed class GuestTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    GuestTokens tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "GuestToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = TokenFrom(Request);
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (tokens.Read(token) is not { } ticket)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired guest token."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(AppClaims.Role, AppClaims.GuestRole),
                new Claim(AppClaims.GuestId, ticket.GuestId.ToString()),
                new Claim(AppClaims.SessionId, ticket.SessionId.ToString()),
                new Claim(ClaimTypes.Name, ticket.Name),
            ],
            Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    private static string? TokenFrom(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        // Browsers cannot set headers on WebSocket requests, so SignalR sends the token in the query string.
        return request.Path.StartsWithSegments("/hubs") ? request.Query["access_token"].FirstOrDefault() : null;
    }
}
