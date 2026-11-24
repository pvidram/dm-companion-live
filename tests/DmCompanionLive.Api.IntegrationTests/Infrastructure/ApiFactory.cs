using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DmCompanionLive.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        // All test requests share one (null) remote address: keep the join limit out of the way.
        builder.UseSetting("RateLimiting:JoinPermitsPerMinute", "100000");
    }
}
