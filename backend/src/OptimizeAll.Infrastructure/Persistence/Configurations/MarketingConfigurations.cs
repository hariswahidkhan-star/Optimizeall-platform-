using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class InvitationLinkConfiguration : IEntityTypeConfiguration<InvitationLink>
{
    public void Configure(EntityTypeBuilder<InvitationLink> b)
    {
        b.ToTable("invitation_links");
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.UtmSource).HasMaxLength(100);
        b.Property(x => x.UtmMedium).HasMaxLength(100);
        b.Property(x => x.UtmCampaign).HasMaxLength(100);
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> b)
    {
        b.ToTable("referrals");
        // A participant can only ever be referred once.
        b.HasIndex(x => x.ReferredUserId).IsUnique();
        b.HasIndex(x => new { x.ReferrerUserId, x.Status });
        b.Property(x => x.CodeUsed).HasMaxLength(32).IsRequired();
        b.Property(x => x.RegistrationIpHash).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.DeviceHash).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.FraudSignals).HasMaxLength(500);
        b.Property(x => x.RejectionReason).HasMaxLength(1000);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReferrerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReferredUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_referral_not_self", "`ReferrerUserId` <> `ReferredUserId`"));
    }
}

internal sealed class TrackingLinkConfiguration : IEntityTypeConfiguration<TrackingLink>
{
    public void Configure(EntityTypeBuilder<TrackingLink> b)
    {
        b.ToTable("tracking_links");
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => new { x.CampaignId, x.UserId }).IsUnique();
        b.Property(x => x.DestinationUrl).HasMaxLength(2000).IsRequired();
        b.Property(x => x.UtmSource).HasMaxLength(100).IsRequired();
        b.Property(x => x.UtmMedium).HasMaxLength(100).IsRequired();
        b.Property(x => x.UtmCampaign).HasMaxLength(100).IsRequired();
        b.Property(x => x.UtmContent).HasMaxLength(100);
        b.Property(x => x.UtmTerm).HasMaxLength(100);
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrackingClickConfiguration : IEntityTypeConfiguration<TrackingClick>
{
    public void Configure(EntityTypeBuilder<TrackingClick> b)
    {
        b.ToTable("tracking_clicks");
        b.Property(x => x.VisitorHash).HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Referrer).HasMaxLength(500);
        b.HasIndex(x => new { x.TrackingLinkId, x.ClickedAt });
        // Tracking/analytics summaries over a date range across all links; tracking-event retention.
        b.HasIndex(x => x.ClickedAt);
        b.HasIndex(x => new { x.TrackingLinkId, x.VisitorHash });
        b.HasOne<TrackingLink>().WithMany().HasForeignKey(x => x.TrackingLinkId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrackingConversionConfiguration : IEntityTypeConfiguration<TrackingConversion>
{
    public void Configure(EntityTypeBuilder<TrackingConversion> b)
    {
        b.ToTable("tracking_conversions");
        b.Property(x => x.ExternalReference).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.TrackingLinkId, x.ExternalReference }).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.HasOne<TrackingLink>().WithMany().HasForeignKey(x => x.TrackingLinkId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    public void Configure(EntityTypeBuilder<Experiment> b)
    {
        b.ToTable("experiments");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Hypothesis).HasMaxLength(1000);
        b.HasIndex(x => new { x.CampaignId, x.Status });
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.ExperimentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExperimentVariantConfiguration : IEntityTypeConfiguration<ExperimentVariant>
{
    public void Configure(EntityTypeBuilder<ExperimentVariant> b)
    {
        b.ToTable("experiment_variants");
        b.Property(x => x.Key).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.ExperimentId, x.Key }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Instructions).HasColumnType("text");
        b.Property(x => x.LandingHeadline).HasMaxLength(200);
        b.Property(x => x.LandingBody).HasColumnType("text");
    }
}

internal sealed class ExperimentAssignmentConfiguration : IEntityTypeConfiguration<ExperimentAssignment>
{
    public void Configure(EntityTypeBuilder<ExperimentAssignment> b)
    {
        b.ToTable("experiment_assignments");
        b.Property(x => x.SubjectKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ExperimentId, x.SubjectKey }).IsUnique();
        b.HasIndex(x => x.VariantId);
        b.HasOne<Experiment>().WithMany().HasForeignKey(x => x.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ExperimentVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> b)
    {
        b.ToTable("achievements");
        b.Property(x => x.Key).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.Icon).HasMaxLength(50);
    }
}

internal sealed class UserAchievementConfiguration : IEntityTypeConfiguration<UserAchievement>
{
    public void Configure(EntityTypeBuilder<UserAchievement> b)
    {
        b.ToTable("user_achievements");
        b.HasKey(x => new { x.UserId, x.AchievementId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Achievement>().WithMany().HasForeignKey(x => x.AchievementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RetentionMessageLogConfiguration : IEntityTypeConfiguration<RetentionMessageLog>
{
    public void Configure(EntityTypeBuilder<RetentionMessageLog> b)
    {
        b.ToTable("retention_message_logs");
        b.Property(x => x.Kind).HasMaxLength(60).IsRequired();
        b.Property(x => x.DedupKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.UserId, x.Kind, x.DedupKey }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
