using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class CustomRoleConfiguration : IEntityTypeConfiguration<CustomRole>
{
    public void Configure(EntityTypeBuilder<CustomRole> b)
    {
        b.ToTable("custom_roles");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
        b.Property(x => x.Description); // long text: unbounded
        b.Property(x => x.Permissions).HasJsonList();
    }
}

internal sealed class UserCustomRoleConfiguration : IEntityTypeConfiguration<UserCustomRole>
{
    public void Configure(EntityTypeBuilder<UserCustomRole> b)
    {
        b.ToTable("user_custom_roles");
        b.HasKey(x => new { x.UserId, x.CustomRoleId });
        b.HasIndex(x => x.CustomRoleId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CustomRole>().WithMany().HasForeignKey(x => x.CustomRoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
