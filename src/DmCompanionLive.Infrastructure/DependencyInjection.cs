using DmCompanionLive.Infrastructure.Persistence;
using DmCompanionLive.Infrastructure.Srd;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DmCompanionLive.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<ISrdCatalog>(_ => SrdCatalog.LoadEmbedded());
        return services;
    }
}
