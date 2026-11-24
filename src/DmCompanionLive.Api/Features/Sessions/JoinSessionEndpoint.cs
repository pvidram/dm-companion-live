using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Sessions;

/// <summary>A player joins a combat with its code and a name, and gets a guest token.</summary>
public static class JoinSessionEndpoint
{
    public const string RateLimitPolicy = "join";

    public sealed record JoinRequest(string Name);

    public sealed record JoinResponse(Guid SessionId, Guid GuestId, string Name, string GuestToken);

    public static IEndpointRouteBuilder MapJoinSession(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions/{code}/join", Join)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Ok<JoinResponse>, NotFound>> Join(
        string code, JoinRequest request, AppDbContext db, GuestTokens tokens, TimeProvider clock, CancellationToken ct)
    {
        var name = GuestName.Validate(request.Name);
        if (!JoinCode.TryNormalize(code, out var normalized))
        {
            return TypedResults.NotFound();
        }

        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.JoinCode == normalized, ct);
        if (session is null)
        {
            return TypedResults.NotFound();
        }

        if (session.Status == SessionStatus.Ended)
        {
            throw new DomainException(DomainErrorCode.SessionEnded, "The combat has ended.");
        }

        if (session.IsExpiredAt(clock.GetUtcNow()))
        {
            throw new DomainException(DomainErrorCode.SessionExpired, "The combat expired after 12 hours without activity.");
        }

        var ticket = new GuestTicket(session.Id, Guid.NewGuid(), name);
        return TypedResults.Ok(new JoinResponse(ticket.SessionId, ticket.GuestId, ticket.Name, tokens.Issue(ticket)));
    }
}
