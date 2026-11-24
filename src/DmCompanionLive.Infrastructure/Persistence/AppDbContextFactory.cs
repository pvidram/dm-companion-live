using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DmCompanionLive.Infrastructure.Persistence;

/// <summary>Used only by the dotnet-ef tool to create migrations; it never connects.</summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dmcompanionlive;Username=dmcl;Password=dmcl")
            .Options);
}
