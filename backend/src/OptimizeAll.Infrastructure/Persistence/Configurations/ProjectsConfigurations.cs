using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

// Delivery (projects, tasks, deliverables, time, reports, briefs, messages, meetings). Every client-owned table carries
// ClientAccountId so IClientScope can filter it directly. Portable across MySQL and SQLite: no column types/collations.

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("projects");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description);
        b.Property(x => x.ServiceLines).HasJsonList();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.TemplateKey).HasMaxLength(64);
        b.Ignore(x => x.IsRetainer);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasIndex(x => x.OwnerUserId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> b)
    {
        b.ToTable("project_members");
        b.HasKey(x => new { x.ProjectId, x.UserId });
        b.HasIndex(x => x.UserId);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> b)
    {
        b.ToTable("project_milestones");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ProjectId);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> b)
    {
        b.ToTable("project_tasks");
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description);
        b.Property(x => x.Labels).HasJsonList();
        b.Property(x => x.RecurrenceKey).HasMaxLength(80);
        b.HasIndex(x => x.RecurrenceKey).IsUnique();
        b.HasIndex(x => new { x.ProjectId, x.Status });
        b.HasIndex(x => new { x.ClientAccountId, x.DueDate });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Milestone>().WithMany().HasForeignKey(x => x.MilestoneId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> b)
    {
        b.ToTable("task_assignees");
        b.HasKey(x => new { x.TaskId, x.UserId });
        b.HasIndex(x => x.UserId);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskWatcherConfiguration : IEntityTypeConfiguration<TaskWatcher>
{
    public void Configure(EntityTypeBuilder<TaskWatcher> b)
    {
        b.ToTable("task_watchers");
        b.HasKey(x => new { x.TaskId, x.UserId });
        b.HasIndex(x => x.UserId);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskChecklistItemConfiguration : IEntityTypeConfiguration<TaskChecklistItem>
{
    public void Configure(EntityTypeBuilder<TaskChecklistItem> b)
    {
        b.ToTable("task_checklist_items");
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.HasIndex(x => x.TaskId);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> b)
    {
        b.ToTable("task_dependencies");
        b.HasKey(x => new { x.TaskId, x.BlockedByTaskId });
        b.HasIndex(x => x.BlockedByTaskId);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.BlockedByTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> b)
    {
        b.ToTable("task_comments");
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.MentionedUserIds).HasJsonList();
        b.HasIndex(x => new { x.TaskId, x.CreatedAt });
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> b)
    {
        b.ToTable("task_attachments");
        b.HasIndex(x => x.TaskId);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DeliveryFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RecurringTaskRuleConfiguration : IEntityTypeConfiguration<RecurringTaskRule>
{
    public void Configure(EntityTypeBuilder<RecurringTaskRule> b)
    {
        b.ToTable("recurring_task_rules");
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description);
        b.Property(x => x.Labels).HasJsonList();
        b.HasIndex(x => new { x.ProjectId, x.IsActive });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProjectTemplateConfiguration : IEntityTypeConfiguration<ProjectTemplate>
{
    public void Configure(EntityTypeBuilder<ProjectTemplate> b)
    {
        b.ToTable("project_templates");
        b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.ServiceLines).HasJsonList();
        b.Property(x => x.Milestones).HasJsonList();
        b.Property(x => x.Tasks).HasJsonList();
        b.Property(x => x.Recurring).HasJsonList();
    }
}

internal sealed class DeliveryFileConfiguration : IEntityTypeConfiguration<DeliveryFile>
{
    public void Configure(EntityTypeBuilder<DeliveryFile> b)
    {
        b.ToTable("delivery_files");
        b.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ClientAccountId);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliveryDispatchKeyConfiguration : IEntityTypeConfiguration<DeliveryDispatchKey>
{
    public void Configure(EntityTypeBuilder<DeliveryDispatchKey> b)
    {
        b.ToTable("delivery_dispatch_keys");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(200);
    }
}

internal sealed class DeliverableConfiguration : IEntityTypeConfiguration<Deliverable>
{
    public void Configure(EntityTypeBuilder<Deliverable> b)
    {
        b.ToTable("deliverables");
        b.Property(x => x.Title).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => new { x.Status, x.ClientDueAt });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class DeliverableVersionConfiguration : IEntityTypeConfiguration<DeliverableVersion>
{
    public void Configure(EntityTypeBuilder<DeliverableVersion> b)
    {
        b.ToTable("deliverable_versions");
        b.Property(x => x.LinkUrl).HasMaxLength(1000);
        b.Property(x => x.Body);
        b.Property(x => x.Notes);
        b.HasIndex(x => new { x.DeliverableId, x.Number }).IsUnique();
        b.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DeliveryFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliverableReviewConfiguration : IEntityTypeConfiguration<DeliverableReview>
{
    public void Configure(EntityTypeBuilder<DeliverableReview> b)
    {
        b.ToTable("deliverable_reviews");
        b.Property(x => x.UserName).HasMaxLength(200);
        b.Property(x => x.Comment);
        b.HasIndex(x => new { x.DeliverableId, x.CreatedAt });
        b.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeliverableCommentConfiguration : IEntityTypeConfiguration<DeliverableComment>
{
    public void Configure(EntityTypeBuilder<DeliverableComment> b)
    {
        b.ToTable("deliverable_comments");
        b.Property(x => x.Body).IsRequired();
        b.HasIndex(x => new { x.DeliverableId, x.VersionNumber });
        b.HasOne<Deliverable>().WithMany().HasForeignKey(x => x.DeliverableId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> b)
    {
        b.ToTable("time_entries");
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Ignore(x => x.IsRunning);
        b.HasIndex(x => new { x.UserId, x.Date });
        b.HasIndex(x => new { x.ProjectId, x.Date });
        b.HasIndex(x => new { x.ClientAccountId, x.Date });
        // Agency-wide ranges (admin dashboard week totals, utilization report).
        b.HasIndex(x => x.Date);
        // One running timer per user: NULL for stopped entries (NULLs never collide in a unique index on MySQL or SQLite).
        b.HasIndex(x => x.RunningUserId).IsUnique();
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProjectTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TimesheetConfiguration : IEntityTypeConfiguration<Timesheet>
{
    public void Configure(EntityTypeBuilder<Timesheet> b)
    {
        b.ToTable("timesheets");
        b.Property(x => x.DecisionComment).HasMaxLength(1000);
        b.HasIndex(x => new { x.UserId, x.WeekStart }).IsUnique();
        b.HasIndex(x => new { x.Status, x.WeekStart });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HourlyRateConfiguration : IEntityTypeConfiguration<HourlyRate>
{
    public void Configure(EntityTypeBuilder<HourlyRate> b)
    {
        b.ToTable("hourly_rates");
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.Role);
    }
}

internal sealed class ClientReportConfiguration : IEntityTypeConfiguration<ClientReport>
{
    public void Configure(EntityTypeBuilder<ClientReport> b)
    {
        b.ToTable("client_reports");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.TemplateKey).HasMaxLength(64);
        b.Property(x => x.Sections).HasJsonList();
        b.Property(x => x.AutoKey).HasMaxLength(80);
        b.HasIndex(x => x.AutoKey).IsUnique();
        b.HasIndex(x => new { x.ClientAccountId, x.PeriodStart });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> b)
    {
        b.ToTable("report_templates");
        b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Sections).HasJsonList();
    }
}

internal sealed class BriefTemplateConfiguration : IEntityTypeConfiguration<BriefTemplate>
{
    public void Configure(EntityTypeBuilder<BriefTemplate> b)
    {
        b.ToTable("brief_templates");
        b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.ServiceLine).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Fields).HasJsonList();
    }
}

internal sealed class BriefConfiguration : IEntityTypeConfiguration<Brief>
{
    public void Configure(EntityTypeBuilder<Brief> b)
    {
        b.ToTable("briefs");
        b.Property(x => x.TemplateKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Answers).HasJsonList();
        b.Property(x => x.StaffNote).HasMaxLength(2000);
        b.HasIndex(x => new { x.ClientAccountId, x.Status });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MessageThreadConfiguration : IEntityTypeConfiguration<MessageThread>
{
    public void Configure(EntityTypeBuilder<MessageThread> b)
    {
        b.ToTable("message_threads");
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.LastMessagePreview).HasMaxLength(200);
        b.HasIndex(x => new { x.ClientAccountId, x.LastMessageAt });
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ThreadMessageConfiguration : IEntityTypeConfiguration<ThreadMessage>
{
    public void Configure(EntityTypeBuilder<ThreadMessage> b)
    {
        b.ToTable("thread_messages");
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.AttachmentFileIds).HasJsonList();
        b.HasIndex(x => new { x.ThreadId, x.CreatedAt });
        // Client health "last activity": newest message per client.
        b.HasIndex(x => new { x.ClientAccountId, x.CreatedAt });
        b.HasOne<MessageThread>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ThreadReadStateConfiguration : IEntityTypeConfiguration<ThreadReadState>
{
    public void Configure(EntityTypeBuilder<ThreadReadState> b)
    {
        b.ToTable("thread_read_states");
        b.HasKey(x => new { x.ThreadId, x.UserId });
        b.HasOne<MessageThread>().WithMany().HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> b)
    {
        b.ToTable("client_meetings");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Location).HasMaxLength(500);
        b.Property(x => x.Agenda);
        b.Property(x => x.Notes);
        b.Property(x => x.AttendeeUserIds).HasJsonList();
        b.Property(x => x.ActionItems).HasJsonList();
        b.HasIndex(x => new { x.ClientAccountId, x.StartsAt });
        b.HasIndex(x => x.StartsAt);
        b.HasOne<ClientAccount>().WithMany().HasForeignKey(x => x.ClientAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
