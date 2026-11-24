using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Features.Tables;

public static class TablesEndpoints
{
    public sealed record TableRequest(string Name);

    public sealed record CharacterRequest(string Name, int MaxHp, int Ac, int DexModifier);

    public sealed record CharacterDto(Guid Id, string Name, int MaxHp, int Ac, int DexModifier);

    public sealed record TableDto(Guid Id, string Name, IReadOnlyList<CharacterDto> Characters);

    public sealed record TableSummaryDto(Guid Id, string Name, int CharacterCount);

    public static IEndpointRouteBuilder MapTables(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tables").RequireAuthorization().WithTags("Tables");
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", Get);
        group.MapPut("/{id:guid}", Rename);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/characters", AddCharacter);
        group.MapPut("/{id:guid}/characters/{characterId:guid}", UpdateCharacter);
        group.MapDelete("/{id:guid}/characters/{characterId:guid}", RemoveCharacter);
        return app;
    }

    private static async Task<Ok<List<TableSummaryDto>>> List(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        var tables = await db.Tables
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .Select(t => new TableSummaryDto(t.Id, t.Name, t.Characters.Count))
            .ToListAsync(ct);
        return TypedResults.Ok(tables);
    }

    private static async Task<Created<TableDto>> Create(
        TableRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var table = Table.Create(user.GetDmUserId(), request.Name, clock.GetUtcNow());
        db.Tables.Add(table);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tables/{table.Id}", ToDto(table));
    }

    private static async Task<Results<Ok<TableDto>, NotFound>> Get(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        return table is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(table));
    }

    private static async Task<Results<NoContent, NotFound>> Rename(
        Guid id, TableRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Delete(
        Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        db.Tables.Remove(table);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<CharacterDto>, NotFound>> AddCharacter(
        Guid id, CharacterRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        var character = table.AddCharacter(request.Name, request.MaxHp, request.Ac, request.DexModifier);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/tables/{id}/characters/{character.Id}", ToDto(character));
    }

    private static async Task<Results<NoContent, NotFound>> UpdateCharacter(
        Guid id, Guid characterId, CharacterRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.UpdateCharacter(characterId, request.Name, request.MaxHp, request.Ac, request.DexModifier);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RemoveCharacter(
        Guid id, Guid characterId, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var table = await FindOwnedAsync(db, id, user, ct);
        if (table is null)
        {
            return TypedResults.NotFound();
        }

        table.RemoveCharacter(characterId);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Tables of other DMs are reported as not found, never as forbidden.</summary>
    private static Task<Table?> FindOwnedAsync(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetDmUserId();
        return db.Tables.SingleOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);
    }

    private static TableDto ToDto(Table table) =>
        new(table.Id, table.Name, table.Characters.Select(ToDto).ToList());

    private static CharacterDto ToDto(PlayerCharacter c) => new(c.Id, c.Name, c.MaxHp, c.Ac, c.DexModifier);
}
