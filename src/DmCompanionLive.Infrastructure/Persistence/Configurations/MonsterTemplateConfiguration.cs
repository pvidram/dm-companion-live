using DmCompanionLive.Domain;
using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class MonsterTemplateConfiguration : IEntityTypeConfiguration<MonsterTemplate>
{
    public void Configure(EntityTypeBuilder<MonsterTemplate> builder)
    {
        builder.ToTable("monster_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Names.MaxLength).IsRequired();
        builder.Property(t => t.HpFormula).HasMaxLength(32);
        builder.Property(t => t.Notes).HasMaxLength(MonsterTemplate.MaxNotesLength).IsRequired();
        builder.HasIndex(t => t.OwnerId);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
