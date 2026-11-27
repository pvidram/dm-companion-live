using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Features.Srd;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Monsters;

public static class MonsterTemplatesEndpoints
{
    public sealed record TemplateRequest(
        string Name,
        int MaxHp,
        int Ac,
        int InitiativeBonus,
        string? HpFormula = null,
        string? Notes = null)
    {
        public MonsterTemplateData ToData() => new(Name, MaxHp, Ac, InitiativeBonus, HpFormula, Notes);
    }

    public sealed record TemplateDto(
        Guid Id,
        string Name,
        int MaxHp,
        int Ac,
        int InitiativeBonus,
        string? HpFormula,
        string Notes)
    {
        public static TemplateDto From(MonsterTemplate t) =>
            new(t.Id, t.Name, t.MaxHp, t.Ac, t.InitiativeBonus, t.HpFormula, t.Notes);
    }

    public static IEndpointRouteBuilder MapMonsterTemplates(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monsters/templates").RequireAuthorization(AuthSetup.DmPolicy).WithTags("Monster templates");
        group.MapGet("/", List);
        group.MapGet("/{id:guid}", Get);
        group.MapPost("/", Create);
        group.MapPost("/from-srd/{slug}", CreateFromSrd);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        return app;
    }

    private static async Task<Ok<List<TemplateDto>>> List(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var templates = await db.MonsterTemplates.Where(t => t.OwnerId == ownerId).OrderBy(t => t.Name).ToListAsync(ct);
        return TypedResults.Ok(templates.Select(TemplateDto.From).ToList());
    }

    private static async Task<Results<Ok<TemplateDto>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        return template is null ? TypedResults.NotFound() : TypedResults.Ok(TemplateDto.From(template));
    }

    private static Task<Created<TemplateDto>> Create(
        TemplateRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        SaveNewAsync(db, MonsterTemplate.Create(user.GetDmUserId(), request.ToData(), clock.GetUtcNow()), ct);

    /// <summary>A new template with the SRD monster's name (in <c>lang</c>) and numbers, ready to edit.</summary>
    private static Task<Created<TemplateDto>> CreateFromSrd(
        string slug, string? lang, HttpRequest request, ClaimsPrincipal user, ISrdCatalog catalog, AppDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var monster = catalog.Find(slug) ?? throw SrdEndpoints.MonsterNotFound(slug);
        var language = Languages.Resolve(lang, request);
        var data = new MonsterTemplateData(
            monster.Name.For(language), monster.HpAverage, monster.Ac, monster.InitiativeBonus, monster.HpFormula);
        return SaveNewAsync(db, MonsterTemplate.Create(user.GetDmUserId(), data, clock.GetUtcNow()), ct);
    }

    private static async Task<Results<NoContent, NotFound>> Update(
        Guid id, TemplateRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        template.Update(request.ToData());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var template = await FindOwnedAsync(db, id, user, ct);
        if (template is null)
        {
            return TypedResults.NotFound();
        }

        db.MonsterTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Created<TemplateDto>> SaveNewAsync(AppDbContext db, MonsterTemplate template, CancellationToken ct)
    {
        db.MonsterTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/monsters/templates/{template.Id}", TemplateDto.From(template));
    }

    /// <summary>Templates of other DMs are reported as not found, never as forbidden.</summary>
    private static Task<MonsterTemplate?> FindOwnedAsync(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        return db.MonsterTemplates.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);
    }
}
