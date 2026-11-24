using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.Hosting;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class JoinTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_guest_joins_with_the_code_and_a_name()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode.ToLowerInvariant()}/join", new { name = "  Ana " });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var joined = (await response.Content.ReadFromJsonAsync<Scenario.JoinResponse>(TestJson.Options))!;
        joined.SessionId.Should().Be(session.SessionId);
        joined.Name.Should().Be("Ana");
        joined.GuestId.Should().NotBeEmpty();
        joined.GuestToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Every_join_is_a_new_guest()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var first = await postgres.Factory.JoinAsync(session.JoinCode, "Ana");
        var second = await postgres.Factory.JoinAsync(session.JoinCode, "Ana");

        second.GuestId.Should().NotBe(first.GuestId);
    }

    [Theory]
    [InlineData("ZZZZZ2")]
    [InlineData("bad")]
    public async Task Unknown_or_malformed_codes_are_not_found(string code)
    {
        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{code}/join", new { name = "Ana" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Blank_or_long_names_are_rejected(string name)
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("InvalidValue");
    }

    [Fact]
    public async Task A_missing_name_is_a_bad_request()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_ended_session_cannot_be_joined()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");
        await postgres.ChangeSessionAsync(session.SessionId, s => s.End(new Actor.Dm(s.OwnerId), DateTimeOffset.UtcNow));

        var response = await postgres.Factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name = "Ana" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOf(response)).Should().Be("SessionEnded");
    }

    [Fact]
    public async Task Joining_is_rate_limited()
    {
        var dm = await postgres.Factory.CreateDmClientAsync();
        var session = await dm.CreateSessionAsync("Lyra");
        await using var limited = postgres.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:JoinPermitsPerMinute", "2"));
        var client = limited.CreateHttpsClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync($"/api/sessions/{session.JoinCode}/join", new { name = "Ana" })).StatusCode);
        }

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}
