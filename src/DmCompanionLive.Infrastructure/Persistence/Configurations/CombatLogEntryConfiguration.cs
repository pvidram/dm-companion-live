using DmCompanionLive.Domain.Combat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class CombatLogEntryConfiguration : IEntityTypeConfiguration<CombatLogEntry>
{
    public void Configure(EntityTypeBuilder<CombatLogEntry> builder)
    {
        builder.ToTable("combat_log");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.SessionId, e.Version }).IsUnique();
        builder.Property(e => e.ActorName).HasMaxLength(Combatant.MaxNameLength).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Parameters).HasColumnType("jsonb").IsRequired();
        builder.HasOne<CombatSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
