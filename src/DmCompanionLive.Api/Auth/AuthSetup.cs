using System.Security.Claims;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Api.Auth;

public static class AuthSetup
{
    public const string DmPolicy = "Dm";

    /// <summary>The DM (cookie) or a player (guest token): whoever may connect to a combat.</summary>
    public const string CombatPolicy = "CombatParticipant";

    public static bool IsGoogleConfigured(IConfiguration configuration) =>
        !string.IsNullOrEmpty(configuration["Authentication:Google:ClientId"]);

    public static IServiceCollection AddDmAuthentication(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddDataProtection();
        services.AddSingleton<GuestTokens>();
        var authentication = services
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
                // A cookie outlives its user if the user is deleted: check it against the database.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    if (!Guid.TryParse(id, out var userId)
                        || !await db.Users.AnyAsync(u => u.Id == userId, context.HttpContext.RequestAborted))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            })
            .AddScheme<AuthenticationSchemeOptions, GuestTokenHandler>(GuestTokenHandler.Scheme, _ => { });

        if (IsGoogleConfigured(configuration))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = configuration["Authentication:Google:ClientId"]!;
                options.ClientSecret = configuration["Authentication:Google:ClientSecret"]
                    ?? throw new InvalidOperationException("Missing 'Authentication:Google:ClientSecret'.");
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.CallbackPath = "/signin-google";
                // Store only the Google subject and a display name; never the e-mail.
                options.Events.OnCreatingTicket = async context =>
                {
                    var principal = context.Principal!;
                    var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? throw new InvalidOperationException("Google did not return a subject.");
                    var services = context.HttpContext.RequestServices;
                    var user = await DmUsers.FindOrCreateAsync(
                        services.GetRequiredService<AppDbContext>(),
                        $"google:{subject}",
                        DmUsers.DisplayNameFrom(principal.FindFirstValue(ClaimTypes.Name)),
                        services.GetRequiredService<TimeProvider>().GetUtcNow(),
                        context.HttpContext.RequestAborted);
                    context.Principal = DmPrincipal.Create(user);
                };
            });
        }

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
