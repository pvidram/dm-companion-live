using System.Net.Http.Json;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class ClientExtensions
{
    public static async Task<HttpClient> CreateDmClientAsync(this ApiFactory factory, string? name = null)
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/dev/login", new { displayName = name ?? $"DM {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
