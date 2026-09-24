using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> b)
    {
        b.ToTable("submissions");
        b.Property(x => x.PostUrl).HasMaxLength(1000).IsRequired();
        // Holds the canonical post key (PlatformUrlRules.Parse), e.g. "instagram:Cabc123". Binary collation: post ids are
        // case-sensitive (Instagram shortcodes), so "instagram:AbC" and "instagram:abc" are different posts.
        b.Property(x => x.NormalizedPostUrl).HasMaxLength(768).IsRequired().UseCollation("utf8mb4_bin");

        // A public post can be claimed exactly once across the whole platform.
        b.HasIndex(x => x.NormalizedPostUrl).IsUnique();
        b.Property(x => x.CaptionText).HasColumnType("text");
        b.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => x.ContentHash);
        b.Property(x => x.ScreenshotSha256).HasMaxLength(64).IsFixedLength();
        b.HasIndex(x => x.ScreenshotSha256);
        b.Property(x => x.DecisionReason).HasMaxLength(1000);
        b.Property(x => x.RewardCurrency).HasMaxLength(3).IsFixedLength().IsRequired();

        b.HasIndex(x => new { x.Status, x.SubmittedAt });
        b.HasIndex(x => new { x.UserId, x.CampaignId });
        b.HasIndex(x => new { x.CampaignId, x.Status });
        b.HasIndex(x => new { x.LiveCheckStatus, x.LiveCheckDueAt });
        b.HasIndex(x => x.AssignedReviewerId);

        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SocialAccount>().WithMany().HasForeignKey(x => x.SocialAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ScreenshotFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RewardRuleSet>().WithMany().HasForeignKey(x => x.RewardRuleSetId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Flags).WithOne().HasForeignKey(f => f.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(e => e.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsOpenForReview);
        b.Ignore(x => x.CanWithdraw);
    }
}

internal sealed class SubmissionFlagConfiguration : IEntityTypeConfiguration<SubmissionFlag>
{
    public void Configure(EntityTypeBuilder<SubmissionFlag> b)
    {
        b.ToTable("submission_flags");
        b.Property(x => x.Detail).HasMaxLength(500).IsRequired();
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.HasIndex(x => new { x.SubmissionId, x.Type });
    }
}

internal sealed class SubmissionEventConfiguration : IEntityTypeConfiguration<SubmissionEvent>
{
    public void Configure(EntityTypeBuilder<SubmissionEvent> b)
    {
        b.ToTable("submission_events");
        b.Property(x => x.Action).HasMaxLength(60).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.SubmissionId, x.CreatedAt });
        // Reviewer stats: decisions per reviewer today.
        b.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
    }
}

internal sealed class AppealConfiguration : IEntityTypeConfiguration<Appeal>
{
    public void Configure(EntityTypeBuilder<Appeal> b)
    {
        b.ToTable("appeals");
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ResolutionNote).HasMaxLength(2000);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => x.SubmissionId);
        b.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
