using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

/// <summary>
/// Website &amp; CMS tables (public agency site). Portable across MySQL and SQLite: no provider column types, lists as
/// JSON via <c>HasJsonList</c>, long text either bounded with <c>HasMaxLength</c> or left unbounded.
/// </summary>
internal static class WebsiteMapping
{
    public static void Seo<T>(OwnedNavigationBuilder<T, SeoMeta> seo) where T : class
    {
        seo.Property(s => s.Title).HasColumnName("SeoTitle").HasMaxLength(70);
        seo.Property(s => s.Description).HasColumnName("SeoDescription").HasMaxLength(200);
        seo.Property(s => s.OgImageUrl).HasColumnName("SeoOgImageUrl").HasMaxLength(500);
        seo.Property(s => s.CanonicalUrl).HasColumnName("SeoCanonicalUrl").HasMaxLength(500);
        seo.Property(s => s.NoIndex).HasColumnName("SeoNoIndex");
    }
}

internal sealed class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> b)
    {
        b.ToTable("website_service_categories");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Icon).HasMaxLength(40);
        b.HasIndex(x => new { x.IsPublished, x.SortOrder });
    }
}

internal sealed class AgencyServiceConfiguration : IEntityTypeConfiguration<AgencyService>
{
    public void Configure(EntityTypeBuilder<AgencyService> b)
    {
        b.ToTable("website_services");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Tagline).HasMaxLength(200).IsRequired();
        b.Property(x => x.HeroTitle).HasMaxLength(150);
        b.Property(x => x.HeroBody).HasMaxLength(600);
        b.Property(x => x.OverviewMarkdown);
        b.Property(x => x.ProblemsSolved).HasJsonList();
        b.Property(x => x.Deliverables).HasJsonList();
        b.Property(x => x.ProcessSteps).HasJsonList();
        b.Property(x => x.Tools).HasJsonList();
        b.Property(x => x.Kpis).HasJsonList();
        b.Property(x => x.Faqs).HasJsonList();
        b.Property(x => x.RelatedServiceIds).HasJsonList();
        b.Property(x => x.Icon).HasMaxLength(40);
        b.Property(x => x.HeroImageUrl).HasMaxLength(500);
        b.Property(x => x.CtaLabel).HasMaxLength(60);
        b.Property(x => x.CtaUrl).HasMaxLength(500);
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.CategoryId, x.SortOrder });
        b.HasIndex(x => x.IsPublished);
        b.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Packages).WithOne().HasForeignKey(p => p.ServiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServicePackageConfiguration : IEntityTypeConfiguration<ServicePackage>
{
    public void Configure(EntityTypeBuilder<ServicePackage> b)
    {
        b.ToTable("website_service_packages");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.Features).HasJsonList();
        b.HasIndex(x => new { x.ServiceId, x.SortOrder });
    }
}

internal sealed class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    public void Configure(EntityTypeBuilder<Industry> b)
    {
        b.ToTable("website_industries");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        b.Property(x => x.BodyMarkdown);
        b.Property(x => x.Challenges).HasJsonList();
        b.Property(x => x.ServiceIds).HasJsonList();
        b.Property(x => x.Icon).HasMaxLength(40);
        b.Property(x => x.HeroImageUrl).HasMaxLength(500);
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.IsPublished, x.SortOrder });
    }
}

internal sealed class CaseStudyConfiguration : IEntityTypeConfiguration<CaseStudy>
{
    public void Configure(EntityTypeBuilder<CaseStudy> b)
    {
        b.ToTable("website_case_studies");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.ClientName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        b.Property(x => x.ServiceIds).HasJsonList();
        b.Property(x => x.ChallengeMarkdown);
        b.Property(x => x.StrategyMarkdown);
        b.Property(x => x.ExecutionMarkdown);
        b.Property(x => x.Metrics).HasJsonList();
        b.Property(x => x.TestimonialQuote).HasMaxLength(1000);
        b.Property(x => x.TestimonialAuthor).HasMaxLength(120);
        b.Property(x => x.TestimonialRole).HasMaxLength(120);
        b.Property(x => x.CoverImageUrl).HasMaxLength(500);
        b.Property(x => x.GalleryImageUrls).HasJsonList();
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.IsPublished, x.SortOrder });
        b.HasIndex(x => x.IndustryId);
        b.HasOne<Industry>().WithMany().HasForeignKey(x => x.IndustryId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TestimonialConfiguration : IEntityTypeConfiguration<Testimonial>
{
    public void Configure(EntityTypeBuilder<Testimonial> b)
    {
        b.ToTable("website_testimonials");
        b.Property(x => x.Quote).HasMaxLength(1000).IsRequired();
        b.Property(x => x.AuthorName).HasMaxLength(120).IsRequired();
        b.Property(x => x.AuthorRole).HasMaxLength(120);
        b.Property(x => x.Company).HasMaxLength(120);
        b.Property(x => x.AvatarUrl).HasMaxLength(500);
        b.HasIndex(x => new { x.IsPublished, x.SortOrder });
        b.HasOne<AgencyService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> b)
    {
        b.ToTable("website_team_members");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Role).HasMaxLength(120).IsRequired();
        b.Property(x => x.Bio).HasMaxLength(2000);
        b.Property(x => x.PhotoUrl).HasMaxLength(500);
        b.Property(x => x.Expertise).HasJsonList();
        b.Property(x => x.SocialLinks).HasJsonList();
        b.HasIndex(x => new { x.IsPublished, x.SortOrder });
    }
}

internal sealed class SitePageConfiguration : IEntityTypeConfiguration<SitePage>
{
    public void Configure(EntityTypeBuilder<SitePage> b)
    {
        b.ToTable("website_pages");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.BlocksJson).IsRequired();
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.IsPublished, x.Kind });
    }
}

internal sealed class SiteSettingsDocumentConfiguration : IEntityTypeConfiguration<SiteSettingsDocument>
{
    public void Configure(EntityTypeBuilder<SiteSettingsDocument> b)
    {
        b.ToTable("website_settings");
        b.Property(x => x.Key).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Json).IsRequired();
    }
}

internal sealed class BlogCategoryConfiguration : IEntityTypeConfiguration<BlogCategory>
{
    public void Configure(EntityTypeBuilder<BlogCategory> b)
    {
        b.ToTable("website_blog_categories");
        b.Property(x => x.Slug).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
    }
}

internal sealed class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> b)
    {
        b.ToTable("website_blog_posts");
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(180).IsRequired();
        b.Property(x => x.Excerpt).HasMaxLength(500).IsRequired();
        b.Property(x => x.BodyMarkdown).IsRequired();
        b.Property(x => x.CoverImageUrl).HasMaxLength(500);
        b.Property(x => x.CoverImageAlt).HasMaxLength(200);
        b.Property(x => x.CategoryIds).HasJsonList();
        b.Property(x => x.Tags).HasJsonList();
        b.Property(x => x.RelatedPostIds).HasJsonList();
        b.OwnsOne(x => x.Seo, WebsiteMapping.Seo);
        b.Navigation(x => x.Seo).IsRequired();
        b.HasIndex(x => new { x.Status, x.PublishedAt });
        b.HasIndex(x => new { x.Status, x.PublishAt });
        b.HasOne<TeamMember>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class JobOpeningConfiguration : IEntityTypeConfiguration<JobOpening>
{
    public void Configure(EntityTypeBuilder<JobOpening> b)
    {
        b.ToTable("website_job_openings");
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Department).HasMaxLength(80).IsRequired();
        b.Property(x => x.Location).HasMaxLength(120).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        b.Property(x => x.DescriptionMarkdown).IsRequired();
        b.Property(x => x.Requirements).HasJsonList();
        b.Property(x => x.Benefits).HasJsonList();
        b.Property(x => x.SalaryCurrency).HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => x.Status);
    }
}

internal sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("website_job_applications");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.PortfolioUrl).HasMaxLength(500);
        b.Property(x => x.CoverLetter);
        b.Property(x => x.ConsentVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.HasIndex(x => new { x.JobOpeningId, x.Stage });
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<JobOpening>().WithMany().HasForeignKey(x => x.JobOpeningId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CareerCvFile>().WithMany().HasForeignKey(x => x.CvFileId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Notes).WithOne().HasForeignKey(n => n.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class JobApplicationNoteConfiguration : IEntityTypeConfiguration<JobApplicationNote>
{
    public void Configure(EntityTypeBuilder<JobApplicationNote> b)
    {
        b.ToTable("website_job_application_notes");
        b.Property(x => x.Body).IsRequired();
        b.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CareerCvFileConfiguration : IEntityTypeConfiguration<CareerCvFile>
{
    public void Configure(EntityTypeBuilder<CareerCvFile> b)
    {
        b.ToTable("website_cv_files");
        b.Property(x => x.FileName).HasMaxLength(120).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Content).IsRequired();
    }
}

internal sealed class WebsiteInquiryConfiguration : IEntityTypeConfiguration<WebsiteInquiry>
{
    public void Configure(EntityTypeBuilder<WebsiteInquiry> b)
    {
        b.ToTable("website_inquiries");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Company).HasMaxLength(150);
        b.Property(x => x.Website).HasMaxLength(500);
        b.Property(x => x.Message); // long text (unbounded keeps MySQL rows under the 64 KB limit)
        b.Property(x => x.ServiceSlugs).HasJsonList();
        b.Property(x => x.PackageIds).HasJsonList();
        b.Property(x => x.BudgetRange).HasMaxLength(60);
        b.Property(x => x.Timeline).HasMaxLength(60);
        b.Property(x => x.PayloadJson).IsRequired();
        b.Property(x => x.UtmSource).HasMaxLength(150);
        b.Property(x => x.UtmMedium).HasMaxLength(150);
        b.Property(x => x.UtmCampaign).HasMaxLength(150);
        b.Property(x => x.UtmTerm).HasMaxLength(150);
        b.Property(x => x.UtmContent).HasMaxLength(150);
        b.Property(x => x.Referrer).HasMaxLength(500);
        b.Property(x => x.LandingPath).HasMaxLength(500);
        b.Property(x => x.ConsentVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.StaffNotes);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.Type, x.CreatedAt });
        b.HasIndex(x => x.Email);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ConsultationSettingsConfiguration : IEntityTypeConfiguration<ConsultationSettings>
{
    public void Configure(EntityTypeBuilder<ConsultationSettings> b)
    {
        b.ToTable("website_consultation_settings");
        b.Property(x => x.Key).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.WeeklyAvailability).HasJsonList();
    }
}

internal sealed class ConsultationBlackoutConfiguration : IEntityTypeConfiguration<ConsultationBlackout>
{
    public void Configure(EntityTypeBuilder<ConsultationBlackout> b)
    {
        b.ToTable("website_consultation_blackouts");
        b.HasIndex(x => x.Date).IsUnique();
        b.Property(x => x.Reason).HasMaxLength(200);
    }
}

internal sealed class ConsultationBookingConfiguration : IEntityTypeConfiguration<ConsultationBooking>
{
    public void Configure(EntityTypeBuilder<ConsultationBooking> b)
    {
        b.ToTable("website_consultation_bookings");
        // Unique while the booking holds the slot (null after cancellation): two bookings of one slot cannot both commit.
        b.Property(x => x.SlotKey).HasMaxLength(20);
        b.HasIndex(x => x.SlotKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.SlotStart });
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Company).HasMaxLength(150);
        b.Property(x => x.Website).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.ServiceSlugs).HasJsonList();
        b.Property(x => x.VisitorTimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.HasOne<WebsiteInquiry>().WithMany().HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class NewsletterSubscriberConfiguration : IEntityTypeConfiguration<NewsletterSubscriber>
{
    public void Configure(EntityTypeBuilder<NewsletterSubscriber> b)
    {
        b.ToTable("website_newsletter_subscribers");
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Property(x => x.ConfirmTokenHash).HasMaxLength(64);
        b.HasIndex(x => x.ConfirmTokenHash);
        b.Property(x => x.UnsubscribeTokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.UnsubscribeTokenHash).IsUnique();
        b.Property(x => x.ConsentVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.Source).HasMaxLength(60);
        b.Property(x => x.UtmSource).HasMaxLength(150);
        b.Property(x => x.UtmMedium).HasMaxLength(150);
        b.Property(x => x.UtmCampaign).HasMaxLength(150);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
