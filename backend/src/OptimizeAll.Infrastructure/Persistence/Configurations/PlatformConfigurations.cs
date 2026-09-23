using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Support;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class HomepageBannerConfiguration : IEntityTypeConfiguration<HomepageBanner>
{
    public void Configure(EntityTypeBuilder<HomepageBanner> b)
    {
        b.ToTable("homepage_banners");
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.CtaLabel).HasMaxLength(60);
        b.Property(x => x.CtaUrl).HasMaxLength(500);
        b.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        b.Property(x => x.LanguageCode).HasMaxLength(10);
        b.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable("announcements");
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.IsActive, x.PublishAt });
    }
}

internal sealed class FaqItemConfiguration : IEntityTypeConfiguration<FaqItem>
{
    public void Configure(EntityTypeBuilder<FaqItem> b)
    {
        b.ToTable("faq_items");
        b.Property(x => x.Question).HasMaxLength(300).IsRequired();
        b.Property(x => x.Answer).HasColumnType("text").IsRequired();
        b.Property(x => x.Category).HasMaxLength(60).IsRequired();
    }
}

internal sealed class OnboardingStepConfiguration : IEntityTypeConfiguration<OnboardingStep>
{
    public void Configure(EntityTypeBuilder<OnboardingStep> b)
    {
        b.ToTable("onboarding_steps");
        b.Property(x => x.Key).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ActionLabel).HasMaxLength(60);
        b.Property(x => x.ActionUrl).HasMaxLength(300);
    }
}

internal sealed class OnboardingStepCompletionConfiguration : IEntityTypeConfiguration<OnboardingStepCompletion>
{
    public void Configure(EntityTypeBuilder<OnboardingStepCompletion> b)
    {
        b.ToTable("onboarding_step_completions");
        b.HasKey(x => new { x.UserId, x.StepId });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<OnboardingStep>().WithMany().HasForeignKey(x => x.StepId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.Property(x => x.Type).HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        b.Property(x => x.LinkUrl).HasMaxLength(500);
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("notification_deliveries");
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.NotificationId, x.Channel }).IsUnique();
        b.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences");
        b.HasKey(x => new { x.UserId, x.Type, x.Channel });
        b.Property(x => x.Type).HasMaxLength(60);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("support_tickets");
        b.Property(x => x.Reference).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.Status, x.Priority, x.UpdatedAt });
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> b)
    {
        b.ToTable("support_messages");
        b.Property(x => x.Body).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.TicketId, x.CreatedAt });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.ActorType).HasMaxLength(40).IsRequired();
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
        b.Property(x => x.BeforeJson).HasColumnType("json");
        b.Property(x => x.AfterJson).HasColumnType("json");
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.ActorUserId);
        b.HasIndex(x => x.Action);
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> b)
    {
        b.ToTable("job_runs");
        b.Property(x => x.JobName).HasMaxLength(100).IsRequired();
        b.Property(x => x.RunKey).HasMaxLength(150).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(2000);
        b.Property(x => x.Error).HasColumnType("text");
        b.Property(x => x.InstanceId).HasMaxLength(100);
        b.HasIndex(x => new { x.JobName, x.RunKey, x.Attempt }).IsUnique();
        b.HasIndex(x => new { x.Status, x.StartedAt });
    }
}

internal sealed class JobLeaseConfiguration : IEntityTypeConfiguration<JobLease>
{
    public void Configure(EntityTypeBuilder<JobLease> b)
    {
        b.ToTable("job_leases");
        b.HasKey(x => x.Name);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Holder).HasMaxLength(100).IsRequired();
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("system_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.ValueJson).HasColumnType("json").IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
    }
}
