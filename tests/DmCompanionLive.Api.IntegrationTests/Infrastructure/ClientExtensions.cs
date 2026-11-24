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
}
