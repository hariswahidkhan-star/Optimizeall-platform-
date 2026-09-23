using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal static class UtmTouchMapping
{
    public static void MapTouch<T>(this EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T, UtmTouch?>> nav, string prefix)
        where T : class
    {
        b.OwnsOne(nav, o =>
        {
            o.Property(p => p.Source).HasMaxLength(100).HasColumnName(prefix + "Source");
            o.Property(p => p.Medium).HasMaxLength(100).HasColumnName(prefix + "Medium");
            o.Property(p => p.Campaign).HasMaxLength(150).HasColumnName(prefix + "Campaign");
            o.Property(p => p.At).HasColumnName(prefix + "At");
            o.Ignore(p => p.IsEmpty);
        });
        b.Navigation(nav).IsRequired();
    }
}

internal sealed class CrmCompanyConfiguration : IEntityTypeConfiguration<CrmCompany>
{
    public void Configure(EntityTypeBuilder<CrmCompany> b)
    {
        b.ToTable("crm_companies");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Domain).HasMaxLength(253);
        b.HasIndex(x => x.Domain).IsUnique();
        b.Property(x => x.Industry).HasMaxLength(100);
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Tags).HasJsonList();
        b.Property(x => x.TagIndex).HasMaxLength(1000).IsRequired();
        b.Property(x => x.CustomFieldsJson).IsRequired(); // long text, validated by CustomFields.Normalize
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.OwnerUserId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CrmContactConfiguration : IEntityTypeConfiguration<CrmContact>
{
    public void Configure(EntityTypeBuilder<CrmContact> b)
    {
        b.ToTable("crm_contacts");
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.NormalizedEmail).HasMaxLength(254);
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.JobTitle).HasMaxLength(120);
        b.Property(x => x.Source).HasMaxLength(100);
        b.Property(x => x.BudgetRange).HasMaxLength(60);
        b.Property(x => x.Tags).HasJsonList();
        b.Property(x => x.TagIndex).HasMaxLength(1000).IsRequired();
        b.Ignore(x => x.DisplayName);
        b.MapTouch(x => x.FirstTouch, "FirstTouch");
        b.MapTouch(x => x.LastTouch, "LastTouch");
        b.HasIndex(x => x.CompanyId);
        b.HasIndex(x => x.OwnerUserId);
        b.HasIndex(x => x.LifecycleStage);
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<CrmCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PipelineStageConfiguration : IEntityTypeConfiguration<PipelineStage>
{
    public void Configure(EntityTypeBuilder<PipelineStage> b)
    {
        b.ToTable("crm_pipeline_stages");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.Position);
    }
}

internal sealed class CrmDealConfiguration : IEntityTypeConfiguration<CrmDeal>
{
    public void Configure(EntityTypeBuilder<CrmDeal> b)
    {
        b.ToTable("crm_deals");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ServiceSlugs).HasJsonList();
        b.Property(x => x.SourceDetail).HasMaxLength(100);
        b.Property(x => x.BudgetRange).HasMaxLength(60);
        b.Property(x => x.LostReason).HasMaxLength(500);
        b.MapTouch(x => x.FirstTouch, "FirstTouch");
        b.MapTouch(x => x.LastTouch, "LastTouch");
        b.HasIndex(x => new { x.Status, x.StageId });
        b.HasIndex(x => x.StageId);
        b.HasIndex(x => x.OwnerUserId);
        b.HasIndex(x => x.CompanyId);
        b.HasIndex(x => x.PrimaryContactId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasIndex(x => x.ClosedAt);
        b.HasOne<PipelineStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CrmCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.PrimaryContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CrmDealContactConfiguration : IEntityTypeConfiguration<CrmDealContact>
{
    public void Configure(EntityTypeBuilder<CrmDealContact> b)
    {
        b.ToTable("crm_deal_contacts");
        b.HasKey(x => new { x.DealId, x.ContactId });
        b.Property(x => x.Role).HasMaxLength(80);
        b.HasIndex(x => x.ContactId);
        b.HasOne<CrmDeal>().WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CrmActivityConfiguration : IEntityTypeConfiguration<CrmActivity>
{
    public void Configure(EntityTypeBuilder<CrmActivity> b)
    {
        b.ToTable("crm_activities");
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body); // long text (validated to 10,000 characters by the API)
        b.HasIndex(x => new { x.DealId, x.CreatedAt });
        b.HasIndex(x => new { x.ContactId, x.CreatedAt });
        b.HasIndex(x => new { x.CompanyId, x.CreatedAt });
        b.HasIndex(x => new { x.AssigneeUserId, x.CompletedAt, x.DueAt });
        b.HasIndex(x => new { x.CompletedAt, x.DueAt, x.OverdueNotifiedAt });
        b.HasIndex(x => new { x.RemindAt, x.ReminderSentAt });
        b.HasOne<CrmDeal>().WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CrmCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class LeadScoringRuleConfiguration : IEntityTypeConfiguration<LeadScoringRule>
{
    public void Configure(EntityTypeBuilder<LeadScoringRule> b)
    {
        b.ToTable("crm_scoring_rules");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Field).HasMaxLength(60).IsRequired();
        b.Property(x => x.MatchValue).HasMaxLength(500);
    }
}

internal sealed class CrmEngagementConfiguration : IEntityTypeConfiguration<CrmEngagement>
{
    public void Configure(EntityTypeBuilder<CrmEngagement> b)
    {
        b.ToTable("crm_engagements");
        b.Property(x => x.Type).HasMaxLength(60).IsRequired();
        b.Property(x => x.SourceKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.SourceKey).IsUnique();
        b.HasIndex(x => new { x.ContactId, x.Type });
        b.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CrmInboundEventConfiguration : IEntityTypeConfiguration<CrmInboundEvent>
{
    public void Configure(EntityTypeBuilder<CrmInboundEvent> b)
    {
        b.ToTable("crm_inbound_events");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(120);
    }
}

internal sealed class CrmAssignmentCursorConfiguration : IEntityTypeConfiguration<CrmAssignmentCursor>
{
    public void Configure(EntityTypeBuilder<CrmAssignmentCursor> b)
    {
        b.ToTable("crm_assignment_cursors");
        b.Property(x => x.Pool).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Pool).IsUnique();
    }
}

internal sealed class CrmSavedViewConfiguration : IEntityTypeConfiguration<CrmSavedView>
{
    public void Configure(EntityTypeBuilder<CrmSavedView> b)
    {
        b.ToTable("crm_saved_views");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Entity).HasMaxLength(20).IsRequired();
        b.Property(x => x.FiltersJson).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.Entity, x.OwnerUserId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> b)
    {
        b.ToTable("proposals");
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.RecipientName).HasMaxLength(150);
        b.Property(x => x.RecipientEmail).HasMaxLength(254);
        b.Property(x => x.ShareTokenHash).HasMaxLength(64);
        b.HasIndex(x => x.ShareTokenHash).IsUnique();
        b.Property(x => x.ShareTokenProtected).HasMaxLength(1000);
        b.Property(x => x.SignerName).HasMaxLength(150);
        b.Property(x => x.SignerTitle).HasMaxLength(150);
        b.Property(x => x.SignerEmail).HasMaxLength(254);
        b.Property(x => x.SignerIpHash).HasMaxLength(64);
        b.Property(x => x.SignerUserAgent).HasMaxLength(500);
        b.Property(x => x.DeclineReason).HasMaxLength(1000);
        b.HasIndex(x => x.DealId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.ProposalId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CrmDeal>().WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<CrmCompany>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<CrmContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProposalVersionConfiguration : IEntityTypeConfiguration<ProposalVersion>
{
    public void Configure(EntityTypeBuilder<ProposalVersion> b)
    {
        b.ToTable("proposal_versions");
        b.HasIndex(x => new { x.ProposalId, x.VersionNumber }).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        foreach (var section in new[] { nameof(ProposalVersion.ExecutiveSummary), nameof(ProposalVersion.Goals), nameof(ProposalVersion.Scope),
                     nameof(ProposalVersion.Deliverables), nameof(ProposalVersion.Timeline), nameof(ProposalVersion.Terms) })
            b.Property<string?>(section); // long text (validated to 20,000 characters by the API)
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ProposalVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProposalLineConfiguration : IEntityTypeConfiguration<ProposalLine>
{
    public void Configure(EntityTypeBuilder<ProposalLine> b)
    {
        b.ToTable("proposal_lines");
        b.MapPricedLine();
        b.Property(x => x.PackageSlug).HasMaxLength(100);
        b.HasIndex(x => new { x.ProposalVersionId, x.Position });
    }
}
