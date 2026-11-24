using System.Net;
using System.Net.Http.Json;
using DmCompanionLive.Api.Features.Combat;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Combat;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class SessionExpiryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_idle_session_can_no_longer_be_joined_or_commanded()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = postgres.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var dm = await factory.CreateDmAsync();
        var session = await dm.Http.CreateSessionAsync("Lyra");
        await using var hub = await factory.ConnectAsync(cookie: dm.Cookie);
        var joined = await hub.InvokeAsync<SnapshotResult>("Join", session.JoinCode);

        clock.Advance(CombatSession.InactivityLimit + TimeSpan.FromMinutes(1));

        var command = await hub.InvokeAsync<CommandResult>("Start", joined.Dm!.Version);
        var join = await factory.CreateHttpsClient().PostAsJsonAsync(
            $"/api/sessions/{session.JoinCode}/join", new { name = "Ana" });

        command.Error.Should().Be("SessionExpired");
        join.StatusCode.Should().Be(HttpStatusCode.Gone);
    }
}
