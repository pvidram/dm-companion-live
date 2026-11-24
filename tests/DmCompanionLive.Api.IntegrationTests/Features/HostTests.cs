using System.Net;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class HostTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_reports_healthy_with_the_database()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task The_openapi_document_is_published()
    {
        var client = postgres.Factory.CreateHttpsClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"openapi\"");
    }
}
