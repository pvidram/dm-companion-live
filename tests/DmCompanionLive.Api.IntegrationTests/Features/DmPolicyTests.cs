using System.Security.Claims;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Api.IntegrationTests.Features;

[Collection(PostgresCollection.Name)]
public sealed class DmPolicyTests(PostgresFixture postgres)
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    private async Task<bool> SucceedsAsync(ClaimsPrincipal principal)
    {
        using var scope = postgres.Factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(principal, AuthSetup.DmPolicy)).Succeeded;
    }

    [Fact]
    public async Task An_authenticated_principal_without_the_dm_role_is_rejected() =>
        (await SucceedsAsync(Principal(new Claim(ClaimTypes.Name, "Someone")))).Should().BeFalse();

    [Fact]
    public async Task A_principal_with_the_dm_role_is_accepted() =>
        (await SucceedsAsync(Principal(new Claim("role", "dm")))).Should().BeTrue();

    [Fact]
    public async Task An_anonymous_principal_is_rejected() =>
        (await SucceedsAsync(new ClaimsPrincipal(new ClaimsIdentity()))).Should().BeFalse();
}
