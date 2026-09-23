using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Email & SMS marketing (Modules/EmailMarketing). Portable across MySQL and SQLite: no provider column types or
// collations; long text has a max length (or none for unbounded); per-workspace uniqueness uses ScopeKey because the
// agency workspace has ClientAccountId = null.

internal sealed class EmailListConfiguration : IEntityTypeConfiguration<EmailList>
{
    public void Configure(EntityTypeBuilder<EmailList> b)
    {
        b.ToTable("email_lists");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.PublicKey).HasMaxLength(32).IsRequired();
        b.Property(x => x.ConsentTextVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.ConsentText).HasMaxLength(1000).IsRequired();
        b.Property(x => x.SeedKey).HasMaxLength(60);
        b.HasIndex(x => x.PublicKey).IsUnique();
        b.HasIndex(x => new { x.ScopeKey, x.SeedKey }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SubscriberConfiguration : IEntityTypeConfiguration<Subscriber>
{
    public void Configure(EntityTypeBuilder<Subscriber> b)
    {
        b.ToTable("email_subscribers");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.NormalizedEmail).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(16);
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.Property(x => x.Language).HasMaxLength(12);
        b.Property(x => x.CountryCode).HasMaxLength(2);
        b.Property(x => x.TimeZone).HasMaxLength(64);
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.ScopeKey, x.NormalizedEmail }).IsUnique();
        b.HasIndex(x => new { x.ScopeKey, x.Phone });
        b.HasIndex(x => new { x.ScopeKey, x.Status });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ListMembershipConfiguration : IEntityTypeConfiguration<ListMembership>
{
    public void Configure(EntityTypeBuilder<ListMembership> b)
    {
        b.ToTable("email_list_memberships");
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.ListId, x.SubscriberId }).IsUnique();
        b.HasIndex(x => new { x.ListId, x.Status });
        b.HasIndex(x => x.SubscriberId);
        b.HasOne<EmailList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SubscriberTagConfiguration : IEntityTypeConfiguration<SubscriberTag>
{
    public void Configure(EntityTypeBuilder<SubscriberTag> b)
    {
        b.ToTable("email_subscriber_tags");
        b.HasKey(x => new { x.SubscriberId, x.Tag });
        b.Property(x => x.Tag).HasMaxLength(50);
        b.HasIndex(x => x.Tag);
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SubscriberFieldConfiguration : IEntityTypeConfiguration<SubscriberField>
{
    public void Configure(EntityTypeBuilder<SubscriberField> b)
    {
        b.ToTable("email_subscriber_fields");
        b.HasKey(x => new { x.SubscriberId, x.Key });
        b.Property(x => x.Key).HasMaxLength(40);
        b.Property(x => x.Value).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.Key);
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> b)
    {
        b.ToTable("email_consent_records");
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.ConsentTextVersion).HasMaxLength(40);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.SubscriberId, x.RecordedAt });
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SuppressionConfiguration : IEntityTypeConfiguration<Suppression>
{
    public void Configure(EntityTypeBuilder<Suppression> b)
    {
        b.ToTable("email_suppressions");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Value).HasMaxLength(254).IsRequired();
        b.Property(x => x.Source).HasMaxLength(60).IsRequired();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.ScopeKey, x.Channel, x.Value }).IsUnique();
        b.HasIndex(x => new { x.ScopeKey, x.CreatedAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SubscriberImportConfiguration : IEntityTypeConfiguration<SubscriberImport>
{
    public void Configure(EntityTypeBuilder<SubscriberImport> b)
    {
        b.ToTable("email_imports");
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.CsvContent);
        b.Property(x => x.MappingJson).HasMaxLength(8000).IsRequired();
        b.Property(x => x.TagsJson).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ConsentAttestation).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ConsentSource).HasMaxLength(200).IsRequired();
        b.Property(x => x.ErrorsJson).HasMaxLength(100_000).IsRequired();
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.ListId);
        b.HasOne<EmailList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SenderProfileConfiguration : IEntityTypeConfiguration<SenderProfile>
{
    public void Configure(EntityTypeBuilder<SenderProfile> b)
    {
        b.ToTable("email_sender_profiles");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.FromName).HasMaxLength(100).IsRequired();
        b.Property(x => x.FromEmail).HasMaxLength(254).IsRequired();
        b.Property(x => x.ReplyTo).HasMaxLength(254);
        b.Property(x => x.VerificationCodeHash).HasMaxLength(64);
        b.HasIndex(x => x.ScopeKey);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailWorkspaceSettingsConfiguration : IEntityTypeConfiguration<EmailWorkspaceSettings>
{
    public void Configure(EntityTypeBuilder<EmailWorkspaceSettings> b)
    {
        b.ToTable("email_workspace_settings");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
        b.Property(x => x.PhysicalAddress).HasMaxLength(500).IsRequired();
        b.Property(x => x.EmailProvider).HasMaxLength(20).IsRequired();
        b.Property(x => x.DefaultTimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.CostCurrency).HasMaxLength(3).IsRequired();
        b.HasIndex(x => x.ScopeKey).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> b)
    {
        b.ToTable("email_templates");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Category).HasMaxLength(40).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.PreviewText).HasMaxLength(200);
        b.Property(x => x.DesignJson).HasMaxLength(200_000).IsRequired();
        b.Property(x => x.SeedKey).HasMaxLength(60);
        b.HasIndex(x => new { x.ScopeKey, x.SeedKey }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> b)
    {
        b.ToTable("email_segments");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.DefinitionJson).HasMaxLength(20_000).IsRequired();
        b.HasIndex(x => x.ScopeKey);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailCampaignConfiguration : IEntityTypeConfiguration<EmailCampaign>
{
    public void Configure(EntityTypeBuilder<EmailCampaign> b)
    {
        b.ToTable("email_campaigns");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.PreviewText).HasMaxLength(200);
        b.Property(x => x.DesignJson).HasMaxLength(200_000).IsRequired();
        b.Property(x => x.Topic).HasMaxLength(60);
        b.Property(x => x.SmsBody).HasMaxLength(1600);
        b.Property(x => x.WhatsAppTemplateName).HasMaxLength(100);
        b.Property(x => x.WhatsAppTemplateLanguage).HasMaxLength(12);
        b.Property(x => x.WhatsAppParametersJson).HasMaxLength(4000);
        b.Property(x => x.ScheduledLocalTime).HasMaxLength(20);
        b.Property(x => x.AbWinnerVariant).HasMaxLength(2);
        b.Property(x => x.ApprovalNote).HasMaxLength(1000);
        b.Property(x => x.PauseReason).HasMaxLength(500);
        b.HasIndex(x => new { x.ScopeKey, x.Status });
        b.HasIndex(x => new { x.Status, x.ScheduledAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CampaignVariantConfiguration : IEntityTypeConfiguration<CampaignVariant>
{
    public void Configure(EntityTypeBuilder<CampaignVariant> b)
    {
        b.ToTable("email_campaign_variants");
        b.Property(x => x.Key).HasMaxLength(2).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.PreviewText).HasMaxLength(200);
        b.Property(x => x.DesignJson).HasMaxLength(200_000);
        b.HasIndex(x => new { x.CampaignId, x.Key }).IsUnique();
        b.HasOne<EmailCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> b)
    {
        b.ToTable("email_campaign_recipients");
        b.Property(x => x.Variant).HasMaxLength(2);
        b.Property(x => x.Address).HasMaxLength(254).IsRequired();
        b.Property(x => x.SkipReason).HasMaxLength(200);
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(1000);
        b.HasIndex(x => new { x.CampaignId, x.SubscriberId }).IsUnique();
        b.HasIndex(x => new { x.CampaignId, x.Status, x.DueAt });
        b.HasIndex(x => new { x.SubscriberId, x.SentAt });
        b.HasIndex(x => x.ProviderMessageId);
        b.HasOne<EmailCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TrackedLinkConfiguration : IEntityTypeConfiguration<TrackedLink>
{
    public void Configure(EntityTypeBuilder<TrackedLink> b)
    {
        b.ToTable("email_tracked_links");
        b.Property(x => x.SourceKey).HasMaxLength(80).IsRequired();
        b.Property(x => x.UrlHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => new { x.SourceKey, x.UrlHash }).IsUnique();
    }
}

internal sealed class EngagementEventConfiguration : IEntityTypeConfiguration<EngagementEvent>
{
    public void Configure(EntityTypeBuilder<EngagementEvent> b)
    {
        b.ToTable("email_events");
        b.Property(x => x.MailClient).HasMaxLength(60);
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.Name).HasMaxLength(64);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ExternalReference).HasMaxLength(150);
        b.Property(x => x.Provider).HasMaxLength(20);
        b.Property(x => x.Detail).HasMaxLength(2000);
        b.Property(x => x.DedupKey).HasMaxLength(200);
        b.HasIndex(x => x.DedupKey).IsUnique();
        b.HasIndex(x => new { x.SubscriberId, x.Type, x.OccurredAt });
        b.HasIndex(x => new { x.CampaignId, x.Type });
        b.HasIndex(x => new { x.AutomationId, x.Type });
        b.HasIndex(x => new { x.ClientAccountId, x.Type, x.OccurredAt });
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AutomationConfiguration : IEntityTypeConfiguration<Automation>
{
    public void Configure(EntityTypeBuilder<Automation> b)
    {
        b.ToTable("email_automations");
        b.Property(x => x.ScopeKey).HasMaxLength(36).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.TriggerConfigJson).HasMaxLength(2000).IsRequired();
        b.Property(x => x.GoalJson).HasMaxLength(1000);
        b.Property(x => x.EntryStepKey).HasMaxLength(20);
        b.Property(x => x.SeedKey).HasMaxLength(60);
        b.Property(x => x.LastAnniversaryScan).HasMaxLength(10);
        b.HasIndex(x => new { x.Status, x.Trigger });
        b.HasIndex(x => new { x.ScopeKey, x.SeedKey }).IsUnique();
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AutomationStepConfiguration : IEntityTypeConfiguration<AutomationStep>
{
    public void Configure(EntityTypeBuilder<AutomationStep> b)
    {
        b.ToTable("email_automation_steps");
        b.Property(x => x.Key).HasMaxLength(20).IsRequired();
        b.Property(x => x.ConfigJson).HasMaxLength(8000).IsRequired();
        b.Property(x => x.NextKey).HasMaxLength(20);
        b.Property(x => x.AltNextKey).HasMaxLength(20);
        b.HasIndex(x => new { x.AutomationId, x.Key }).IsUnique();
        b.HasOne<Automation>().WithMany().HasForeignKey(x => x.AutomationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AutomationEnrollmentConfiguration : IEntityTypeConfiguration<AutomationEnrollment>
{
    public void Configure(EntityTypeBuilder<AutomationEnrollment> b)
    {
        b.ToTable("email_automation_enrollments");
        b.Property(x => x.CurrentStepKey).HasMaxLength(20);
        b.Property(x => x.ExitReason).HasMaxLength(500);
        b.Property(x => x.TriggerDataJson).HasMaxLength(4000);
        b.HasIndex(x => new { x.AutomationId, x.SubscriberId, x.Iteration }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextRunAt });
        b.HasIndex(x => x.SubscriberId);
        b.HasOne<Automation>().WithMany().HasForeignKey(x => x.AutomationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AutomationStepRunConfiguration : IEntityTypeConfiguration<AutomationStepRun>
{
    public void Configure(EntityTypeBuilder<AutomationStepRun> b)
    {
        b.ToTable("email_automation_step_runs");
        b.Property(x => x.StepKey).HasMaxLength(20).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.Address).HasMaxLength(254);
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);
        b.HasIndex(x => new { x.EnrollmentId, x.StepKey }).IsUnique();
        b.HasIndex(x => new { x.AutomationId, x.StepKey });
        b.HasIndex(x => x.ProviderMessageId);
        b.HasOne<AutomationEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Cascade);
    }
}
