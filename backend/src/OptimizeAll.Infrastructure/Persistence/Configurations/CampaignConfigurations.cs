using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class CampaignCategoryConfiguration : IEntityTypeConfiguration<CampaignCategory>
{
    public void Configure(EntityTypeBuilder<CampaignCategory> b)
    {
        b.ToTable("campaign_categories");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Icon).HasMaxLength(50);
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("campaigns");
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.Topics).HasJsonList();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.PostingInstructions).HasColumnType("text").IsRequired();
        b.Property(x => x.DefaultDisclosureText).HasMaxLength(300).IsRequired();
        b.Property(x => x.RequiredHashtags).HasMaxLength(300);
        b.Property(x => x.RequiredMentions).HasMaxLength(300);
        b.Property(x => x.BudgetCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.LandingHeadline).HasMaxLength(200);
        b.Property(x => x.LandingBody).HasColumnType("text");
        b.Property(x => x.HeroImageUrl).HasMaxLength(500);
        b.Property(x => x.TrackingDestinationUrl).HasMaxLength(1000);
        b.Property(x => x.UtmCampaign).HasMaxLength(100);
        b.HasIndex(x => new { x.Status, x.StartsAt, x.EndsAt });
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        b.OwnsOne(x => x.Eligibility, e =>
        {
            e.Property(p => p.MinAccountAgeDays).HasColumnName("elig_min_account_age_days");
            e.Property(p => p.MinFollowers).HasColumnName("elig_min_followers");
            e.Property(p => p.RequireVerifiedAccount).HasColumnName("elig_require_verified_account");
            e.Property(p => p.Countries).HasColumnName("elig_countries").HasJsonList();
            e.Property(p => p.Languages).HasColumnName("elig_languages").HasJsonList();
            e.Property(p => p.Interests).HasColumnName("elig_interests").HasJsonList();
            e.Property(p => p.Tiers).HasColumnName("elig_tiers").HasJsonList();
        });
        b.Navigation(x => x.Eligibility).IsRequired();

        b.HasMany(x => x.Platforms).WithOne().HasForeignKey(p => p.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Assets).WithOne().HasForeignKey(a => a.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Disclosures).WithOne().HasForeignKey(d => d.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CampaignPlatformConfiguration : IEntityTypeConfiguration<CampaignPlatform>
{
    public void Configure(EntityTypeBuilder<CampaignPlatform> b)
    {
        b.ToTable("campaign_platforms");
        b.HasKey(x => new { x.CampaignId, x.Platform });
    }
}

internal sealed class CampaignAssetConfiguration : IEntityTypeConfiguration<CampaignAsset>
{
    public void Configure(EntityTypeBuilder<CampaignAsset> b)
    {
        b.ToTable("campaign_assets");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.Body).HasColumnType("text");
        b.HasIndex(x => new { x.CampaignId, x.SortOrder });
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<PostTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CampaignDisclosureConfiguration : IEntityTypeConfiguration<CampaignDisclosure>
{
    public void Configure(EntityTypeBuilder<CampaignDisclosure> b)
    {
        b.ToTable("campaign_disclosures");
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.CampaignId, x.Platform, x.CountryCode }).IsUnique();
    }
}

internal sealed class PostTemplateConfiguration : IEntityTypeConfiguration<PostTemplate>
{
    public void Configure(EntityTypeBuilder<PostTemplate> b)
    {
        b.ToTable("post_templates");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasColumnType("text").IsRequired();
        b.Property(x => x.Hashtags).HasMaxLength(500);
        b.Property(x => x.LanguageCode).HasMaxLength(10);
    }
}

internal sealed class ContentCalendarEntryConfiguration : IEntityTypeConfiguration<ContentCalendarEntry>
{
    public void Configure(EntityTypeBuilder<ContentCalendarEntry> b)
    {
        b.ToTable("content_calendar_entries");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => x.ScheduledFor);
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PostTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class RewardRuleSetConfiguration : IEntityTypeConfiguration<RewardRuleSet>
{
    public void Configure(EntityTypeBuilder<RewardRuleSet> b)
    {
        b.ToTable("reward_rule_sets");
        b.HasIndex(x => new { x.CampaignId, x.Version }).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ChangeReason).HasMaxLength(500).IsRequired();
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Rules).WithOne().HasForeignKey(r => r.RuleSetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RewardRuleConfiguration : IEntityTypeConfiguration<RewardRule>
{
    public void Configure(EntityTypeBuilder<RewardRule> b)
    {
        b.ToTable("reward_rules");
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Label).HasMaxLength(150);
        b.ToTable(t => t.HasCheckConstraint("ck_reward_rules_amount_nonnegative", "`Amount` >= 0"));
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("stored_files");
        b.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => x.Sha256);
        b.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        b.HasIndex(x => x.OwnerUserId);
    }
}
