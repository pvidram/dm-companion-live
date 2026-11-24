using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class TablesTests(PostgresFixture postgres)
{
    private sealed record CharacterDto(Guid Id, string Name, int MaxHp, int Ac, int DexModifier);

    private sealed record TableDto(Guid Id, string Name, List<CharacterDto> Characters);

    private sealed record TableSummaryDto(Guid Id, string Name, int CharacterCount);

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/tables")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dm_creates_and_edits_a_table_with_characters()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var created = await client.PostAsJsonAsync("/api/tables", new { name = "Costa de Sal" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var table = (await created.Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        created.Headers.Location!.ToString().Should().Be($"/api/tables/{table.Id}");

        var addLyra = await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Lyra", maxHp = 20, ac = 15, dexModifier = 2 });
        addLyra.StatusCode.Should().Be(HttpStatusCode.Created);
        var lyra = (await addLyra.Content.ReadFromJsonAsync<CharacterDto>(TestJson.Options))!;
        await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Torvald", maxHp = 30, ac = 18, dexModifier = 0 });

        (await client.PutAsJsonAsync(
            $"/api/tables/{table.Id}/characters/{lyra.Id}", new { name = "Lyra", maxHp = 22, ac = 15, dexModifier = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/api/tables/{table.Id}", new { name = "Costa de Sal II" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loaded = await client.GetFromJsonAsync<TableDto>($"/api/tables/{table.Id}", TestJson.Options);
        loaded!.Name.Should().Be("Costa de Sal II");
        loaded.Characters.Should().HaveCount(2);
        loaded.Characters.Single(c => c.Name == "Lyra").MaxHp.Should().Be(22);

        var list = await client.GetFromJsonAsync<List<TableSummaryDto>>("/api/tables", TestJson.Options);
        list.Should().ContainSingle().Which.CharacterCount.Should().Be(2);
    }

    [Fact]
    public async Task Removing_characters_and_tables()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Temp" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        var character = (await (await client.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name = "Nim", maxHp = 10, ac = 12, dexModifier = 1 }))
            .Content.ReadFromJsonAsync<CharacterDto>(TestJson.Options))!;

        (await client.DeleteAsync($"/api/tables/{table.Id}/characters/{character.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<TableDto>($"/api/tables/{table.Id}", TestJson.Options))!
            .Characters.Should().BeEmpty();
        (await client.DeleteAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_dm_cannot_see_or_change_my_table()
    {
        var owner = await postgres.Factory.CreateDmClientAsync();
        var intruder = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await owner.PostAsJsonAsync("/api/tables", new { name = "Mine" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;

        (await intruder.GetAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PutAsJsonAsync($"/api/tables/{table.Id}", new { name = "Stolen" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/tables/{table.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.GetFromJsonAsync<List<TableSummaryDto>>("/api/tables", TestJson.Options))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_input_is_a_400_problem()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Valid" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;

        (await client.PostAsJsonAsync("/api/tables", new { name = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(
            $"/api/tables/{table.Id}/characters", new { name = "Nim", maxHp = 0, ac = 12, dexModifier = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_dm_cannot_touch_characters_of_my_table()
    {
        var owner = await postgres.Factory.CreateDmClientAsync();
        var intruder = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await owner.PostAsJsonAsync("/api/tables", new { name = "Mine" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        var character = (await (await owner.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name = "Nim", maxHp = 10, ac = 12, dexModifier = 1 }))
            .Content.ReadFromJsonAsync<CharacterDto>(TestJson.Options))!;

        (await intruder.PostAsJsonAsync(
                $"/api/tables/{table.Id}/characters", new { name = "Evil", maxHp = 1, ac = 1, dexModifier = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.PutAsJsonAsync(
                $"/api/tables/{table.Id}/characters/{character.Id}",
                new { name = "Stolen", maxHp = 99, ac = 99, dexModifier = 9 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await intruder.DeleteAsync($"/api/tables/{table.Id}/characters/{character.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var loaded = await owner.GetFromJsonAsync<TableDto>($"/api/tables/{table.Id}", TestJson.Options);
        loaded!.Characters.Should().ContainSingle().Which.Should().Be(character);
    }

    [Fact]
    public async Task Unknown_character_ids_are_404()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = (await (await client.PostAsJsonAsync("/api/tables", new { name = "Mine" }))
            .Content.ReadFromJsonAsync<TableDto>(TestJson.Options))!;
        var unknown = Guid.NewGuid();

        var put = await client.PutAsJsonAsync(
            $"/api/tables/{table.Id}/characters/{unknown}", new { name = "Ghost", maxHp = 5, ac = 10, dexModifier = 0 });
        var delete = await client.DeleteAsync($"/api/tables/{table.Id}/characters/{unknown}");

        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await put.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"CharacterNotFound\"");
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"CharacterNotFound\"");
    }
}
