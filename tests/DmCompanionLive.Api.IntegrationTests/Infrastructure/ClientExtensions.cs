using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class ClientExtensions
{
    /// <summary>HTTPS base address so the Secure auth cookie is sent back.</summary>
    public static HttpClient CreateHttpsClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

    public static async Task<HttpClient> CreateDmClientAsync(this WebApplicationFactory<Program> factory, string? name = null)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>A DM whose auth cookie is also available as a raw header value for hub connections.</summary>
    public sealed record TestDm(HttpClient Http, string Cookie);

    public static async Task<TestDm> CreateDmAsync(this WebApplicationFactory<Program> factory, string? name = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';')[0])
            .Single(pair => pair.StartsWith("dmcl.auth=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return new TestDm(client, cookie);
    }
}
