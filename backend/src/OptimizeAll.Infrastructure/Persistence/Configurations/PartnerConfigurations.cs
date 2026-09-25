using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

/// <summary>Partners and sponsored placements (docs/WEBSITE.md "Partners and sponsored placements").</summary>
internal sealed class WebsitePartnerConfiguration : IEntityTypeConfiguration<WebsitePartner>
{
    public void Configure(EntityTypeBuilder<WebsitePartner> b)
    {
        b.ToTable("website_partners");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.WebsiteUrl).HasMaxLength(500);
        b.Property(x => x.Tagline).HasMaxLength(160).IsRequired();
        b.Property(x => x.DescriptionMarkdown);
        b.Property(x => x.RelationshipLabel).HasMaxLength(160).IsRequired();
        b.Property(x => x.Highlights).HasJsonList();
        b.Property(x => x.Offerings).HasJsonList();
        b.Property(x => x.Keywords).HasJsonList();
        b.Property(x => x.Categories).HasJsonList();
        b.Property(x => x.SameAs).HasJsonList();
        b.Property(x => x.RelatedPartnerIds).HasJsonList();
        b.Property(x => x.Slots).HasJsonList();
        b.Property(x => x.BrandColor).HasMaxLength(7);
        b.Property(x => x.UtmSource).HasMaxLength(100).IsRequired();
        b.Property(x => x.UtmMedium).HasMaxLength(100).IsRequired();
        b.Property(x => x.UtmCampaign).HasMaxLength(100);
        b.Property(x => x.OfferText).HasMaxLength(200);
        b.Property(x => x.OfferCode).HasMaxLength(40);
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

internal sealed class WebsitePartnerStatConfiguration : IEntityTypeConfiguration<WebsitePartnerStat>
{
    public void Configure(EntityTypeBuilder<WebsitePartnerStat> b)
    {
        b.ToTable("website_partner_stats");
        b.Property(x => x.Slot).HasMaxLength(40).IsRequired();
        b.Property(x => x.PagePath).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.PartnerId, x.Slot, x.PagePath, x.Day }).IsUnique();
        b.HasIndex(x => x.Day);
        b.HasOne<WebsitePartner>().WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
