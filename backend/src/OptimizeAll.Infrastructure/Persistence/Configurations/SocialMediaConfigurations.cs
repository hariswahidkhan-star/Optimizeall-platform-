using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Social media management (Modules/SocialMedia). Tables are prefixed "sm_". Long free text is left unbounded (no max
// length) so the portable mapping picks a text type on MySQL and SQLite; the API validates lengths.

internal sealed class BrandProfileConfiguration : IEntityTypeConfiguration<BrandProfile>
{
    public void Configure(EntityTypeBuilder<BrandProfile> b)
    {
        b.ToTable("sm_brand_profiles");
        b.Property(x => x.Handle).HasMaxLength(150).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProfileUrl).HasMaxLength(500);
        b.Property(x => x.AvatarUrl).HasMaxLength(500);
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.StatusMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Network, x.Handle }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialClientSettingsConfiguration : IEntityTypeConfiguration<SocialClientSettings>
{
    public void Configure(EntityTypeBuilder<SocialClientSettings> b)
    {
        b.ToTable("sm_client_settings");
        b.HasKey(x => x.ClientAccountId);
        b.Property(x => x.DefaultUtmMedium).HasMaxLength(100).IsRequired();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialCampaignConfiguration : IEntityTypeConfiguration<SocialCampaign>
{
    public void Configure(EntityTypeBuilder<SocialCampaign> b)
    {
        b.ToTable("sm_campaigns");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.UtmCampaign).HasMaxLength(150).IsRequired();
        b.Property(x => x.UtmSource).HasMaxLength(100);
        b.Property(x => x.UtmMedium).HasMaxLength(100);
        b.Property(x => x.UtmContent).HasMaxLength(150);
        b.Property(x => x.UtmTerm).HasMaxLength(150);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialPostConfiguration : IEntityTypeConfiguration<SocialPost>
{
    public void Configure(EntityTypeBuilder<SocialPost> b)
    {
        b.ToTable("sm_posts");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.ClientAccountId, x.ScheduledAt });
        b.HasIndex(x => new { x.Status, x.ScheduledAt });
        b.HasIndex(x => new { x.RecycledFromPostId, x.RecycleNumber }).IsUnique();
        b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialPostVariantConfiguration : IEntityTypeConfiguration<SocialPostVariant>
{
    public void Configure(EntityTypeBuilder<SocialPostVariant> b)
    {
        b.ToTable("sm_post_variants");
        b.Property(x => x.Text).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.MediaIds).HasJsonList();
        b.Property(x => x.AltTexts).HasJsonList();
        b.Property(x => x.Hashtags).HasJsonList();
        b.Property(x => x.Mentions).HasJsonList();
        b.Property(x => x.Link).HasMaxLength(2000);
        b.Property(x => x.FirstComment);
        b.Property(x => x.FailureReason).HasMaxLength(2000);
        b.Property(x => x.ExternalPostId).HasMaxLength(200);
        b.Property(x => x.PublishedUrl).HasMaxLength(1000);
        b.HasIndex(x => x.ProfileId);
        b.HasIndex(x => new { x.ClientAccountId, x.ExternalPostId });
        b.HasOne<BrandProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SocialPostCommentConfiguration : IEntityTypeConfiguration<SocialPostComment>
{
    public void Configure(EntityTypeBuilder<SocialPostComment> b)
    {
        b.ToTable("sm_post_comments");
        b.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.PostId, x.CreatedAt });
        b.HasOne<SocialPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialPublishAttemptConfiguration : IEntityTypeConfiguration<SocialPublishAttempt>
{
    public void Configure(EntityTypeBuilder<SocialPublishAttempt> b)
    {
        b.ToTable("sm_publish_attempts");
        b.Property(x => x.Outcome).HasMaxLength(40).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000);
        b.Property(x => x.ExternalPostId).HasMaxLength(200);
        b.HasIndex(x => new { x.PostId, x.StartedAt });
        b.HasIndex(x => new { x.ClientAccountId, x.StartedAt });
        b.HasOne<SocialPost>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialMediaAssetConfiguration : IEntityTypeConfiguration<SocialMediaAsset>
{
    public void Configure(EntityTypeBuilder<SocialMediaAsset> b)
    {
        b.ToTable("sm_media_assets");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.ExternalUrl).HasMaxLength(2000);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.AltText).HasMaxLength(1000);
        b.Property(x => x.Tags).HasJsonList();
        b.HasIndex(x => new { x.ClientAccountId, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialHashtagSetConfiguration : IEntityTypeConfiguration<SocialHashtagSet>
{
    public void Configure(EntityTypeBuilder<SocialHashtagSet> b)
    {
        b.ToTable("sm_hashtag_sets");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Hashtags).HasJsonList();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialCaptionSnippetConfiguration : IEntityTypeConfiguration<SocialCaptionSnippet>
{
    public void Configure(EntityTypeBuilder<SocialCaptionSnippet> b)
    {
        b.ToTable("sm_caption_snippets");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialQueueSlotConfiguration : IEntityTypeConfiguration<SocialQueueSlot>
{
    public void Configure(EntityTypeBuilder<SocialQueueSlot> b)
    {
        b.ToTable("sm_queue_slots");
        b.HasIndex(x => new { x.ProfileId, x.DayOfWeek, x.MinuteOfDay }).IsUnique();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<BrandProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialNetworkPresetConfiguration : IEntityTypeConfiguration<SocialNetworkPreset>
{
    public void Configure(EntityTypeBuilder<SocialNetworkPreset> b)
    {
        b.ToTable("sm_network_presets");
        b.HasKey(x => x.Network);
        b.Property(x => x.RecommendedTimes).HasJsonList();
        b.Property(x => x.Source).HasMaxLength(500).IsRequired();
    }
}

internal sealed class SocialAwarenessDayConfiguration : IEntityTypeConfiguration<SocialAwarenessDay>
{
    public void Configure(EntityTypeBuilder<SocialAwarenessDay> b)
    {
        b.ToTable("sm_awareness_days");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Countries).HasJsonList();
        b.Property(x => x.SourceUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.SeedKey).HasMaxLength(120);
        b.HasIndex(x => new { x.Month, x.Day, x.Name }).IsUnique();
        b.HasIndex(x => x.SeedKey).IsUnique();
    }
}

internal sealed class SocialListeningQueryConfiguration : IEntityTypeConfiguration<SocialListeningQuery>
{
    public void Configure(EntityTypeBuilder<SocialListeningQuery> b)
    {
        b.ToTable("sm_listening_queries");
        b.Property(x => x.Term).HasMaxLength(150).IsRequired();
        b.Property(x => x.Networks).HasJsonList();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialMentionConfiguration : IEntityTypeConfiguration<SocialMention>
{
    public void Configure(EntityTypeBuilder<SocialMention> b)
    {
        b.ToTable("sm_mentions");
        b.Property(x => x.AuthorHandle).HasMaxLength(150).IsRequired();
        b.Property(x => x.Text).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.ClientAccountId, x.DedupeKey }).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.PostedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialInboxItemConfiguration : IEntityTypeConfiguration<SocialInboxItem>
{
    public void Configure(EntityTypeBuilder<SocialInboxItem> b)
    {
        b.ToTable("sm_inbox_items");
        b.Property(x => x.AuthorHandle).HasMaxLength(150).IsRequired();
        b.Property(x => x.Text).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.ClientAccountId, x.DedupeKey }).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Status, x.ReceivedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialInboxReplyConfiguration : IEntityTypeConfiguration<SocialInboxReply>
{
    public void Configure(EntityTypeBuilder<SocialInboxReply> b)
    {
        b.ToTable("sm_inbox_replies");
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(200);
        b.HasIndex(x => new { x.ItemId, x.CreatedAt });
        b.HasOne<SocialInboxItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialPostMetricConfiguration : IEntityTypeConfiguration<SocialPostMetric>
{
    public void Configure(EntityTypeBuilder<SocialPostMetric> b)
    {
        b.ToTable("sm_post_metrics");
        b.Property(x => x.PostKey).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.ProfileId, x.PostKey, x.Date }).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Date });
        b.HasIndex(x => x.VariantId);
        b.HasOne<BrandProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialProfileMetricConfiguration : IEntityTypeConfiguration<SocialProfileMetric>
{
    public void Configure(EntityTypeBuilder<SocialProfileMetric> b)
    {
        b.ToTable("sm_profile_metrics");
        b.HasIndex(x => new { x.ProfileId, x.Date }).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Date });
        b.HasOne<BrandProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialMetricImportConfiguration : IEntityTypeConfiguration<SocialMetricImport>
{
    public void Configure(EntityTypeBuilder<SocialMetricImport> b)
    {
        b.ToTable("sm_metric_imports");
        b.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.Errors).HasJsonList();
        b.HasIndex(x => new { x.ClientAccountId, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialCompetitorConfiguration : IEntityTypeConfiguration<SocialCompetitor>
{
    public void Configure(EntityTypeBuilder<SocialCompetitor> b)
    {
        b.ToTable("sm_competitors");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Handle).HasMaxLength(150).IsRequired();
        b.Property(x => x.ProfileUrl).HasMaxLength(500);
        b.HasIndex(x => new { x.ClientAccountId, x.Network, x.Handle }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SocialCompetitorSnapshotConfiguration : IEntityTypeConfiguration<SocialCompetitorSnapshot>
{
    public void Configure(EntityTypeBuilder<SocialCompetitorSnapshot> b)
    {
        b.ToTable("sm_competitor_snapshots");
        b.HasIndex(x => new { x.CompetitorId, x.Date }).IsUnique();
        b.HasOne<SocialCompetitor>().WithMany().HasForeignKey(x => x.CompetitorId).OnDelete(DeleteBehavior.Cascade);
    }
}
