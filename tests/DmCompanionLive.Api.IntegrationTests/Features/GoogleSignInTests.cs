using System.Net;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using DmCompanionLive.Domain.Users;
using Microsoft.AspNetCore.Hosting;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class GoogleSignInTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Without_credentials_google_sign_in_is_not_offered()
    {
        var response = await postgres.Factory.CreateHttpsClient().GetAsync("/api/auth/google/login");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_credentials_sign_in_redirects_to_google()
    {
        await using var factory = postgres.Factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:Google:ClientId", "test-client-id")
            .UseSetting("Authentication:Google:ClientSecret", "test-client-secret"));

        var response = await factory.CreateHttpsClient().GetAsync("/api/auth/google/login?returnUrl=/tables");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("https://accounts.google.com/");
        location.Should().Contain("client_id=test-client-id");
        location.Should().Contain(Uri.EscapeDataString("https://localhost/signin-google"));
    }

    [Fact]
    public async Task The_same_google_account_always_maps_to_the_same_dm()
    {
        var externalId = $"google:{Guid.NewGuid():N}";
        await using var db = postgres.CreateDbContext();

        var first = await DmUsers.FindOrCreateAsync(db, externalId, "Ana", DateTimeOffset.UtcNow, default);
        var second = await DmUsers.FindOrCreateAsync(db, externalId, "Ana B.", DateTimeOffset.UtcNow, default);

        second.Id.Should().Be(first.Id);
    }

    [Theory]
    [InlineData(null, "DM")]
    [InlineData("   ", "DM")]
    [InlineData(" Ana ", "Ana")]
    public void Display_names_from_google_are_cleaned(string? name, string expected) =>
        DmUsers.DisplayNameFrom(name).Should().Be(expected);

    [Fact]
    public void Long_display_names_from_google_are_shortened() =>
        DmUsers.DisplayNameFrom(new string('a', 120)).Should().HaveLength(DmUser.MaxDisplayNameLength);

    [Theory]
    [InlineData("/tables", "/tables")]
    [InlineData(null, "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void Only_local_return_urls_are_kept(string? returnUrl, string expected) =>
        AuthEndpoints.SafeReturnUrl(returnUrl).Should().Be(expected);
}
