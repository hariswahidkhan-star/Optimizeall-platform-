using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Paid advertising management (Modules/Ads). Tables are prefixed "ads_".

internal sealed class AdAccountConfiguration : IEntityTypeConfiguration<AdAccount>
{
    public void Configure(EntityTypeBuilder<AdAccount> b)
    {
        b.ToTable("ads_accounts");
        b.Property(x => x.ExternalAccountId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.StatusMessage).HasMaxLength(1000);
        b.Property(x => x.LastSyncMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Platform, x.ExternalAccountId }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdsClientSettingsConfiguration : IEntityTypeConfiguration<AdsClientSettings>
{
    public void Configure(EntityTypeBuilder<AdsClientSettings> b)
    {
        b.ToTable("ads_client_settings");
        b.HasKey(x => x.ClientAccountId);
        b.Property(x => x.CampaignNamingTemplate).HasMaxLength(300);
        b.Property(x => x.DefaultUtmSource).HasMaxLength(100).IsRequired();
        b.Property(x => x.DefaultUtmMedium).HasMaxLength(100).IsRequired();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdCampaignConfiguration : IEntityTypeConfiguration<AdCampaign>
{
    public void Configure(EntityTypeBuilder<AdCampaign> b)
    {
        b.ToTable("ads_campaigns");
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Objective).HasMaxLength(100);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.BidStrategy).HasMaxLength(100);
        b.Property(x => x.TargetingSummary).HasMaxLength(2000);
        b.HasIndex(x => new { x.AdAccountId, x.ExternalId });
        b.HasIndex(x => new { x.AdAccountId, x.Name });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AdAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdGroupConfiguration : IEntityTypeConfiguration<AdGroup>
{
    public void Configure(EntityTypeBuilder<AdGroup> b)
    {
        b.ToTable("ads_ad_groups");
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.BidStrategy).HasMaxLength(100);
        b.Property(x => x.TargetingSummary).HasMaxLength(2000);
        b.HasIndex(x => new { x.CampaignId, x.ExternalId });
        b.HasIndex(x => new { x.CampaignId, x.Name });
        b.HasOne<AdCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> b)
    {
        b.ToTable("ads_ads");
        b.Property(x => x.ExternalId).HasMaxLength(100);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.AdGroupId, x.ExternalId });
        b.HasIndex(x => new { x.AdGroupId, x.Name });
        b.HasOne<AdGroup>().WithMany().HasForeignKey(x => x.AdGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdDailyMetricConfiguration : IEntityTypeConfiguration<AdDailyMetric>
{
    public void Configure(EntityTypeBuilder<AdDailyMetric> b)
    {
        b.ToTable("ads_daily_metrics");
        b.Property(x => x.EntityKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(300).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.AdAccountId, x.Date, x.Level, x.EntityKey }).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Level, x.Date });
        b.HasIndex(x => new { x.CampaignId, x.Date });
        b.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AdAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdImportBatchConfiguration : IEntityTypeConfiguration<AdImportBatch>
{
    public void Configure(EntityTypeBuilder<AdImportBatch> b)
    {
        b.ToTable("ads_import_batches");
        b.Property(x => x.Template).HasMaxLength(40).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Errors).HasJsonList();
        b.HasIndex(x => new { x.AdAccountId, x.CreatedAt });
        b.HasOne<AdAccount>().WithMany().HasForeignKey(x => x.AdAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdBudgetConfiguration : IEntityTypeConfiguration<AdBudget>
{
    public void Configure(EntityTypeBuilder<AdBudget> b)
    {
        b.ToTable("ads_budgets");
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.ClientAccountId, x.Month });
        b.HasIndex(x => x.Month);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdAlertConfiguration : IEntityTypeConfiguration<AdAlert>
{
    public void Configure(EntityTypeBuilder<AdAlert> b)
    {
        b.ToTable("ads_alerts");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        b.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.DedupeKey).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.Status, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MediaPlanConfiguration : IEntityTypeConfiguration<MediaPlan>
{
    public void Configure(EntityTypeBuilder<MediaPlan> b)
    {
        b.ToTable("ads_media_plans");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasIndex(x => new { x.ClientAccountId, x.Month });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MediaPlanLineConfiguration : IEntityTypeConfiguration<MediaPlanLine>
{
    public void Configure(EntityTypeBuilder<MediaPlanLine> b)
    {
        b.ToTable("ads_media_plan_lines");
        b.Property(x => x.Channel).HasMaxLength(150).IsRequired();
        b.Property(x => x.Objective).HasMaxLength(100);
        b.Property(x => x.KpiName).HasMaxLength(20).IsRequired();
    }
}

internal sealed class AdCreativeConfiguration : IEntityTypeConfiguration<AdCreative>
{
    public void Configure(EntityTypeBuilder<AdCreative> b)
    {
        b.ToTable("ads_creatives");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Headlines).HasJsonList();
        b.Property(x => x.Descriptions).HasJsonList();
        b.Property(x => x.MediaAssetIds).HasJsonList();
        b.Property(x => x.PrimaryText).HasMaxLength(2000);
        b.Property(x => x.CallToAction).HasMaxLength(60);
        b.Property(x => x.FinalUrl).HasMaxLength(2000);
        b.Property(x => x.ReviewNote).HasMaxLength(2000);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdExperimentConfiguration : IEntityTypeConfiguration<AdExperiment>
{
    public void Configure(EntityTypeBuilder<AdExperiment> b)
    {
        b.ToTable("ads_experiments");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Hypothesis).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Metric).HasMaxLength(40).IsRequired();
        b.Property(x => x.Result).HasMaxLength(2000);
        b.Property(x => x.WinnerVariant).HasMaxLength(100);
        b.HasIndex(x => x.ClientAccountId);
        b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdExperimentVariantConfiguration : IEntityTypeConfiguration<AdExperimentVariant>
{
    public void Configure(EntityTypeBuilder<AdExperimentVariant> b)
    {
        b.ToTable("ads_experiment_variants");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
    }
}

internal sealed class AdUtmLinkConfiguration : IEntityTypeConfiguration<AdUtmLink>
{
    public void Configure(EntityTypeBuilder<AdUtmLink> b)
    {
        b.ToTable("ads_utm_links");
        b.Property(x => x.BaseUrl).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Source).HasMaxLength(100).IsRequired();
        b.Property(x => x.Medium).HasMaxLength(100).IsRequired();
        b.Property(x => x.Campaign).HasMaxLength(150).IsRequired();
        b.Property(x => x.Term).HasMaxLength(150);
        b.Property(x => x.Content).HasMaxLength(150);
        b.Property(x => x.TaggedUrl).HasMaxLength(3000).IsRequired();
        b.HasIndex(x => new { x.ClientAccountId, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
