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
        b.Property(x => x.Notes);
        b.Property(x => x.StatusReason).HasMaxLength(1000);
        b.Property(x => x.Summary).HasMaxLength(1000);
        b.Property(x => x.BillingContactName).HasMaxLength(200);
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

internal sealed class ClientTeamAssignmentConfiguration : IEntityTypeConfiguration<ClientTeamAssignment>
{
    public void Configure(EntityTypeBuilder<ClientTeamAssignment> b)
    {
        b.ToTable("client_team_assignments");
        b.HasIndex(x => new { x.ClientAccountId, x.UserId, x.ServiceRole }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientOnboardingItemConfiguration : IEntityTypeConfiguration<ClientOnboardingItem>
{
    public void Configure(EntityTypeBuilder<ClientOnboardingItem> b)
    {
        b.ToTable("client_onboarding_items");
        b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Category).HasMaxLength(64).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Key }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BrandKitConfiguration : IEntityTypeConfiguration<BrandKit>
{
    public void Configure(EntityTypeBuilder<BrandKit> b)
    {
        b.ToTable("brand_kits");
        b.HasIndex(x => x.ClientAccountId).IsUnique();
        b.Property(x => x.Colors).HasJsonList();
        b.Property(x => x.Fonts).HasJsonList();
        b.Property(x => x.Personas).HasJsonList();
        b.Property(x => x.Competitors).HasJsonList();
        b.Property(x => x.Dos).HasJsonList();
        b.Property(x => x.Donts).HasJsonList();
        b.Property(x => x.KeyMessages).HasJsonList();
        b.Property(x => x.ToneOfVoice);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BrandAssetConfiguration : IEntityTypeConfiguration<BrandAsset>
{
    public void Configure(EntityTypeBuilder<BrandAsset> b)
    {
        b.ToTable("brand_assets");
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientFeedbackConfiguration : IEntityTypeConfiguration<ClientFeedback>
{
    public void Configure(EntityTypeBuilder<ClientFeedback> b)
    {
        b.ToTable("client_feedback");
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.Period).HasMaxLength(16);
        b.Property(x => x.DedupeKey).HasMaxLength(160).IsRequired();
        b.HasIndex(x => x.DedupeKey).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Kind, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
