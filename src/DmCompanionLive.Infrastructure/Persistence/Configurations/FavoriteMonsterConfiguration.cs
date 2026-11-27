using DmCompanionLive.Domain.Monsters;
using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class FavoriteMonsterConfiguration : IEntityTypeConfiguration<FavoriteMonster>
{
    public void Configure(EntityTypeBuilder<FavoriteMonster> builder)
    {
        builder.ToTable("favorite_monsters");
        builder.HasKey(f => new { f.OwnerId, f.SrdSlug });
        builder.Property(f => f.SrdSlug).HasMaxLength(FavoriteMonster.MaxSlugLength);
        builder.HasOne<DmUser>().WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
