using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

/// <summary>
/// Editable copy, email template overrides and CMS page revisions. Portable across MySQL and SQLite; long text is
/// left unbounded.
/// </summary>
internal sealed class ContentCopyEntryConfiguration : IEntityTypeConfiguration<ContentCopyEntry>
{
    public void Configure(EntityTypeBuilder<ContentCopyEntry> b)
    {
        b.ToTable("content_copy_entries");
        b.Property(x => x.Key).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Value).IsRequired();
    }
}

internal sealed class EmailTemplateOverrideConfiguration : IEntityTypeConfiguration<EmailTemplateOverride>
{
    public void Configure(EntityTypeBuilder<EmailTemplateOverride> b)
    {
        b.ToTable("email_template_overrides");
        b.Property(x => x.Key).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.ActionLabel).HasMaxLength(80);
    }
}

internal sealed class SitePageRevisionConfiguration : IEntityTypeConfiguration<SitePageRevision>
{
    public void Configure(EntityTypeBuilder<SitePageRevision> b)
    {
        b.ToTable("website_page_revisions");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.BlocksJson).IsRequired();
        b.Property(x => x.SeoJson).IsRequired();
        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => new { x.PageId, x.Version }).IsUnique();
        b.HasOne<SitePage>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
