using DmCompanionLive.Domain.Tables;
using DmCompanionLive.Domain.Users;
using DmCompanionLive.Domain.Combat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class CombatSessionConfiguration : IEntityTypeConfiguration<CombatSession>
{
    public void Configure(EntityTypeBuilder<CombatSession> builder)
    {
        builder.ToTable("combat_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.JoinCode).HasMaxLength(JoinCode.Length).IsRequired();
        builder.HasIndex(s => s.JoinCode).IsUnique();
        builder.HasIndex(s => s.OwnerId);
        builder.HasOne<Table>().WithMany().HasForeignKey(s => s.TableId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Ignore(s => s.UncommittedEvents);
        builder.Ignore(s => s.TurnOrder);

        builder.OwnsMany(s => s.Combatants, combatant =>
        {
            combatant.ToTable("combatants");
            combatant.WithOwner().HasForeignKey("SessionId");
            combatant.HasKey(c => c.Id);
            combatant.Property(c => c.Id).ValueGeneratedNever();
            combatant.Property(c => c.Name).HasMaxLength(Combatant.MaxNameLength).IsRequired();
            combatant.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
            combatant.Property(c => c.SourceKind).HasConversion<string>().HasMaxLength(16);
            combatant.Property(c => c.SourceId).HasMaxLength(120);
            combatant.Ignore(c => c.IsDefeated);
        });
        builder.Navigation(s => s.Combatants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
