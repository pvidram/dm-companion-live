using DmCompanionLive.Domain.Combat;
using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace DmCompanionLive.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DmUser> Users => Set<DmUser>();
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<CombatSession> Sessions => Set<CombatSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
