using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class SeoSiteConfiguration : IEntityTypeConfiguration<SeoSite>
{
    public void Configure(EntityTypeBuilder<SeoSite> b)
    {
        b.ToTable("seo_sites");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Domain).HasMaxLength(253).IsRequired();
        b.Property(x => x.Protocol).HasMaxLength(5).IsRequired();
        b.Property(x => x.SitemapUrl).HasMaxLength(1000);
        b.Property(x => x.TargetCountry).HasMaxLength(2).IsRequired();
        b.Property(x => x.TargetLanguage).HasMaxLength(10).IsRequired();
        b.Property(x => x.Competitors).HasJsonList();
        b.Ignore(x => x.BaseUrl);
        b.HasIndex(x => new { x.ClientAccountId, x.Domain }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoAuditConfiguration : IEntityTypeConfiguration<SeoAudit>
{
    public void Configure(EntityTypeBuilder<SeoAudit> b)
    {
        b.ToTable("seo_audits");
        b.Property(x => x.FailureMessage).HasMaxLength(2000);
        b.HasIndex(x => new { x.SiteId, x.QueuedAt });
        b.HasIndex(x => new { x.Status, x.QueuedAt });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoAuditPageConfiguration : IEntityTypeConfiguration<SeoAuditPage>
{
    public void Configure(EntityTypeBuilder<SeoAuditPage> b)
    {
        b.ToTable("seo_audit_pages");
        b.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150);
        b.Property(x => x.Title).HasMaxLength(1000);
        b.Property(x => x.MetaDescription).HasMaxLength(2000);
        b.Property(x => x.Canonical).HasMaxLength(2000);
        b.Property(x => x.RedirectChain).HasMaxLength(4000);
        b.Property(x => x.FetchError).HasMaxLength(500);
        b.HasIndex(x => x.AuditId);
        b.HasOne<SeoAudit>().WithMany().HasForeignKey(x => x.AuditId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoAuditIssueConfiguration : IEntityTypeConfiguration<SeoAuditIssue>
{
    public void Configure(EntityTypeBuilder<SeoAuditIssue> b)
    {
        b.ToTable("seo_audit_issues");
        b.Property(x => x.RuleKey).HasMaxLength(60).IsRequired();
        b.Property(x => x.AffectedUrls).HasJsonList();
        b.Property(x => x.Details);
        b.HasIndex(x => new { x.AuditId, x.RuleKey }).IsUnique();
        b.HasOne<SeoAudit>().WithMany().HasForeignKey(x => x.AuditId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoAuditRuleConfiguration : IEntityTypeConfiguration<SeoAuditRule>
{
    public void Configure(EntityTypeBuilder<SeoAuditRule> b)
    {
        b.ToTable("seo_audit_rules");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(60);
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60).IsRequired();
        b.Property(x => x.WhyItMatters).HasMaxLength(2000).IsRequired();
        b.Property(x => x.HowToFix).HasMaxLength(2000).IsRequired();
    }
}

internal sealed class SeoKeywordConfiguration : IEntityTypeConfiguration<SeoKeyword>
{
    public void Configure(EntityTypeBuilder<SeoKeyword> b)
    {
        b.ToTable("seo_keywords");
        b.Property(x => x.Keyword).HasMaxLength(200).IsRequired();
        b.Property(x => x.NormalizedKeyword).HasMaxLength(200).IsRequired();
        b.Property(x => x.TargetUrl).HasMaxLength(1000);
        b.Property(x => x.Tags).HasJsonList();
        b.HasIndex(x => new { x.SiteId, x.NormalizedKeyword }).IsUnique();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoRankSnapshotConfiguration : IEntityTypeConfiguration<SeoRankSnapshot>
{
    public void Configure(EntityTypeBuilder<SeoRankSnapshot> b)
    {
        b.ToTable("seo_rank_snapshots");
        b.Property(x => x.Domain).HasMaxLength(253).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.SerpFeatures).HasJsonList();
        b.HasIndex(x => new { x.KeywordId, x.Date, x.Domain }).IsUnique();
        b.HasIndex(x => new { x.SiteId, x.Date });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoKeyword>().WithMany().HasForeignKey(x => x.KeywordId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoSearchPerformanceConfiguration : IEntityTypeConfiguration<SeoSearchPerformance>
{
    public void Configure(EntityTypeBuilder<SeoSearchPerformance> b)
    {
        b.ToTable("seo_search_performance");
        b.Property(x => x.Query).HasMaxLength(500).IsRequired();
        b.Property(x => x.Page).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RowHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.SiteId, x.Date, x.RowHash }).IsUnique();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoBacklinkConfiguration : IEntityTypeConfiguration<SeoBacklink>
{
    public void Configure(EntityTypeBuilder<SeoBacklink> b)
    {
        b.ToTable("seo_backlinks");
        b.Property(x => x.SourceUrl).HasMaxLength(2000).IsRequired();
        b.Property(x => x.TargetUrl).HasMaxLength(2000).IsRequired();
        b.Property(x => x.LinkHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.AnchorText).HasMaxLength(500);
        b.Property(x => x.Rel).HasMaxLength(100);
        b.Property(x => x.CheckMessage).HasMaxLength(500);
        b.HasIndex(x => new { x.SiteId, x.LinkHash }).IsUnique();
        b.HasIndex(x => new { x.Status, x.LastCheckedAt });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoOutreachProspectConfiguration : IEntityTypeConfiguration<SeoOutreachProspect>
{
    public void Configure(EntityTypeBuilder<SeoOutreachProspect> b)
    {
        b.ToTable("seo_outreach_prospects");
        b.Property(x => x.ProspectUrl).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.ContactEmail).HasMaxLength(254);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasIndex(x => x.SiteId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoLocalProfileConfiguration : IEntityTypeConfiguration<SeoLocalProfile>
{
    public void Configure(EntityTypeBuilder<SeoLocalProfile> b)
    {
        b.ToTable("seo_local_profiles");
        b.Property(x => x.BusinessName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Website).HasMaxLength(500);
        b.Property(x => x.CompletedChecklist).HasJsonList();
        b.HasIndex(x => x.SiteId).IsUnique();
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoCitationSourceConfiguration : IEntityTypeConfiguration<SeoCitationSource>
{
    public void Configure(EntityTypeBuilder<SeoCitationSource> b)
    {
        b.ToTable("seo_citation_sources");
        b.Property(x => x.Key).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Url).HasMaxLength(500).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60).IsRequired();
        b.Property(x => x.Countries).HasJsonList();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class SeoCitationConfiguration : IEntityTypeConfiguration<SeoCitation>
{
    public void Configure(EntityTypeBuilder<SeoCitation> b)
    {
        b.ToTable("seo_citations");
        b.Property(x => x.ListingUrl).HasMaxLength(1000);
        b.Property(x => x.ListedName).HasMaxLength(200);
        b.Property(x => x.ListedAddress).HasMaxLength(500);
        b.Property(x => x.ListedPhone).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.SiteId, x.SourceId }).IsUnique();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SeoCitationSource>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SeoReviewConfiguration : IEntityTypeConfiguration<SeoReview>
{
    public void Configure(EntityTypeBuilder<SeoReview> b)
    {
        b.ToTable("seo_reviews");
        b.Property(x => x.Platform).HasMaxLength(60).IsRequired();
        b.Property(x => x.AuthorName).HasMaxLength(150);
        b.Property(x => x.Text).HasMaxLength(4000);
        b.Property(x => x.ResponseText).HasMaxLength(4000);
        b.HasIndex(x => new { x.SiteId, x.ReviewedAt });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SeoContentBriefConfiguration : IEntityTypeConfiguration<SeoContentBrief>
{
    public void Configure(EntityTypeBuilder<SeoContentBrief> b)
    {
        b.ToTable("seo_content_briefs");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.TargetKeyword).HasMaxLength(200).IsRequired();
        b.Property(x => x.RelatedKeywords).HasJsonList();
        b.Property(x => x.Questions).HasJsonList();
        b.Property(x => x.Outline).HasJsonList();
        b.Property(x => x.CompetitorUrls).HasJsonList();
        b.Property(x => x.Notes).HasMaxLength(8000);
        b.HasIndex(x => x.SiteId);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<SeoSite>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);
    }
}
