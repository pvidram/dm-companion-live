using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using DmCompanionLive.Api.Auth;
using DmCompanionLive.Api.Errors;
using DmCompanionLive.Api.Features.Auth;
using DmCompanionLive.Api.Features.Combat;
using DmCompanionLive.Api.Features.Sessions;
using DmCompanionLive.Api.Features.Tables;
using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Infrastructure;
using DmCompanionLive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing connection string 'Postgres'.");

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // A null or missing value for a non-nullable member is a 400, never a NullReferenceException (500).
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddSingleton<IJoinCodeGenerator, RandomJoinCodeGenerator>();
builder.Services.AddOpenApi();
builder.Services.AddDmAuthentication(builder.Environment, builder.Configuration);
var joinPermits = builder.Configuration.GetValue("RateLimiting:JoinPermitsPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client address. Behind a reverse proxy this needs ForwardedHeaders (Plan 5).
    options.AddPolicy(JoinSessionEndpoint.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = joinPermits, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Same shapes as the REST API: enums as names, nulls in non-nullable members rejected.
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.RespectNullableAnnotations = true;
    });
builder.Services.AddSingleton<SessionPresence>();
builder.Services.AddSingleton<CombatBroadcaster>();
builder.Services.AddScoped<CombatCommandRunner>();
builder.Services.AddSingleton<IDiceRoller, RandomDiceRoller>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapDevLogin();
}

app.MapAuth(AuthSetup.IsGoogleConfigured(app.Configuration));
app.MapTables();
app.MapSessions();
app.MapJoinSession();
app.MapHub<CombatHub>("/hubs/combat");

app.Run();

public partial class Program;
