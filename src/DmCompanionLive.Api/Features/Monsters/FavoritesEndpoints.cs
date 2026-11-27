using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Features.Srd;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DmCompanionLive.Api.Features.Srd.SrdEndpoints;

namespace DmCompanionLive.Api.Features.Monsters;

public static class FavoritesEndpoints
{
    public static IEndpointRouteBuilder MapFavorites(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monsters/favorites").RequireAuthorization(AuthSetup.DmPolicy).WithTags("Favorites");
        group.MapGet("/", List);
        group.MapPut("/{slug}", Add);
        group.MapDelete("/{slug}", Remove);
        return app;
    }

    private static async Task<Ok<List<SrdMonsterSummaryDto>>> List(
        string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db, CancellationToken ct)
    {
        var language = Languages.Resolve(lang, request);
        var ownerId = user.GetDmUserId();
        var slugs = await db.FavoriteMonsters.Where(f => f.OwnerId == ownerId).Select(f => f.SrdSlug).ToListAsync(ct);

        // A favourite whose monster left the catalogue (after a new SRD import) is skipped, not an error.
        var items = slugs
            .Select(catalog.Find)
            .OfType<SrdMonster>()
            .Select(m => SrdMonsterSummaryDto.From(m, language, isFavorite: true))
            .OrderBy(m => SearchText.Normalize(m.Name), StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(items);
    }

    private static async Task<NoContent> Add(
        string slug, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (catalog.Find(slug) is null)
        {
            throw MonsterNotFound(slug);
        }

        var ownerId = user.GetDmUserId();
        if (await db.FavoriteMonsters.AnyAsync(f => f.OwnerId == ownerId && f.SrdSlug == slug, ct))
        {
            return TypedResults.NoContent();
        }

        db.FavoriteMonsters.Add(FavoriteMonster.Create(ownerId, slug, clock.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same favourite was added concurrently: the outcome is the one asked for.
        }

        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Remove(string slug, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        await db.FavoriteMonsters.Where(f => f.OwnerId == ownerId && f.SrdSlug == slug).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}
