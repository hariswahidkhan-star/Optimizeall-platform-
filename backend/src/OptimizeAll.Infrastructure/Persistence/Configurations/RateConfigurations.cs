using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Submissions;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Person-level pricing: rate cards, rate groups, assignments and per-submission rate snapshots (docs/REWARD_ENGINE.md).

internal sealed class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> b)
    {
        b.ToTable("rate_cards");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ArchiveReason).HasMaxLength(500);
        // Card list: standard cards filtered by status, ordered by name.
        b.HasIndex(x => new { x.Kind, x.Status, x.Name });
        // A person's custom (negotiated) rates.
        b.HasIndex(x => x.OwnerUserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.RateCardId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RateCardVersionConfiguration : IEntityTypeConfiguration<RateCardVersion>
{
    public void Configure(EntityTypeBuilder<RateCardVersion> b)
    {
        b.ToTable("rate_card_versions");
        // Final guard against two editors saving the same version number.
        b.HasIndex(x => new { x.RateCardId, x.Version }).IsUnique();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ChangeReason).HasMaxLength(500).IsRequired();
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.Property(x => x.MaxIncreasePercent).HasPrecision(19, 2);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.VersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RateCardLineConfiguration : IEntityTypeConfiguration<RateCardLine>
{
    public void Configure(EntityTypeBuilder<RateCardLine> b)
    {
        b.ToTable("rate_card_lines", t => t.HasCheckConstraint("ck_rate_card_lines_amount_nonnegative", "`Amount` >= 0"));
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Label).HasMaxLength(150);
    }
}

internal sealed class RateGroupConfiguration : IEntityTypeConfiguration<RateGroup>
{
    public void Configure(EntityTypeBuilder<RateGroup> b)
    {
        b.ToTable("rate_groups");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.AutoTiers).HasJsonList();
        b.Property(x => x.ArchiveReason).HasMaxLength(500);
        // Resolution loads the live automatic groups; the list shows live groups first.
        b.HasIndex(x => new { x.MembershipMode, x.ArchivedAt });
    }
}

internal sealed class RateGroupMemberConfiguration : IEntityTypeConfiguration<RateGroupMember>
{
    public void Configure(EntityTypeBuilder<RateGroupMember> b)
    {
        b.ToTable("rate_group_members");
        // One membership per person and group (bulk adds insert-if-absent against it).
        b.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
        // Pricing: the groups a person belongs to.
        b.HasIndex(x => x.UserId);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RateGroupMemberEventConfiguration : IEntityTypeConfiguration<RateGroupMemberEvent>
{
    public void Configure(EntityTypeBuilder<RateGroupMemberEvent> b)
    {
        b.ToTable("rate_group_member_events");
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500);
        // Group history (newest first) and a person's membership history.
        b.HasIndex(x => new { x.GroupId, x.At });
        b.HasIndex(x => new { x.UserId, x.At });
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RateAssignmentConfiguration : IEntityTypeConfiguration<RateAssignment>
{
    public void Configure(EntityTypeBuilder<RateAssignment> b)
    {
        b.ToTable("rate_assignments", t =>
        {
            t.HasCheckConstraint("ck_rate_assignments_target",
                "(`Target` = 'Person' AND `UserId` IS NOT NULL AND `GroupId` IS NULL) OR (`Target` = 'Group' AND `GroupId` IS NOT NULL AND `UserId` IS NULL)");
        });
        b.Property(x => x.Note).HasMaxLength(500).IsRequired();
        b.Property(x => x.EndReason).HasMaxLength(500);
        b.Ignore(x => x.EffectiveTo);
        // Pricing and the person's Rates tab: a person's assignments (all or one campaign).
        b.HasIndex(x => new { x.UserId, x.CampaignId });
        // Pricing: assignments of the groups a person is in; a group's assignments.
        b.HasIndex(x => new { x.GroupId, x.CampaignId });
        // Campaign editor panel: assignments scoped to the campaign.
        b.HasIndex(x => x.CampaignId);
        // Card detail: where a card is assigned; archiving a card.
        b.HasIndex(x => x.RateCardId);
        b.HasOne<RateCard>().WithMany().HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionRateConfiguration : IEntityTypeConfiguration<SubmissionRate>
{
    public void Configure(EntityTypeBuilder<SubmissionRate> b)
    {
        b.ToTable("submission_rates", t => t.HasCheckConstraint("ck_submission_rates_fx_positive", "`ExchangeRate` > 0"));
        b.HasIndex(x => x.SubmissionId).IsUnique();
        b.Property(x => x.CardName).HasMaxLength(120).IsRequired();
        b.Property(x => x.GroupName).HasMaxLength(120);
        b.Property(x => x.CardCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ExchangeRate).HasPrecision(18, 8);
        b.Property(x => x.LineLabel).HasMaxLength(150);
        b.Property(x => x.Explanation).HasMaxLength(2000).IsRequired();
        b.Ignore(x => x.SourceLabel);
        // Usage counts of a card / group / assignment (card detail, archive checks).
        b.HasIndex(x => x.RateCardId);
        b.HasIndex(x => x.RateGroupId);
        b.HasIndex(x => x.RateAssignmentId);
        b.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateCard>().WithMany().HasForeignKey(x => x.RateCardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateGroup>().WithMany().HasForeignKey(x => x.RateGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RateAssignment>().WithMany().HasForeignKey(x => x.RateAssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
