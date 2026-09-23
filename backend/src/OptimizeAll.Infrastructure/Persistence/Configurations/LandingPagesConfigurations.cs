using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.LandingPages;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class LandingPageConfiguration : IEntityTypeConfiguration<LandingPage>
{
    public void Configure(EntityTypeBuilder<LandingPage> b)
    {
        b.ToTable("landing_pages");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        b.Property(x => x.MetaTitle).HasMaxLength(150);
        b.Property(x => x.MetaDescription).HasMaxLength(320);
        b.Property(x => x.OgImageUrl).HasMaxLength(1000);
        b.Property(x => x.TemplateKey).HasMaxLength(60);
        b.Property(x => x.VariantsJson).IsRequired();
        b.HasIndex(x => new { x.ClientAccountId, x.Slug }).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LandingPageVersionConfiguration : IEntityTypeConfiguration<LandingPageVersion>
{
    public void Configure(EntityTypeBuilder<LandingPageVersion> b)
    {
        b.ToTable("landing_page_versions");
        b.HasKey(x => x.Id);
        b.Property(x => x.SnapshotJson).IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.HasIndex(x => new { x.PageId, x.Version }).IsUnique();
        b.HasOne<LandingPage>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LandingPageAssignmentConfiguration : IEntityTypeConfiguration<LandingPageAssignment>
{
    public void Configure(EntityTypeBuilder<LandingPageAssignment> b)
    {
        b.ToTable("landing_page_assignments");
        b.Property(x => x.SubjectKey).HasMaxLength(120).IsRequired();
        b.Property(x => x.VariantKey).HasMaxLength(1).IsRequired();
        b.HasIndex(x => new { x.ExperimentId, x.SubjectKey }).IsUnique();
        b.HasIndex(x => x.PageId);
        b.HasOne<LandingPage>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LandingPageViewConfiguration : IEntityTypeConfiguration<LandingPageView>
{
    public void Configure(EntityTypeBuilder<LandingPageView> b)
    {
        b.ToTable("landing_page_views");
        b.Property(x => x.VariantKey).HasMaxLength(1).IsRequired();
        b.Property(x => x.VisitorHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ReferrerHost).HasMaxLength(253);
        b.Property(x => x.UtmSource).HasMaxLength(100);
        b.HasIndex(x => new { x.PageId, x.ViewedAt });
        b.HasIndex(x => new { x.PageId, x.VisitorHash });
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<LandingPage>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LandingPageTemplateConfiguration : IEntityTypeConfiguration<LandingPageTemplate>
{
    public void Configure(EntityTypeBuilder<LandingPageTemplate> b)
    {
        b.ToTable("landing_page_templates");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(60);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.MetaTitle).HasMaxLength(150).IsRequired();
        b.Property(x => x.MetaDescription).HasMaxLength(320).IsRequired();
        b.Property(x => x.BlocksJson).IsRequired();
        b.Property(x => x.FormTemplateKey).HasMaxLength(60);
    }
}

internal sealed class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> b)
    {
        b.ToTable("forms");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.SchemaJson).IsRequired();
        b.Property(x => x.SubmitLabel).HasMaxLength(60).IsRequired();
        b.Property(x => x.SuccessMessage).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RedirectUrl).HasMaxLength(1000);
        b.Property(x => x.NotifyUserIds).HasJsonList();
        b.Property(x => x.AutoresponderSubject).HasMaxLength(200);
        b.Property(x => x.AutoresponderBody).HasMaxLength(5000);
        b.Property(x => x.AllowedOrigins).HasJsonList();
        b.Property(x => x.ConsentText).HasMaxLength(2000);
        b.Property(x => x.TemplateKey).HasMaxLength(60);
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormConsentVersionConfiguration : IEntityTypeConfiguration<FormConsentVersion>
{
    public void Configure(EntityTypeBuilder<FormConsentVersion> b)
    {
        b.ToTable("form_consent_versions");
        b.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => new { x.FormId, x.Version }).IsUnique();
        b.HasOne<Form>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> b)
    {
        b.ToTable("form_templates");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(60);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.SchemaJson).IsRequired();
        b.Property(x => x.SubmitLabel).HasMaxLength(60).IsRequired();
        b.Property(x => x.SuccessMessage).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ConsentText).HasMaxLength(2000);
        b.Property(x => x.AutoresponderSubject).HasMaxLength(200);
        b.Property(x => x.AutoresponderBody).HasMaxLength(5000);
    }
}

internal sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> b)
    {
        b.ToTable("form_submissions");
        b.Property(x => x.DataJson).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.VariantKey).HasMaxLength(1);
        b.Property(x => x.UtmSource).HasMaxLength(100);
        b.Property(x => x.UtmMedium).HasMaxLength(100);
        b.Property(x => x.UtmCampaign).HasMaxLength(150);
        b.Property(x => x.UtmTerm).HasMaxLength(150);
        b.Property(x => x.UtmContent).HasMaxLength(150);
        b.Property(x => x.Referrer).HasMaxLength(1000);
        b.Property(x => x.EmbedOrigin).HasMaxLength(253);
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.HasIndex(x => new { x.FormId, x.SubmittedAt });
        b.HasIndex(x => new { x.LandingPageId, x.SubmittedAt });
        b.HasIndex(x => x.ClientAccountId);
        b.HasIndex(x => x.EventPublishedAt);
        b.HasOne<Form>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormSubmissionFileConfiguration : IEntityTypeConfiguration<FormSubmissionFile>
{
    public void Configure(EntityTypeBuilder<FormSubmissionFile> b)
    {
        b.ToTable("form_submission_files");
        b.Property(x => x.FieldKey).HasMaxLength(40).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(120).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.SubmissionId);
        b.HasOne<FormSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FormEmailOutboxConfiguration : IEntityTypeConfiguration<FormEmailOutbox>
{
    public void Configure(EntityTypeBuilder<FormEmailOutbox> b)
    {
        b.ToTable("form_email_outbox");
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.ToAddress).HasMaxLength(254).IsRequired();
        b.Property(x => x.ToName).HasMaxLength(200);
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(10000).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => x.Key).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}
