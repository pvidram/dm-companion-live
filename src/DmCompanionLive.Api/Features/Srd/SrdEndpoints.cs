using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Srd;

public static class SrdEndpoints
{
    public const int MaxQueryLength = 100;

    public sealed record SrdMonsterSummaryDto(
        string Slug,
        string Name,
        string Type,
        IReadOnlyList<string> Sizes,
        string Cr,
        int Ac,
        int HpAverage,
        bool IsFavorite)
    {
        public static SrdMonsterSummaryDto From(SrdMonster monster, SrdLanguage language, bool isFavorite) => new(
            monster.Slug, monster.Name.For(language), monster.Type, monster.Sizes, monster.Cr, monster.Ac,
            monster.HpAverage, isFavorite);
    }

    public sealed record SrdSearchDto(IReadOnlyList<SrdMonsterSummaryDto> Items, int Page, int Total);

    public sealed record SrdMonsterDto(
        string Slug,
        string Name,
        string Type,
        IReadOnlyList<string> Sizes,
        string Cr,
        int Ac,
        int HpAverage,
        string HpFormula,
        int InitiativeBonus,
        IReadOnlyList<int> Abilities,
        bool IsFavorite,
        StatBlock StatBlock);

    public static IEndpointRouteBuilder MapSrd(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/srd/monsters").RequireAuthorization(AuthSetup.DmPolicy).WithTags("SRD");
        group.MapGet("/", Search);
        group.MapGet("/{slug}", Get);
        return app;
    }

    public static DomainException MonsterNotFound(string slug) =>
        new(DomainErrorCode.MonsterNotFound, $"There is no SRD monster '{slug}'.");

    private static async Task<Ok<SrdSearchDto>> Search(
        string? q, string? lang, int? page, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog,
        AppDbContext db, CancellationToken ct)
    {
        if (q is { Length: > MaxQueryLength } || page < 1)
        {
            throw new DomainException(
                DomainErrorCode.InvalidValue, $"The query has at most {MaxQueryLength} characters and pages start at 1.");
        }

        var language = Languages.Resolve(lang, request);
        var result = catalog.Search(q, language, page ?? 1);
        var favorites = await FavoriteSlugsAsync(db, user.GetDmUserId(), result.Items.Select(m => m.Slug).ToList(), ct);
        return TypedResults.Ok(new SrdSearchDto(
            result.Items.Select(m => SrdMonsterSummaryDto.From(m, language, favorites.Contains(m.Slug))).ToList(),
            result.Page,
            result.Total));
    }

    private static async Task<Ok<SrdMonsterDto>> Get(
        string slug, string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db,
        CancellationToken ct)
    {
        var monster = catalog.Find(slug) ?? throw MonsterNotFound(slug);
        var language = Languages.Resolve(lang, request);
        var favorites = await FavoriteSlugsAsync(db, user.GetDmUserId(), [monster.Slug], ct);
        return TypedResults.Ok(new SrdMonsterDto(
            monster.Slug,
            monster.Name.For(language),
            monster.Type,
            monster.Sizes,
            monster.Cr,
            monster.Ac,
            monster.HpAverage,
            monster.HpFormula,
            monster.InitiativeBonus,
            monster.Abilities,
            favorites.Contains(monster.Slug),
            monster.StatBlock.For(language)));
    }

    private static async Task<HashSet<string>> FavoriteSlugsAsync(
        AppDbContext db, Guid ownerId, List<string> slugs, CancellationToken ct) =>
        (await db.FavoriteMonsters
            .Where(f => f.OwnerId == ownerId && slugs.Contains(f.SrdSlug))
            .Select(f => f.SrdSlug)
            .ToListAsync(ct))
        .ToHashSet(StringComparer.Ordinal);
}
