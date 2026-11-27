using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SrdTests(PostgresFixture postgres)
{
    private sealed record SummaryDto(string Slug, string Name, string Type, List<string> Sizes, string Cr, int Ac, int HpAverage, bool IsFavorite);

    private sealed record SearchDto(List<SummaryDto> Items, int Page, int Total);

    private sealed record EntryDto(string? Name, string Text);

    private sealed record SectionDto(string Kind, List<EntryDto> Entries);

    private sealed record StatBlockDto(string Subtitle, List<SectionDto> Sections);

    private sealed record MonsterDto(string Slug, string Name, string HpFormula, int InitiativeBonus, bool IsFavorite, StatBlockDto StatBlock);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/srd/monsters")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/monsters/favorites")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_ignores_accents_and_returns_names_in_the_requested_language()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var spanish = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=dragon%20rojo&lang=es"));
        var english = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=oso%20lechuza&lang=en"));

        spanish.Items[0].Should().BeEquivalentTo(new { Slug = "adult-red-dragon", Name = "Dragón rojo adulto" });
        english.Items.Should().ContainSingle().Which.Name.Should().Be("Owlbear");
        english.Total.Should().Be(1);
    }

    [Fact]
    public async Task Without_lang_the_accept_language_header_decides_and_english_is_the_default()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/srd/monsters?q=owlbear");
        request.Headers.TryAddWithoutValidation("Accept-Language", "fr-FR,es;q=0.9,en;q=0.8");

        var spanish = await ReadAsync<SearchDto>(await client.SendAsync(request));
        var english = await ReadAsync<SearchDto>(await client.GetAsync("/api/srd/monsters?q=owlbear"));

        spanish.Items[0].Name.Should().Be("Oso lechuza");
        english.Items[0].Name.Should().Be("Owlbear");
    }

    [Theory]
    [InlineData("/api/srd/monsters?lang=fr")]
    [InlineData("/api/srd/monsters?page=0")]
    [InlineData("/api/srd/monsters?page=10001")]
    public async Task An_unsupported_language_or_page_is_an_invalid_value(string url)
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("InvalidValue");
    }

    [Fact]
    public async Task A_monster_comes_with_its_stat_block_in_the_requested_language()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var english = await ReadAsync<MonsterDto>(await client.GetAsync("/api/srd/monsters/goblin-warrior?lang=en"));
        var spanish = await ReadAsync<MonsterDto>(await client.GetAsync("/api/srd/monsters/goblin-warrior?lang=es"));
        var missing = await client.GetAsync("/api/srd/monsters/kraken-of-nowhere");

        (english.Name, english.HpFormula, english.InitiativeBonus).Should().Be(("Goblin Warrior", "3d6", 2));
        english.StatBlock.Sections.Should().Contain(s => s.Kind == "Actions" && s.Entries.Any(e => e.Name == "Scimitar"));
        spanish.Name.Should().Be("Guerrero goblin");
        spanish.StatBlock.Sections.Should().Contain(s => s.Kind == "Actions");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).Should().Be("MonsterNotFound");
    }

    [Fact]
    public async Task Favourites_are_idempotent_per_dm_and_marked_in_search_results()
    {
        var ana = await postgres.Factory.CreateDmClientAsync();
        var bea = await postgres.Factory.CreateDmClientAsync();

        (await ana.PutAsync("/api/monsters/favorites/owlbear", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ana.PutAsync("/api/monsters/favorites/owlbear", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var favorites = await ReadAsync<List<SummaryDto>>(await ana.GetAsync("/api/monsters/favorites?lang=es"));
        favorites.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Slug = "owlbear", Name = "Oso lechuza", IsFavorite = true });
        (await ReadAsync<SearchDto>(await ana.GetAsync("/api/srd/monsters?q=owlbear"))).Items[0].IsFavorite.Should().BeTrue();
        (await ReadAsync<MonsterDto>(await ana.GetAsync("/api/srd/monsters/owlbear"))).IsFavorite.Should().BeTrue();
        (await ReadAsync<SearchDto>(await bea.GetAsync("/api/srd/monsters?q=owlbear"))).Items[0].IsFavorite.Should().BeFalse();
        (await ReadAsync<List<SummaryDto>>(await bea.GetAsync("/api/monsters/favorites"))).Should().BeEmpty();

        (await ana.DeleteAsync("/api/monsters/favorites/owlbear")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ana.DeleteAsync("/api/monsters/favorites/owlbear")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadAsync<List<SummaryDto>>(await ana.GetAsync("/api/monsters/favorites"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_srd_monsters_can_be_favourites()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var response = await client.PutAsync("/api/monsters/favorites/kraken-of-nowhere", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be("MonsterNotFound");
    }
}
