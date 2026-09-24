using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> b)
    {
        b.ToTable("external_logins");
        b.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(255).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        // One account per provider identity, and one identity per provider per account.
        b.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
