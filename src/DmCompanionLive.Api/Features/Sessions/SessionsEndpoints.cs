using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DmCompanionLive.Api.Features.Sessions;

public static class SessionsEndpoints
{
    private const int MaxJoinCodeAttempts = 5;

    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tables/{tableId:guid}/sessions", Create).RequireAuthorization(AuthSetup.DmPolicy).WithTags("Sessions");
        app.MapGet("/api/sessions/{code}", GetDmView).RequireAuthorization(AuthSetup.DmPolicy).WithTags("Sessions");
        return app;
    }

    private static async Task<Results<Created<SessionCreatedDto>, NotFound>> Create(
        Guid tableId,
        ClaimsPrincipal user,
        AppDbContext db,
        TimeProvider clock,
        IJoinCodeGenerator codes,
        CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var table = await db.Tables.SingleOrDefaultAsync(t => t.Id == tableId && t.OwnerId == ownerId, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        for (var attempt = 1; ; attempt++)
        {
            var session = table.StartSession(codes.New(), clock.GetUtcNow());
            db.Sessions.Add(session);
            try
            {
                await db.SaveChangesAsync(ct);
                return TypedResults.Created(
                    $"/api/sessions/{session.JoinCode}", new SessionCreatedDto(session.Id, session.JoinCode));
            }
            catch (DbUpdateException e) when (
                attempt < MaxJoinCodeAttempts
                && e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The unique index on the join code is the source of truth: on a collision, try a fresh code.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task<Results<Ok<DmView>, NotFound>> GetDmView(
        string code, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        if (!JoinCode.TryNormalize(code, out var normalized))
        {
            return TypedResults.NotFound();
        }

        var ownerId = user.GetDmUserId();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.JoinCode == normalized && s.OwnerId == ownerId, ct);
        return session is null ? TypedResults.NotFound() : TypedResults.Ok(CombatViews.ForDm(session));
    }
}
