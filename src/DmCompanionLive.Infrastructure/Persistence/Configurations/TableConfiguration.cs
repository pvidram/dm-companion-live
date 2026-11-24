using DmCompanionLive.Domain.Users;
using DmCompanionLive.Domain.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("tables");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(Table.MaxNameLength).IsRequired();
        builder.HasIndex(t => t.OwnerId);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);

        builder.OwnsMany(t => t.Characters, character =>
        {
            character.ToTable("player_characters");
            character.WithOwner().HasForeignKey("TableId");
            character.HasKey(c => c.Id);
            character.Property(c => c.Id).ValueGeneratedNever();
            character.Property(c => c.Name).HasMaxLength(80).IsRequired();
        });
        builder.Navigation(t => t.Characters).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
