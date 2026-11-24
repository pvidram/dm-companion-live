using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DmCompanionLive.Api.Auth;

public static class AuthSetup
{
    public const string DmPolicy = "Dm";

    /// <summary>The DM (cookie) or a player (guest token): whoever may connect to a combat.</summary>
    public const string CombatPolicy = "CombatParticipant";

    public static IServiceCollection AddDmAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDataProtection();
        services.AddSingleton<GuestTokens>();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "dmcl.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                // An API answers 401/403; it never redirects to a login page.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, GuestTokenHandler>(GuestTokenHandler.Scheme, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(DmPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(AppClaims.Role, AppClaims.DmRole));
            options.AddPolicy(CombatPolicy, policy => policy
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, GuestTokenHandler.Scheme)
                .RequireAuthenticatedUser());
        });
        return services;
    }
}
