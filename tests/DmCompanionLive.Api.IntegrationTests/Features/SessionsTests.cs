using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Api.Features.Sessions;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SessionsTests(PostgresFixture postgres)
{
    private sealed record TableDto(Guid Id, string Name);

    private sealed record SessionCreatedDto(Guid SessionId, string JoinCode);

    [Theory]
    [InlineData("ZZZZZ2")]
    [InlineData("bad")]
    public async Task Unknown_or_malformed_codes_are_not_found(string code)
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        (await client.GetAsync($"/api/sessions/{code}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_table_can_run_several_sessions()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);

        var first = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);
        var second = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Location.Should().NotBe(second.Headers.Location);
    }

    [Fact]
    public async Task A_join_code_collision_is_retried_with_a_new_code()
    {
        var codes = new QueuedCodes();
        await using var factory = postgres.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IJoinCodeGenerator>(codes)));
        var client = await factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);
        var taken = (await (await client.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!.JoinCode;
        var fresh = JoinCode.New();
        codes.Enqueue(taken, fresh);

        var response = await client.PostAsync($"/api/tables/{table.Id}/sessions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!.JoinCode.Should().Be(fresh);
    }

    private sealed class QueuedCodes : IJoinCodeGenerator
    {
        private readonly ConcurrentQueue<string> _codes = new();

        public void Enqueue(params string[] codes)
        {
            foreach (var code in codes)
            {
                _codes.Enqueue(code);
            }
        }

        public string New() => _codes.TryDequeue(out var code) ? code : JoinCode.New();
    }

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
    public async Task Conditions_travel_as_a_json_array_of_strings()
    {
        var client = await postgres.Factory.CreateDmClientAsync();
        var table = await CreateTableWithLyraAsync(client);
        var created = (await (await client.PostAsync($"/api/tables/{table.Id}/sessions", null))
            .Content.ReadFromJsonAsync<SessionCreatedDto>(TestJson.Options))!;

        var json = await client.GetStringAsync($"/api/sessions/{created.JoinCode}");

        var conditions = System.Text.Json.JsonDocument.Parse(json).RootElement
            .GetProperty("combatants")[0].GetProperty("conditions");
        conditions.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        conditions.GetArrayLength().Should().Be(0);
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
