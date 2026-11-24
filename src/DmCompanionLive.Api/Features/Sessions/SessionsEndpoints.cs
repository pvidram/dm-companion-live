using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Sessions;

public static class SessionsEndpoints
{
    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tables/{tableId:guid}/sessions", Create).RequireAuthorization().WithTags("Sessions");
        app.MapGet("/api/sessions/{code}", GetDmView).RequireAuthorization().WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Created<SessionCreatedDto>, NotFound>> Create(
        Guid tableId, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == tableId && t.OwnerId == ownerId, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        string code;
        do
        {
            code = JoinCode.New();
        }
        while (await db.Sessions.AnyAsync(s => s.JoinCode == code, ct));

        var session = table.StartSession(code, clock.GetUtcNow());
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/sessions/{code}", new SessionCreatedDto(session.Id, code));
    }

    private static async Task<Results<Ok<DmView>, NotFound>> GetDmView(
        string code, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var normalized = JoinCode.Normalize(code);
        var ownerId = user.GetDmUserId();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.JoinCode == normalized && s.OwnerId == ownerId, ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(CombatViews.ForDm(session));
    }
}
