using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class ClientAccountConfiguration : IEntityTypeConfiguration<ClientAccount>
{
    public void Configure(EntityTypeBuilder<ClientAccount> b)
    {
        b.ToTable("client_accounts");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Industry).HasMaxLength(100);
        b.Property(x => x.Website).HasMaxLength(500);
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.BillingEmail).HasMaxLength(254);
        b.Property(x => x.BillingAddress).HasMaxLength(1000);
        b.Property(x => x.TaxId).HasMaxLength(64);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.AccountManagerUserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AccountManagerUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientMemberConfiguration : IEntityTypeConfiguration<ClientMember>
{
    public void Configure(EntityTypeBuilder<ClientMember> b)
    {
        b.ToTable("client_members");
        b.HasKey(x => new { x.ClientAccountId, x.UserId });
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
