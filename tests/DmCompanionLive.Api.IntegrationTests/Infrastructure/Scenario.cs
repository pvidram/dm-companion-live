using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

/// <summary>Builds the usual starting point of a combat test through the public API.</summary>
public static class Scenario
{
    public sealed record TableDto(Guid Id, string Name);

    public sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    public sealed record JoinResponse(Guid SessionId, Guid GuestId, string Name, string GuestToken);

    /// <summary>Creates a table with one 20 HP / AC 15 character per name and starts a session for it.</summary>
    public static async Task<SessionCreatedDto> CreateSessionAsync(this HttpClient dm, params string[] characters)
    {
        var table = (await (await dm.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        foreach (var name in characters)
        {
            (await dm.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name, maxHp = 20, ac = 15, dexModifier = 0 }))
                .EnsureSuccessStatusCode();
        }

        var response = await dm.PostAsync($"/api/tables/{table.Id}/sessions", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;
    }

    public static async Task<JoinResponse> JoinAsync(this WebApplicationFactory<Program> factory, string code, string name)
    {
        var response = await factory.CreateHttpsClient().PostAsJsonAsync($"/api/sessions/{code}/join", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JoinResponse>(TestJson.Options))!;
    }
}
