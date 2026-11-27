using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class MonsterTemplatesTests(PostgresFixture postgres)
{
    private sealed record TemplateDto(Guid Id, string Name, int MaxHp, int Ac, int InitiativeBonus, string? HpFormula, string Notes);

    private static readonly object Veteran = new
    {
        name = " Bandido veterano ",
        maxHp = 30,
        ac = 14,
        initiativeBonus = 3,
        hpFormula = "4d8 + 12",
        notes = "Lleva una ballesta.",
    };

    private static async Task<TemplateDto> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = (await response.Content.ReadFromJsonAsync<TemplateDto>(TestJson.Options))!;
        response.Headers.Location!.ToString().Should().Be($"/api/monsters/templates/{template.Id}");
        return template;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var client = postgres.Factory.CreateHttpsClient();

        (await client.GetAsync("/api/monsters/templates")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_dm_creates_lists_edits_and_deletes_templates()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var created = await CreatedAsync(await client.PostAsJsonAsync("/api/monsters/templates", Veteran));
        created.Should().BeEquivalentTo(new
        {
            Name = "Bandido veterano", MaxHp = 30, Ac = 14, InitiativeBonus = 3, HpFormula = "4d8+12", Notes = "Lleva una ballesta.",
        });

        var update = await client.PutAsJsonAsync(
            $"/api/monsters/templates/{created.Id}", new { name = "Capitán bandido", maxHp = 52, ac = 15, initiativeBonus = 3 });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = (await client.GetFromJsonAsync<List<TemplateDto>>("/api/monsters/templates", TestJson.Options))!;
        list.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            created.Id, Name = "Capitán bandido", MaxHp = 52, HpFormula = (string?)null, Notes = string.Empty,
        });

        (await client.DeleteAsync($"/api/monsters/templates/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/monsters/templates/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Templates_of_another_dm_are_not_found()
    {
        var ana = await postgres.Factory.CreateDmClientAsync();
        var bea = await postgres.Factory.CreateDmClientAsync();
        var template = await CreatedAsync(await ana.PostAsJsonAsync("/api/monsters/templates", Veteran));

        (await bea.GetAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.PutAsJsonAsync($"/api/monsters/templates/{template.Id}", Veteran)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.DeleteAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bea.GetFromJsonAsync<List<TemplateDto>>("/api/monsters/templates", TestJson.Options)).Should().BeEmpty();
        (await ana.GetAsync($"/api/monsters/templates/{template.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_templates_are_rejected()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var badFormula = await client.PostAsJsonAsync(
            "/api/monsters/templates", new { name = "Lobo", maxHp = 11, ac = 12, initiativeBonus = 2, hpFormula = "2d7" });
        var noName = await client.PostAsJsonAsync("/api/monsters/templates", new { maxHp = 11, ac = 12, initiativeBonus = 2 });

        badFormula.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(badFormula)).Should().Be("InvalidValue");
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_template_can_start_as_a_copy_of_an_srd_monster()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var copy = await CreatedAsync(await client.PostAsync("/api/monsters/templates/from-srd/owlbear?lang=es", null));
        var missing = await client.PostAsync("/api/monsters/templates/from-srd/kraken-of-nowhere", null);

        copy.Should().BeEquivalentTo(new
        {
            Name = "Oso lechuza", MaxHp = 59, Ac = 13, InitiativeBonus = 1, HpFormula = "7d10+21", Notes = string.Empty,
        });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).Should().Be("MonsterNotFound");
    }
}
