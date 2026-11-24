using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SessionsTests(PostgresFixture postgres)
{
    private sealed record TableDto(Guid Id, string Name);

    private sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    [Fact]
    public async Task A_dm_starts_a_session_with_the_party_and_reads_it_by_code()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);

        var response = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;
        created.JoinCode.Should().HaveLength(JoinCode.Length);
        response.Headers.Location!.ToString().Should().Be($"/api/sessions/{created.JoinCode}");

        var view = await client.GetFromJsonAsync<DmView>(
            $"/api/sessions/{created.JoinCode.ToLowerInvariant()}", TestJson.Options);
        view!.SessionId.Should().Be(created.SessionId);
        view.Status.Should().Be(SessionStatus.Lobby);
        view.Combatants.Should().ContainSingle().Which.Name.Should().Be("Lyra");
    }

    [Fact]
    public async Task Other_dms_cannot_start_or_read_my_sessions()
    {
        var owner = await postgres.Factory.CreateDmClientAsync();
        var intruder = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(owner);
        var created = (await (await owner.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;

        (await intruder.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/sessions/{created.JoinCode}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<TableDto> CreateTableWithLyraAsync(HttpClient client)
    {
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Lyra", maxHp = 20, ac = 15, dexModifier = 2 });
        return table;
    }
}
