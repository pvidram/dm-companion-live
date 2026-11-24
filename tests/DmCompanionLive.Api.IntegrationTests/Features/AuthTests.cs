using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id, string DisplayName);

    [Fact]
    public async Task Anonymous_callers_get_401_from_me()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dev_login_signs_in_and_reuses_the_same_user()
    {
        var name = $"DM {Guid.NewGuid():N}";
        var first = await postgres.Factory.CreateDmClientAsync(name);
        var second = await postgres.Factory.CreateDmClientAsync(name);

        var me1 = await first.GetFromJsonAsync<Me>("/api/auth/me", TestJson.Options);
        var me2 = await second.GetFromJsonAsync<Me>("/api/auth/me", TestJson.Options);

        me1!.DisplayName.Should().Be(name);
        me2!.Id.Should().Be(me1.Id);
    }

    [Fact]
    public async Task Logout_clears_the_session()
    {
        var client = await postgres.Factory.CreateDmClientAsync();

        var logout = await client.PostAsync("/api/auth/logout", null);
        var me = await client.GetAsync("/api/auth/me");

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_invalid_display_name_is_a_400_problem_with_a_code()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = new string('x', 81) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"InvalidValue\"");
    }

    [Fact]
    public async Task Dev_login_is_not_mapped_in_production()
    {
        await using var production = postgres.Factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = "DM" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_auth_cookie_is_httponly_secure_and_lax()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = $"DM {Guid.NewGuid():N}" });

        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dmcl.auth=")).ToLowerInvariant();
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=lax");
    }
}
