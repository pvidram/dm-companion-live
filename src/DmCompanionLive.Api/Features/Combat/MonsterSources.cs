using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Combat;

/// <summary>Turns an <see cref="AddMonsterInput"/> into the stats of the monster to add, on the server.</summary>
public sealed class MonsterSources(AppDbContext db, ISrdCatalog catalog)
{
    /// <exception cref="DomainException">
    /// <c>InvalidValue</c> for a missing or ambiguous source or bad stats; <c>NotAllowed</c> for players;
    /// <c>MonsterNotFound</c> for an unknown slug or a template that is missing or another DM's.
    /// </exception>
    public async Task<MonsterSeed> ResolveAsync(AddMonsterInput? input, Actor actor, SrdLanguage language, CancellationToken ct)
    {
        if (input?.Source is not { } source)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "A monster source is required.");
        }

        var given = (source.SrdSlug is null ? 0 : 1) + (source.TemplateId is null ? 0 : 1) + (source.Custom is null ? 0 : 1);
        if (given != 1)
        {
            throw new DomainException(DomainErrorCode.InvalidValue, "Give exactly one of srdSlug, templateId or custom.");
        }

        if (actor is not Actor.Dm dm)
        {
            throw new DomainException(DomainErrorCode.NotAllowed, "Only the DM of this session can do this.");
        }

        if (source.SrdSlug is { } slug)
        {
            var monster = catalog.Find(slug) ?? throw NotFound();
            return new MonsterSeed(
                monster.Name.For(language),
                monster.HpAverage,
                monster.Ac,
                monster.DexModifier,
                input.IsHidden,
                MonsterSource.Srd(monster.Slug),
                monster.InitiativeBonus,
                HpFormula.Parse(monster.HpFormula));
        }

        if (source.TemplateId is { } templateId)
        {
            var template = await db.MonsterTemplates.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == templateId && t.OwnerId == dm.UserId, ct)
                ?? throw NotFound();
            return template.ToSeed(input.IsHidden);
        }

        var custom = source.Custom!;
        return new MonsterSeed(
            custom.Name ?? string.Empty,
            custom.MaxHp,
            custom.Ac,
            custom.InitiativeBonus,
            input.IsHidden,
            MonsterSource.None,
            custom.InitiativeBonus,
            string.IsNullOrWhiteSpace(custom.HpFormula) ? null : HpFormula.Parse(custom.HpFormula));
    }

    private static DomainException NotFound() =>
        new(DomainErrorCode.MonsterNotFound, "There is no such monster or template.");
}
