using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.DbBench;

/// <summary>One benchmarked endpoint/job: the LINQ below mirrors the module code it names (kept in sync by hand).</summary>
public sealed record BenchCase(string Name, string Serves, Func<AppDbContext, BenchContext, Task> Run);

/// <summary>Sample ids picked from the data (the busiest user, client, campaign... so the worst realistic case is measured).</summary>
public sealed class BenchContext
{
    public DateTime Now { get; } = DateTime.UtcNow;
    public Guid BusyUserId { get; set; }
    public Guid AuditActorId { get; set; }
    public string AuditEntityType { get; set; } = "";
    public string AuditEntityId { get; set; } = "";
    public Guid EmailClientId { get; set; }
    public Guid EmailCampaignId { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid ReviewerId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid FormId { get; set; }
    public string IpHash { get; set; } = "";
    public Guid PageId { get; set; }
    public string VisitorHash { get; set; } = "";
    public Guid LeadsClientId { get; set; }
    public Guid AdsClientId { get; set; }
    public Guid? AdCampaignId { get; set; }
    public List<Guid> ReviewerIds { get; set; } = new();
    public List<Guid> ClientIds { get; set; } = new();
    public string BusiestJob { get; set; } = "";

    public static async Task<BenchContext> ResolveAsync(AppDbContext db)
    {
        var c = new BenchContext();
        c.BusyUserId = await db.Set<Notification>().GroupBy(n => n.UserId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.AuditActorId = await db.Set<AuditLog>().Where(a => a.ActorUserId != null).GroupBy(a => a.ActorUserId!.Value)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        var entity = await db.Set<AuditLog>().GroupBy(a => new { a.EntityType, a.EntityId }).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstAsync();
        (c.AuditEntityType, c.AuditEntityId) = (entity.EntityType, entity.EntityId);
        var scope = await db.Set<Subscriber>().Where(s => s.ClientAccountId != null).GroupBy(s => s.ClientAccountId!.Value)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.EmailClientId = scope;
        c.EmailCampaignId = await db.Set<CampaignRecipient>().GroupBy(r => r.CampaignId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.SubscriberId = await db.Set<EngagementEvent>().GroupBy(e => e.SubscriberId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.ReviewerId = await db.Set<SubmissionEvent>().Where(e => e.ActorUserId != null && e.Action == "approved")
            .GroupBy(e => e.ActorUserId!.Value).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.ReviewerIds = await db.Set<SubmissionEvent>().Where(e => e.ActorUserId != null && e.Action == "approved")
            .Select(e => e.ActorUserId!.Value).Distinct().Take(20).ToListAsync();
        c.ParticipantId = await db.Set<Submission>().GroupBy(s => s.UserId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.FormId = await db.Set<FormSubmission>().GroupBy(s => s.FormId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.IpHash = await db.Set<FormSubmission>().Where(s => s.IpHash != null).GroupBy(s => s.IpHash!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        var view = await db.Set<LandingPageView>().GroupBy(v => new { v.PageId, v.VisitorHash }).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        (c.PageId, c.VisitorHash) = (view.PageId, view.VisitorHash);
        c.LeadsClientId = await db.Set<LandingPageView>().GroupBy(v => v.ClientAccountId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        c.AdsClientId = await db.Set<AdDailyMetric>().GroupBy(m => m.ClientAccountId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefaultAsync();
        c.AdCampaignId = await db.Set<AdDailyMetric>().Where(m => m.CampaignId != null).Select(m => m.CampaignId).FirstOrDefaultAsync();
        c.ClientIds = await db.Set<OptimizeAll.Domain.Agency.ClientAccount>().Select(x => x.Id).ToListAsync();
        c.BusiestJob = await db.Set<JobRun>().GroupBy(r => r.JobName).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
        return c;
    }
}

public static class BenchCases
{
    private static string Like(string s) => "%" + s + "%";

    public static IReadOnlyList<BenchCase> All() => new List<BenchCase>
    {
        // ---------------------------------------------------------------- audit log (admin)
        new("audit.list", "GET /admin/audit-logs (page 1, no filter: count + page)", async (db, c) =>
        {
            var logs = db.Set<AuditLog>().AsNoTracking();
            await Count(db, logs);
            await WithActors(db, logs).Skip(0).Take(25).ToListAsync();
        }),
        new("audit.list.entity", "GET /admin/audit-logs?entityType&entityId", async (db, c) =>
        {
            var logs = db.Set<AuditLog>().AsNoTracking().Where(l => l.EntityType == c.AuditEntityType && l.EntityId == c.AuditEntityId);
            await Count(db, logs);
            await WithActors(db, logs).Skip(0).Take(25).ToListAsync();
        }),
        new("audit.list.actor", "GET /admin/audit-logs?actorUserId", async (db, c) =>
        {
            var logs = db.Set<AuditLog>().AsNoTracking().Where(l => l.ActorUserId == c.AuditActorId);
            await Count(db, logs);
            await WithActors(db, logs).Skip(0).Take(25).ToListAsync();
        }),
        new("audit.list.range", "GET /admin/audit-logs?from&to (last 7 days)", async (db, c) =>
        {
            var from = c.Now.AddDays(-7);
            var logs = db.Set<AuditLog>().AsNoTracking().Where(l => l.CreatedAt >= from && l.CreatedAt <= c.Now);
            await Count(db, logs);
            await WithActors(db, logs).Skip(0).Take(25).ToListAsync();
        }),
        new("audit.user-status-history", "GET /admin/users/{id} (status history)", async (db, c) =>
        {
            var id = c.BusyUserId.ToString();
            var actions = new[] { "admin.user_suspended", "admin.user_reactivated" };
            await (from l in db.Set<AuditLog>().AsNoTracking()
                   where l.EntityType == nameof(User) && l.EntityId == id && actions.Contains(l.Action)
                   join a in db.Set<User>().AsNoTracking() on l.ActorUserId equals a.Id into actors
                   from a in actors.DefaultIfEmpty()
                   orderby l.Id descending
                   select new { l.CreatedAt, l.Action, a.DisplayName }).Take(100).ToListAsync();
        }),

        // ---------------------------------------------------------------- notifications
        new("notifications.list", "GET /me/notifications (bell list, page 1)", async (db, c) =>
        {
            var q = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == c.BusyUserId);
            await q.CountAsync();
            await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(20).Select(n => new { n.Id, n.Title, n.CreatedAt, n.ReadAt }).ToListAsync();
        }),
        new("notifications.unread-count", "GET /me/notifications/unread-count, home", async (db, c) =>
            await db.Set<Notification>().CountAsync(n => n.UserId == c.BusyUserId && n.ReadAt == null)),
        new("notifications.inquiry-dedup", "InquiryNotificationHandler (per website inquiry)", async (db, c) =>
        {
            var link = "/agency/website/inquiries/" + Guid.NewGuid();
            if (Legacy)
                await db.Set<Notification>().AnyAsync(n => n.Type == "website.inquiry" && n.LinkUrl == link);
            else
            {
                var since = c.Now.AddHours(-1); // inquiry received just now (PK lookup of the inquiry not measured)
                await db.Set<Notification>().AnyAsync(n => n.Type == "website.inquiry" && n.CreatedAt >= since && n.LinkUrl == link);
            }
        }),
        new("notifications.live-check-dedup", "LiveCheckReminderJob (hourly)", async (db, c) =>
        {
            var dayStart = c.Now.Date;
            var ids = c.ReviewerIds;
            await db.Set<Notification>().Where(n => n.Type == NotificationTypes.ReviewLiveCheckDue && n.CreatedAt >= dayStart && ids.Contains(n.UserId))
                .Select(n => n.UserId).Distinct().ToListAsync();
        }),
        new("notifications.dispatch-candidates", "NotificationDispatchJob (every 30 s)", async (db, c) =>
            await db.Set<NotificationDelivery>().AsNoTracking()
                .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= c.Now && (d.LockedUntil == null || d.LockedUntil < c.Now))
                .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Select(d => d.Id).Take(100).ToListAsync()),
        new("notifications.outbox", "GET /admin/notifications/deliveries (page 1)", async (db, c) =>
        {
            var q = from d in db.Set<NotificationDelivery>().AsNoTracking()
                    join n in db.Set<Notification>().AsNoTracking() on d.NotificationId equals n.Id into ns
                    from n in ns.DefaultIfEmpty()
                    join u in db.Set<User>().AsNoTracking() on d.UserId equals u.Id into us
                    from u in us.DefaultIfEmpty()
                    select new { d, n, u };
            var ordered = q.OrderByDescending(x => x.d.CreatedAt).ThenByDescending(x => x.d.Id)
                .Select(x => new { x.d.Id, Email = x.u == null ? null : x.u.Email, Type = x.n == null ? null : x.n.Type, x.d.Status });
            if (Legacy) await ordered.CountAsync();
            else await db.Set<NotificationDelivery>().AsNoTracking().CountAsync();
            await ordered.Skip(0).Take(25).ToListAsync();
        }),

        // ---------------------------------------------------------------- job runs
        new("jobs.list", "GET /admin/jobs (last run of each of 29 jobs)", async (db, c) =>
        {
            foreach (var name in JobNames)
                await db.Set<JobRun>().AsNoTracking().Where(r => r.JobName == name)
                    .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync();
        }),
        new("jobs.runs", "GET /admin/jobs/runs (page 1)", async (db, c) =>
        {
            var q = db.Set<JobRun>().AsNoTracking().OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("jobs.runs.by-name", "GET /admin/jobs/runs?jobName (page 1)", async (db, c) =>
        {
            var q = db.Set<JobRun>().AsNoTracking().Where(r => r.JobName == c.BusiestJob).OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),

        // ---------------------------------------------------------------- email marketing
        new("email.send-candidates", "CampaignSendJob (every 30 s, per sending campaign)", async (db, c) =>
        {
            var windowStart = c.Now.AddMinutes(-1);
            if (Legacy)
                await db.Set<CampaignRecipient>().CountAsync(r => r.CampaignId == c.EmailCampaignId &&
                    (r.Status == RecipientStatus.Sending || (r.SentAt != null && r.SentAt > windowStart)));
            else
            {
                await db.Set<CampaignRecipient>().CountAsync(r => r.CampaignId == c.EmailCampaignId && r.Status == RecipientStatus.Sending);
                await db.Set<CampaignRecipient>().CountAsync(r => r.CampaignId == c.EmailCampaignId && r.SentAt > windowStart && r.Status != RecipientStatus.Sending);
            }
            await db.Set<CampaignRecipient>().AsNoTracking()
                .Where(r => r.CampaignId == c.EmailCampaignId && r.Status == RecipientStatus.Pending && r.DueAt <= c.Now && (r.LockedUntil == null || r.LockedUntil < c.Now))
                .OrderBy(r => r.DueAt).ThenBy(r => r.Id).Select(r => r.Id).Take(600).ToListAsync();
        }),
        new("email.campaign-report", "GET /email/campaigns/{id}/report (recipient aggregates + events)", async (db, c) =>
        {
            var id = c.EmailCampaignId;
            var r = db.Set<CampaignRecipient>().AsNoTracking().Where(x => x.CampaignId == id);
            await r.GroupBy(x => 1).Select(g => new
            {
                Total = g.Count(), Sent = g.Count(x => x.Status == RecipientStatus.Sent), UniqueOpens = g.Count(x => x.OpenedAt != null),
                UniqueClicks = g.Count(x => x.ClickedAt != null), TotalOpens = g.Sum(x => x.OpenCount),
            }).FirstOrDefaultAsync();
            var human = db.Set<EngagementEvent>().AsNoTracking().Where(e => e.CampaignId == id && !e.IsMachine && (e.Type == EngagementType.Open || e.Type == EngagementType.Click));
            await human.GroupBy(e => new { e.Device, e.Type }).Select(g => new { g.Key.Device, g.Key.Type, Count = g.Count() }).ToListAsync();
        }),
        new("email.kpis", "GET /email/reports/kpis (last 30 days, client workspace)", async (db, c) =>
        {
            var key = Workspace.Key(c.EmailClientId);
            var start = c.Now.AddDays(-30);
            var recipients = from r in db.Set<CampaignRecipient>().AsNoTracking()
                             join cm in db.Set<EmailCampaign>() on r.CampaignId equals cm.Id
                             where cm.ScopeKey == key && r.SentAt >= start && r.SentAt <= c.Now
                             select r;
            await recipients.Where(r => r.Channel == MessageChannel.Email).GroupBy(r => 1)
                .Select(g => new { Sent = g.Count(), Opens = g.Count(r => r.OpenedAt != null), Clicks = g.Count(r => r.ClickedAt != null) }).FirstOrDefaultAsync();
            var clientId = (Guid?)c.EmailClientId;
            await db.Set<EngagementEvent>().AsNoTracking().Where(e => e.Type == EngagementType.Conversion && e.OccurredAt >= start && e.OccurredAt <= c.Now &&
                (e.CampaignId != null || e.AutomationId != null) && e.ClientAccountId == clientId).CountAsync();
            await db.Set<Subscriber>().AsNoTracking().CountAsync(s => s.ScopeKey == key && s.Status == SubscriberStatus.Subscribed && s.EmailConsent == OptimizeAll.Domain.EmailMarketing.ConsentStatus.Granted);
        }),
        new("email.subscribers", "GET /email/subscribers (page 1, newest first)", async (db, c) =>
        {
            var key = Workspace.Key(c.EmailClientId);
            var q = db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == key).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("email.subscribers.search", "GET /email/subscribers?search=", async (db, c) =>
        {
            var key = Workspace.Key(c.EmailClientId);
            var p = Like("bench1234");
            var q = db.Set<Subscriber>().AsNoTracking().Where(s => s.ScopeKey == key && (EF.Functions.Like(s.Email!, p) || EF.Functions.Like(s.FirstName!, p) ||
                EF.Functions.Like(s.LastName!, p) || EF.Functions.Like(s.Phone!, p))).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("email.subscriber-activity", "GET /email/subscribers/{id} (activity)", async (db, c) =>
            await (from e in db.Set<EngagementEvent>().AsNoTracking()
                   where e.SubscriberId == c.SubscriberId
                   orderby e.OccurredAt descending
                   select new { e.Type, e.OccurredAt, e.CampaignId }).Take(50).ToListAsync()),
        new("email.campaign-list-stats", "GET /email/campaigns (per-page recipient stats)", async (db, c) =>
        {
            var ids = await db.Set<EmailCampaign>().AsNoTracking().OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Select(x => x.Id).Take(25).ToListAsync();
            await db.Set<CampaignRecipient>().AsNoTracking().Where(r => ids.Contains(r.CampaignId)).GroupBy(r => r.CampaignId)
                .Select(g => new { g.Key, Sent = g.Count(r => r.Status == RecipientStatus.Sent), Opens = g.Count(r => r.OpenedAt != null) }).ToListAsync();
        }),

        // ---------------------------------------------------------------- review & submissions
        new("review.queue", "GET /review/queue (oldest first)", async (db, c) =>
        {
            var q = db.Set<Submission>().AsNoTracking().Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview)
                .OrderBy(s => s.SubmittedAt).ThenBy(s => s.Id);
            await q.CountAsync();
            await (from s in q
                   join cp in db.Set<Campaign>() on s.CampaignId equals cp.Id
                   join u in db.Set<User>() on s.UserId equals u.Id
                   join a in db.Set<SocialAccount>() on s.SocialAccountId equals a.Id
                   select new { s.Id, cp.Title, u.DisplayName, a.Handle }).Skip(0).Take(25).ToListAsync();
        }),
        new("review.stats", "GET /review/stats (reviewer dashboard)", async (db, c) =>
        {
            var dayStart = c.Now.Date;
            var actions = new[] { "approved", "correction_requested", "rejected" };
            await db.Set<SubmissionEvent>().CountAsync(e => e.ActorUserId == c.ReviewerId && e.CreatedAt >= dayStart && actions.Contains(e.Action));
            await db.Set<Submission>().Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview || s.Status == SubmissionStatus.NeedsCorrection)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            await db.Set<Submission>().CountAsync(s => s.Status == SubmissionStatus.Approved && s.LiveCheckStatus == LiveCheckStatus.Pending && s.LiveCheckDueAt <= c.Now);
            await db.Set<Submission>().CountAsync(s => s.ClaimedByUserId == c.ReviewerId && s.ClaimExpiresAt > c.Now &&
                (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.UnderReview));
        }),
        new("review.reviewers", "GET /review/reviewers (decisions today per reviewer)", async (db, c) =>
        {
            var dayStart = c.Now.Date;
            var ids = c.ReviewerIds;
            var actions = new[] { "approved", "correction_requested", "rejected" };
            await db.Set<SubmissionEvent>().Where(e => e.ActorUserId != null && ids.Contains(e.ActorUserId.Value) && e.CreatedAt >= dayStart && actions.Contains(e.Action))
                .GroupBy(e => e.ActorUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        }),
        new("submissions.mine", "GET /me/submissions (page 1)", async (db, c) =>
        {
            var q = from s in db.Set<Submission>().AsNoTracking()
                    join cp in db.Set<Campaign>() on s.CampaignId equals cp.Id
                    where s.UserId == c.ParticipantId
                    orderby s.SubmittedAt descending
                    select new { s.Id, cp.Title, s.Status, s.SubmittedAt };
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("submissions.risk-velocity", "POST /me/submissions (risk signals)", async (db, c) =>
        {
            var dayAgo = c.Now.AddHours(-24);
            await db.Set<Submission>().CountAsync(s => s.UserId == c.ParticipantId && s.Id != Guid.Empty && s.SubmittedAt >= dayAgo);
        }),
        new("analytics.posts", "GET /analytics (posts in last 30 days)", async (db, c) =>
        {
            var start = c.Now.AddDays(-30);
            var testUsers = db.Set<User>().Where(u => u.IsTestAccount).Select(u => u.Id);
            var posts = db.Set<Submission>().AsNoTracking().Where(s => s.SubmittedAt >= start && s.SubmittedAt <= c.Now && !testUsers.Contains(s.UserId));
            await posts.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            await db.Set<Submission>().AsNoTracking()
                .Where(s => s.Status == SubmissionStatus.Approved && s.DecidedAt != null && s.DecidedAt >= start && s.DecidedAt <= c.Now && !testUsers.Contains(s.UserId))
                .GroupBy(s => s.DecidedAt!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync();
        }),

        // ---------------------------------------------------------------- landing pages & forms
        new("forms.rate-limit", "POST /public/forms/{id} (per-IP rate limit)", async (db, c) =>
        {
            var since = c.Now.AddMinutes(-10);
            var recent = db.Set<FormSubmission>().AsNoTracking().Where(s => s.IpHash == c.IpHash && s.SubmittedAt >= since);
            await recent.CountAsync(s => s.FormId == c.FormId);
            await recent.CountAsync();
        }),
        new("forms.submissions", "GET /landing/forms/{id}/submissions (page 1)", async (db, c) =>
        {
            var q = db.Set<FormSubmission>().AsNoTracking().Where(s => s.FormId == c.FormId && s.Status != FormSubmissionStatus.Spam)
                .OrderByDescending(s => s.SubmittedAt);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("landing.view-dedup", "GET /public/p/{slug} (unique-view check per visit)", async (db, c) =>
        {
            var dayStart = c.Now.Date;
            await db.Set<LandingPageView>().AsNoTracking().AnyAsync(v => v.PageId == c.PageId && v.VisitorHash == c.VisitorHash && v.ViewedAt >= dayStart);
        }),
        new("seo.kpi-leads", "GET /seo/kpis, client reports (views + leads by client, 90 days)", async (db, c) =>
        {
            var from = c.Now.AddDays(-90);
            await db.Set<LandingPageView>().AsNoTracking().Where(v => v.ClientAccountId == c.LeadsClientId && v.ViewedAt >= from && v.ViewedAt <= c.Now)
                .GroupBy(v => v.PageId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            await db.Set<FormSubmission>().AsNoTracking().Where(s => s.ClientAccountId == c.LeadsClientId && s.SubmittedAt >= from && s.SubmittedAt <= c.Now)
                .Select(s => new { s.LandingPageId, s.SubmittedAt }).ToListAsync();
        }),
        new("tracking.summary", "GET /marketing/tracking/summary (30 days)", async (db, c) =>
        {
            var start = c.Now.AddDays(-30);
            var testUsers = db.Set<User>().Where(u => u.IsTestAccount).Select(u => u.Id);
            var links = db.Set<TrackingLink>().AsNoTracking().Where(l => l.UserId == null || !testUsers.Contains(l.UserId.Value));
            var clicks = from k in db.Set<TrackingClick>().AsNoTracking()
                         join l in links on k.TrackingLinkId equals l.Id
                         where k.ClickedAt >= start && k.ClickedAt <= c.Now
                         select new { k.IsSuspectedBot, k.IsUnique, l.UserId };
            await clicks.GroupBy(_ => 1).Select(g => new { Human = g.Count(x => !x.IsSuspectedBot), Bots = g.Count(x => x.IsSuspectedBot) }).FirstOrDefaultAsync();
        }),

        // ---------------------------------------------------------------- delivery (projects/time/clients)
        new("time.admin-week", "GET /agency/dashboard (admin: this week's minutes)", async (db, c) =>
        {
            var week = DateOnly.FromDateTime(c.Now).AddDays(-6);
            var end = DateOnly.FromDateTime(c.Now);
            var weekEntries = db.Set<TimeEntry>().AsNoTracking().Where(e => e.Date >= week && e.Date <= end);
            await weekEntries.SumAsync(e => (int?)e.Minutes);
            await weekEntries.Where(e => e.Billable).SumAsync(e => (int?)e.Minutes);
        }),
        new("time.utilization", "GET /agency/time/utilization (30 days)", async (db, c) =>
        {
            var from = DateOnly.FromDateTime(c.Now).AddDays(-30);
            var to = DateOnly.FromDateTime(c.Now);
            await db.Set<TimeEntry>().AsNoTracking().Where(e => e.Date >= from && e.Date <= to && e.RunningUserId == null)
                .GroupBy(e => e.UserId).Select(g => new { g.Key, Total = g.Sum(e => e.Minutes) }).ToListAsync();
        }),
        new("clients.health-last-activity", "GET /agency/clients (health: last message/time)", async (db, c) =>
        {
            var ids = c.ClientIds;
            await db.Set<ThreadMessage>().AsNoTracking().Where(m => ids.Contains(m.ClientAccountId))
                .GroupBy(m => m.ClientAccountId).Select(g => new { g.Key, At = g.Max(m => m.CreatedAt) }).ToListAsync();
            await db.Set<TimeEntry>().AsNoTracking().Where(t => ids.Contains(t.ClientAccountId))
                .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, At = g.Max(t => t.UpdatedAt) }).ToListAsync();
        }),
        new("crm.contacts", "GET /crm/contacts (page 1, newest first)", async (db, c) =>
        {
            var q = db.Set<CrmContact>().AsNoTracking().Where(x => x.ArchivedAt == null).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
            await q.CountAsync();
            await q.Skip(0).Take(25).ToListAsync();
        }),
        new("ads.adgroup-metrics", "GET /ads/campaigns/{id}/ad-groups (30 days)", async (db, c) =>
        {
            var f = DateOnly.FromDateTime(c.Now).AddDays(-30);
            var t = DateOnly.FromDateTime(c.Now);
            await db.Set<AdDailyMetric>().AsNoTracking().Where(m => m.CampaignId == c.AdCampaignId && m.Level == AdLevel.AdGroup && m.Date >= f && m.Date <= t).ToListAsync();
            await db.Set<AdDailyMetric>().AsNoTracking().Where(m => m.Level == AdLevel.Campaign && m.Date >= f && m.Date <= t && m.ClientAccountId == c.AdsClientId).ToListAsync();
        }),

        // ---------------------------------------------------------------- housekeeping (retention) candidate scans
        new("retention.job-runs", "DataRetentionJob: job runs older than 30 days (batch of ids)", async (db, c) =>
        {
            var cutoff = c.Now.AddDays(-30);
            await db.Set<JobRun>().AsNoTracking().Where(r => r.StartedAt < cutoff && r.Status != JobRunStatus.Running).OrderBy(r => r.StartedAt).Select(r => r.Id).Take(1000).ToListAsync();
        }),
        new("retention.notifications", "DataRetentionJob: read notifications older than 180 days (batch of ids)", async (db, c) =>
        {
            var readCutoff = c.Now.AddDays(-180);
            var allCutoff = c.Now.AddDays(-365);
            var bound = OptimizeAll.Domain.Common.IdGenerator.LowerBound(readCutoff);
            var deliveries = db.Set<NotificationDelivery>();
            await db.Set<Notification>().AsNoTracking()
                .Where(n => n.Id.CompareTo(bound) < 0 && ((n.ReadAt != null && n.CreatedAt < readCutoff) || n.CreatedAt < allCutoff) &&
                            !deliveries.Any(d => d.NotificationId == n.Id && (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Sending)))
                .OrderBy(n => n.Id).Select(n => n.Id).Take(1000).ToListAsync();
        }),
        new("retention.refresh-tokens", "DataRetentionJob: expired refresh tokens (batch of ids)", async (db, c) =>
        {
            var cutoff = c.Now.AddDays(-30);
            var bound = OptimizeAll.Domain.Common.IdGenerator.LowerBound(cutoff);
            await db.Set<RefreshToken>().AsNoTracking().Where(t => t.Id.CompareTo(bound) < 0 && t.ExpiresAt < cutoff).OrderBy(t => t.Id).Select(t => t.Id).Take(1000).ToListAsync();
        }),
    };

    private static readonly string[] JobNames =
    {
        "SeoAuditJob", "RankTrackingJob", "BacklinkCheckJob", "RecurringTaskJob", "MonthlyReportDraftJob", "DeliverableSlaJob", "CrmTaskReminderJob",
        "CampaignScheduleJob", "ReferralExpiryJob", "NotificationDispatchJob", "SocialPublishingJob", "SocialEvergreenJob", "SocialMetricsSyncJob",
        "RetentionJob", "LiveCheckReminderJob", "AdsSyncJob", "AdsAlertJob", "FormEmailDispatchJob", "FormEventRetryJob", "CampaignSendJob",
        "AutomationJob", "SubscriberImportJob", "IntegrationExpiryJob", "PayoutPreparationJob", "BlogSchedulerJob", "UsedFormTokenCleanupJob",
        "RecurringInvoiceJob", "InvoiceOverdueJob", "DataRetentionJob",
    };

    /// <summary>
    /// Mirrors the module code of the build being measured: set by "--legacy true" for the "before" run (the audit-log
    /// total was counted over the actor joins until the optimization).
    /// </summary>
    public static bool Legacy { get; set; }

    private static Task<int> Count(AppDbContext db, IQueryable<AuditLog> logs) => Legacy ? WithActors(db, logs).CountAsync() : logs.CountAsync();

    private static IQueryable<object> WithActors(AppDbContext db, IQueryable<AuditLog> logs) =>
        from l in logs
        join u in db.Set<User>().AsNoTracking() on l.ActorUserId equals u.Id into actors
        from u in actors.DefaultIfEmpty()
        join i in db.Set<User>().AsNoTracking() on l.ImpersonatorUserId equals i.Id into impersonators
        from i in impersonators.DefaultIfEmpty()
        orderby l.Id descending
        select (object)new { Log = l, ActorEmail = u == null ? null : u.Email, ImpersonatorDisplayName = i == null ? null : i.DisplayName };
}
