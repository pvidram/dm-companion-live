using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public static class HubClients
{
    /// <summary>
    /// A started connection to the combat hub through the in-memory test server. Long polling,
    /// because the test server's handler does not do WebSockets; the hub code is the same.
    /// </summary>
    public static async Task<HubConnection> ConnectAsync(
        this WebApplicationFactory<Program> factory, string? cookie = null, string? guestToken = null, string? lang = null)
    {
        var url = lang is null ? "https://localhost/hubs/combat" : $"https://localhost/hubs/combat?lang={lang}";
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (cookie is not null)
                {
                    options.Headers["Cookie"] = cookie;
                }

                if (guestToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(guestToken);
                }
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
