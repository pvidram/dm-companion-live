using DmCompanionLive.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DmCompanionLive.Infrastructure.Persistence.Configurations;

internal sealed class DmUserConfiguration : IEntityTypeConfiguration<DmUser>
{
    public void Configure(EntityTypeBuilder<DmUser> builder)
    {
        builder.ToTable("dm_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.ExternalId).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(DmUser.MaxDisplayNameLength).IsRequired();
    }
}
