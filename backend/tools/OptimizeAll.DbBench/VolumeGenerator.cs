using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;
using EmailCampaignStatus = OptimizeAll.Domain.EmailMarketing.CampaignStatus;
using EmailConsent = OptimizeAll.Domain.EmailMarketing.ConsentStatus;

namespace OptimizeAll.DbBench;

/// <summary>
/// Adds synthetic high-volume rows on top of a Demo-seeded database, reusing its clients, users, campaigns, forms, pages,
/// projects, links, ad accounts and brand profiles as parents. Deterministic (fixed random seed) so a "before" and an
/// "after" database get comparable data. Row ids are time-ordered (UUIDv7 at the row's own timestamp), as in production.
/// </summary>
public sealed class VolumeGenerator(AppDbContext db, double scale)
{
    private readonly Random _rng = new(20260924);
    private readonly DateTime _now = DateTime.UtcNow;
    private int N(int count) => Math.Max(1, (int)(count * scale));

    private List<Guid> _users = new();
    private List<Guid> _staff = new();
    private List<Guid> _participants = new();
    private List<Guid> _clients = new();

    public async Task RunAsync()
    {
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.SetCommandTimeout(600);
        _users = await db.Set<User>().Select(u => u.Id).ToListAsync();
        _participants = await db.Set<User>().Where(u => u.Roles.Any(r => r.Role == Role.Participant)).Select(u => u.Id).ToListAsync();
        _staff = _users.Except(_participants).ToList();
        _clients = await db.Set<ClientAccount>().Select(c => c.Id).ToListAsync();
        if (_participants.Count == 0 || _clients.Count == 0) throw new InvalidOperationException("Seed the database with Baseline + Demo first.");

        await Step("audit_logs", AuditLogsAsync);
        await Step("notifications", NotificationsAsync);
        await Step("job_runs", JobRunsAsync);
        await Step("refresh_tokens/user_tokens", TokensAsync);
        await Step("email (subscribers, campaigns, recipients, events)", EmailAsync);
        await Step("submissions/submission_events", SubmissionsAsync);
        await Step("form_submissions/landing_page_views", LandingAsync);
        await Step("tracking_clicks", ClicksAsync);
        await Step("time_entries/thread_messages", DeliveryAsync);
        await Step("crm_contacts", CrmAsync);
        await Step("ads_daily_metrics/sm_post_metrics", MetricsAsync);
    }

    private static async Task Step(string name, Func<Task<int>> step)
    {
        var sw = Stopwatch.StartNew();
        var rows = await step();
        Console.WriteLine($"{name}: {rows:N0} rows in {sw.Elapsed.TotalSeconds:F0}s");
    }

    private async Task<int> InsertAsync<T>(IEnumerable<T> rows) where T : class
    {
        var count = 0;
        foreach (var chunk in rows.Chunk(5000))
        {
            db.Set<T>().AddRange(chunk);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            count += chunk.Length;
        }
        return count;
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    /// <summary>Skewed pick: the first items get most of the traffic (busy users, big clients).</summary>
    private T Skewed<T>(IReadOnlyList<T> items) => items[(int)(items.Count * Math.Pow(_rng.NextDouble(), 3))];

    private DateTime Ago(double maxDays) => _now.AddSeconds(-_rng.NextDouble() * maxDays * 86400);
    private static Guid IdAt(DateTime at) => IdGenerator.NewId(new DateTimeOffset(at, TimeSpan.Zero));
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    // ------------------------------------------------------------------ audit

    private static readonly string[] AuditActions =
    {
        "submission.approved", "submission.rejected", "submission.claimed", "campaign.updated", "campaign.reward_rules_changed",
        "user.suspended", "user.login", "payout.batch_finalized", "payout.item_paid", "earning.approved", "settings.changed",
        "crm.contact_updated", "crm.deal_stage_changed", "invoice.issued", "invoice.payment_recorded", "email.campaign.sent",
        "social.post_published", "seo.audit_completed", "project.task_updated", "time.entry_created",
    };
    private static readonly string[] EntityTypes =
        { "Submission", "Campaign", "User", "PayoutBatch", "EarningEntry", "SystemSetting", "CrmContact", "CrmDeal", "Invoice", "EmailCampaign", "SocialPost", "ProjectTask" };

    private Task<int> AuditLogsAsync()
    {
        var entityIds = Enumerable.Range(0, 20000).Select(_ => Guid.NewGuid().ToString()).ToList();
        return InsertAsync(Enumerable.Range(0, N(200_000)).Select(_ =>
        {
            var at = Ago(730);
            var system = _rng.Next(10) == 0;
            return new AuditLog
            {
                CreatedAt = at,
                ActorUserId = system ? null : Skewed(_staff.Count > 0 ? _staff : _users),
                ActorType = system ? "system" : "Admin",
                Action = Pick(AuditActions),
                EntityType = Pick(EntityTypes),
                EntityId = Skewed(entityIds),
                AfterJson = "{\"status\":\"Approved\",\"amount\":12.5}",
                Reason = _rng.Next(4) == 0 ? "Synthetic volume row" : null,
                IpAddress = "203.0.113." + _rng.Next(1, 255),
                CorrelationId = Guid.NewGuid().ToString("N"),
            };
        }).OrderBy(a => a.CreatedAt));
    }

    // ------------------------------------------------------------------ notifications + outbox, job runs, tokens

    private async Task<int> NotificationsAsync()
    {
        var types = new[] { NotificationTypes.SubmissionDecision, NotificationTypes.EarningApproved, NotificationTypes.CampaignAlert,
            NotificationTypes.ReviewLiveCheckDue, NotificationTypes.PayoutPaid, "website.inquiry", NotificationTypes.SupportReply };
        var notifications = new List<Notification>();
        var deliveries = new List<NotificationDelivery>();
        for (var i = 0; i < N(100_000); i++)
        {
            var at = Ago(365);
            var n = new Notification
            {
                Id = IdAt(at), UserId = Skewed(_users), Type = Pick(types), Title = "Synthetic notification", Body = "Body text",
                LinkUrl = "/app/x/" + _rng.Next(50_000), CreatedAt = at, ReadAt = _rng.Next(10) < 7 ? at.AddHours(_rng.Next(1, 72)) : null,
            };
            notifications.Add(n);
            if (_rng.Next(10) < 3)
                deliveries.Add(new NotificationDelivery
                {
                    Id = IdAt(at), NotificationId = n.Id, UserId = n.UserId, Channel = NotificationChannel.Email,
                    Status = _rng.Next(100) == 0 ? DeliveryStatus.Pending : DeliveryStatus.Sent, Attempts = 1, NextAttemptAt = at,
                    CreatedAt = at, SentAt = at.AddSeconds(30),
                });
        }
        return await InsertAsync(notifications.OrderBy(n => n.CreatedAt)) + await InsertAsync(deliveries.OrderBy(d => d.CreatedAt));
    }

    private Task<int> JobRunsAsync()
    {
        var jobs = new[] { "NotificationDispatchJob", "CampaignSendJob", "SubscriberImportJob", "SeoAuditJob", "CampaignScheduleJob",
            "SocialPublishingJob", "AutomationJob", "FormEmailDispatchJob", "BlogSchedulerJob", "RetentionJob", "PayoutPreparationJob",
            "DeliverableSlaJob", "CrmTaskReminderJob", "InvoiceOverdueJob", "RecurringInvoiceJob" };
        return InsertAsync(Enumerable.Range(0, N(150_000)).Select(i =>
        {
            var at = Ago(90);
            return new JobRun
            {
                Id = IdAt(at), JobName = Skewed(jobs), RunKey = $"{at:yyyyMMddHHmmssfff}-{i:D8}", Status = _rng.Next(200) == 0 ? JobRunStatus.Failed : JobRunStatus.Succeeded,
                Attempt = 1, StartedAt = at, FinishedAt = at.AddMilliseconds(_rng.Next(5, 900)), Summary = "sent 0", InstanceId = "bench:1",
            };
        }).OrderBy(r => r.StartedAt));
    }

    private async Task<int> TokensAsync()
    {
        var refresh = Enumerable.Range(0, N(50_000)).Select(i =>
        {
            var at = Ago(120);
            return new RefreshToken
            {
                Id = IdAt(at), UserId = Skewed(_users), TokenHash = Hash("rt" + i), FamilyId = Guid.NewGuid(), CreatedAt = at,
                ExpiresAt = at.AddDays(14), RevokedAt = _rng.Next(3) == 0 ? at.AddHours(1) : null, RevokedReason = null,
            };
        }).OrderBy(t => t.CreatedAt).ToList();
        var tokens = Enumerable.Range(0, N(20_000)).Select(i =>
        {
            var at = Ago(120);
            return new UserToken
            {
                Id = IdAt(at), UserId = Pick(_users), Purpose = i % 3 == 0 ? UserTokenPurpose.PasswordReset : UserTokenPurpose.EmailVerification,
                TokenHash = Hash("ut" + i), CreatedAt = at, ExpiresAt = at.AddDays(1), UsedAt = _rng.Next(2) == 0 ? at.AddMinutes(10) : null,
            };
        }).OrderBy(t => t.CreatedAt).ToList();
        return await InsertAsync(refresh) + await InsertAsync(tokens);
    }

    // ------------------------------------------------------------------ email marketing

    private async Task<int> EmailAsync()
    {
        var client = _clients[0];
        var scope = Workspace.Key(client);
        var subscribers = Enumerable.Range(0, N(30_000)).Select(i =>
        {
            var at = Ago(700);
            return new Subscriber
            {
                Id = IdAt(at), ClientAccountId = client, ScopeKey = scope, Email = $"bench{i}@example.com", NormalizedEmail = $"bench{i}@example.com",
                FirstName = "Bench", LastName = "Subscriber" + i, Source = "import", CreatedAt = at,
                Status = _rng.Next(20) == 0 ? SubscriberStatus.Unsubscribed : SubscriberStatus.Subscribed,
                EmailConsent = EmailConsent.Granted, EmailConsentAt = at, LastOpenAt = _rng.Next(3) == 0 ? Ago(60) : null,
            };
        }).OrderBy(s => s.CreatedAt).ToList();
        var rows = await InsertAsync(subscribers);

        var campaignCount = 10;
        var perCampaign = N(100_000) / campaignCount;
        for (var c = 0; c < campaignCount; c++)
        {
            var started = _now.AddDays(-(c * 9 + 2));
            var campaign = new EmailCampaign
            {
                Id = IdAt(started), ClientAccountId = client, ScopeKey = scope, Name = $"Bench campaign {c}", Subject = "Hello",
                Status = EmailCampaignStatus.Sent, CreatedAt = started.AddDays(-1), SendStartedAt = started, ExpandedAt = started,
                CompletedAt = started.AddHours(3), RecipientCount = perCampaign,
            };
            db.Add(campaign);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var recipients = new List<CampaignRecipient>();
            var events = new List<EngagementEvent>();
            foreach (var s in subscribers.OrderBy(_ => _rng.Next()).Take(perCampaign))
            {
                var sentAt = started.AddSeconds(_rng.Next(0, 10_800));
                var opened = _rng.Next(10) < 3;
                var clicked = opened && _rng.Next(10) < 3;
                var r = new CampaignRecipient
                {
                    Id = IdAt(sentAt), CampaignId = campaign.Id, ClientAccountId = client, SubscriberId = s.Id, Channel = MessageChannel.Email,
                    Address = s.Email!, Status = RecipientStatus.Sent, DueAt = started, Attempts = 1, CreatedAt = started, SentAt = sentAt,
                    ProviderMessageId = "pm-" + Guid.NewGuid().ToString("N"), OpenedAt = opened ? sentAt.AddHours(2) : null, OpenCount = opened ? 1 : 0,
                    ClickedAt = clicked ? sentAt.AddHours(3) : null, ClickCount = clicked ? 1 : 0,
                };
                recipients.Add(r);
                events.Add(Event(client, s.Id, campaign.Id, r.Id, EngagementType.Sent, sentAt));
                if (opened) events.Add(Event(client, s.Id, campaign.Id, r.Id, EngagementType.Open, sentAt.AddHours(2)));
                if (clicked) events.Add(Event(client, s.Id, campaign.Id, r.Id, EngagementType.Click, sentAt.AddHours(3)));
            }
            rows += await InsertAsync(recipients.OrderBy(r => r.SentAt)) + await InsertAsync(events.OrderBy(e => e.OccurredAt));
        }
        return rows + campaignCount;
    }

    private EngagementEvent Event(Guid client, Guid subscriber, Guid campaign, Guid recipient, EngagementType type, DateTime at) => new()
    {
        Id = IdAt(at), ClientAccountId = client, SubscriberId = subscriber, CampaignId = campaign, RecipientId = recipient, Type = type,
        OccurredAt = at, Device = type == EngagementType.Sent ? DeviceType.Unknown : (DeviceType)_rng.Next(1, 4),
        DedupKey = $"bench:{type}:{recipient:N}",
    };

    // ------------------------------------------------------------------ participant submissions

    private async Task<int> SubmissionsAsync()
    {
        var campaigns = await db.Set<Campaign>().Select(c => new { c.Id }).ToListAsync();
        var ruleSets = await db.Set<RewardRuleSet>().Select(r => new { r.Id, r.CampaignId, r.Version }).ToListAsync();
        var accounts = await db.Set<SocialAccount>().Select(a => new { a.Id, a.UserId, a.Platform }).ToListAsync();
        var byCampaign = ruleSets.GroupBy(r => r.CampaignId).ToDictionary(g => g.Key, g => g.First());
        var usable = campaigns.Where(c => byCampaign.ContainsKey(c.Id)).Select(c => c.Id).ToList();
        var statuses = new[] { SubmissionStatus.Approved, SubmissionStatus.Approved, SubmissionStatus.Approved, SubmissionStatus.Rejected,
            SubmissionStatus.Pending, SubmissionStatus.NeedsCorrection, SubmissionStatus.UnderReview, SubmissionStatus.Reversed };

        var submissions = new List<Submission>();
        var events = new List<SubmissionEvent>();
        for (var i = 0; i < N(50_000); i++)
        {
            var account = Skewed(accounts);
            var campaign = Pick(usable);
            var rules = byCampaign[campaign];
            var at = Ago(540);
            var status = Pick(statuses);
            var decided = status is SubmissionStatus.Pending or SubmissionStatus.UnderReview ? (DateTime?)null : at.AddHours(_rng.Next(1, 96));
            var reviewer = decided is null ? (Guid?)null : Skewed(_staff);
            var s = new Submission
            {
                Id = IdAt(at), CampaignId = campaign, UserId = account.UserId, SocialAccountId = account.Id, Platform = account.Platform,
                PostUrl = $"https://example.com/p/bench{i}", NormalizedPostUrl = $"example.com/p/bench{i}", PostedAt = at.AddHours(-2),
                ContentHash = Hash("content" + _rng.Next(40_000)), Status = status, SubmittedAt = at, CreatedAt = at, DecidedAt = decided,
                DecidedByUserId = reviewer, RewardRuleSetId = rules.Id, RewardRuleSetVersion = rules.Version, EstimatedRewardAmount = 12.5m,
                RewardCurrency = "USD", RiskScore = _rng.Next(0, 100),
                LiveCheckStatus = status == SubmissionStatus.Approved && _rng.Next(4) == 0 ? LiveCheckStatus.Pending : LiveCheckStatus.NotRequired,
                LiveCheckDueAt = status == SubmissionStatus.Approved ? decided!.Value.AddDays(7) : null,
            };
            submissions.Add(s);
            events.Add(new SubmissionEvent { Id = IdAt(at), SubmissionId = s.Id, ToStatus = SubmissionStatus.Pending, Action = "submitted", ActorUserId = s.UserId, CreatedAt = at });
            if (decided is { } d)
            {
                events.Add(new SubmissionEvent { Id = IdAt(d.AddMinutes(-5)), SubmissionId = s.Id, FromStatus = SubmissionStatus.Pending, ToStatus = SubmissionStatus.UnderReview, Action = "claimed", ActorUserId = reviewer, CreatedAt = d.AddMinutes(-5) });
                events.Add(new SubmissionEvent { Id = IdAt(d), SubmissionId = s.Id, FromStatus = SubmissionStatus.UnderReview, ToStatus = status, Action = status == SubmissionStatus.Approved ? "approved" : "rejected", ActorUserId = reviewer, CreatedAt = d });
            }
        }
        return await InsertAsync(submissions.OrderBy(s => s.SubmittedAt)) + await InsertAsync(events.OrderBy(e => e.CreatedAt));
    }

    // ------------------------------------------------------------------ landing pages, forms, tracking

    private async Task<int> LandingAsync()
    {
        var forms = await db.Set<Form>().Select(f => new { f.Id, f.ClientAccountId }).ToListAsync();
        var pages = await db.Set<LandingPage>().Select(p => new { p.Id, p.ClientAccountId }).ToListAsync();
        var versions = await db.Set<LandingPageVersion>().Select(v => new { v.Id, v.PageId }).ToListAsync();
        var ips = Enumerable.Range(0, 20_000).Select(i => Hash("ip" + i)).ToList();
        var submissions = Enumerable.Range(0, N(50_000)).Select(i =>
        {
            var form = Skewed(forms);
            var page = pages.FirstOrDefault(p => p.ClientAccountId == form.ClientAccountId);
            var at = Ago(365);
            return new FormSubmission
            {
                Id = IdAt(at), FormId = form.Id, ClientAccountId = form.ClientAccountId, LandingPageId = page?.Id, DataJson = "{\"message\":\"hi\"}",
                Email = $"lead{i}@example.com", Name = "Lead " + i, IpHash = Skewed(ips), SubmittedAt = at, EventPublishedAt = at,
                Status = _rng.Next(20) == 0 ? FormSubmissionStatus.Spam : FormSubmissionStatus.New, ConsentGiven = true,
            };
        }).OrderBy(s => s.SubmittedAt).ToList();
        var views = Enumerable.Range(0, N(100_000)).Select(_ =>
        {
            var page = Skewed(pages);
            var at = Ago(365);
            return new LandingPageView
            {
                Id = IdAt(at), PageId = page.Id, ClientAccountId = page.ClientAccountId,
                VersionId = versions.FirstOrDefault(v => v.PageId == page.Id)?.Id ?? Guid.NewGuid(), VisitorHash = Skewed(ips),
                ViewedAt = at, IsUnique = _rng.Next(3) == 0, ReferrerHost = "google.com",
            };
        }).OrderBy(v => v.ViewedAt).ToList();
        return await InsertAsync(submissions) + await InsertAsync(views);
    }

    private async Task<int> ClicksAsync()
    {
        var links = await db.Set<TrackingLink>().Select(l => l.Id).ToListAsync();
        return await InsertAsync(Enumerable.Range(0, N(100_000)).Select(i =>
        {
            var at = Ago(365);
            return new TrackingClick
            {
                Id = IdAt(at), TrackingLinkId = Skewed(links), ClickedAt = at, VisitorHash = Hash("v" + _rng.Next(30_000)),
                IsUnique = _rng.Next(3) == 0, IsSuspectedBot = _rng.Next(15) == 0, Referrer = "https://instagram.com/",
            };
        }).OrderBy(c => c.ClickedAt));
    }

    // ------------------------------------------------------------------ delivery, CRM, metrics

    private async Task<int> DeliveryAsync()
    {
        var projects = await db.Set<Project>().Select(p => new { p.Id, p.ClientAccountId }).ToListAsync();
        var threads = await db.Set<MessageThread>().Select(t => new { t.Id, t.ClientAccountId }).ToListAsync();
        var staff = _staff.Count > 0 ? _staff : _users;
        var entries = Enumerable.Range(0, N(50_000)).Select(_ =>
        {
            var p = Skewed(projects);
            var at = Ago(540);
            return new TimeEntry
            {
                Id = IdAt(at), UserId = Pick(staff), ClientAccountId = p.ClientAccountId, ProjectId = p.Id, Date = DateOnly.FromDateTime(at),
                Minutes = _rng.Next(15, 240), Billable = _rng.Next(4) != 0, CreatedAt = at, Note = "Synthetic",
            };
        }).OrderBy(e => e.CreatedAt).ToList();
        var messages = threads.Count == 0 ? new List<ThreadMessage>() : Enumerable.Range(0, N(20_000)).Select(_ =>
        {
            var t = Skewed(threads);
            var at = Ago(540);
            return new ThreadMessage { Id = IdAt(at), ThreadId = t.Id, ClientAccountId = t.ClientAccountId, AuthorUserId = Pick(staff), Body = "Synthetic message", CreatedAt = at };
        }).OrderBy(m => m.CreatedAt).ToList();
        return await InsertAsync(entries) + await InsertAsync(messages);
    }

    private Task<int> CrmAsync() => InsertAsync(Enumerable.Range(0, N(20_000)).Select(i =>
    {
        var at = Ago(700);
        return new CrmContact
        {
            Id = IdAt(at), FirstName = "Contact", LastName = "No" + i, Email = $"crm{i}@example.com", NormalizedEmail = $"crm{i}@example.com",
            LifecycleStage = (LifecycleStage)_rng.Next(0, 7), OwnerUserId = Skewed(_staff.Count > 0 ? _staff : _users), CreatedAt = at,
            Score = _rng.Next(0, 100),
        };
    }).OrderBy(c => c.CreatedAt));

    private async Task<int> MetricsAsync()
    {
        var accounts = await db.Set<AdAccount>().Select(a => new { a.Id, a.ClientAccountId, a.Platform, a.Currency }).ToListAsync();
        var profiles = await db.Set<BrandProfile>().Select(p => new { p.Id, p.ClientAccountId, p.Network }).ToListAsync();
        var rows = 0;
        if (accounts.Count > 0)
        {
            var perAccount = N(50_000) / accounts.Count;
            var ads = new List<AdDailyMetric>();
            foreach (var a in accounts)
                for (var i = 0; i < perAccount; i++)
                {
                    var date = DateOnly.FromDateTime(_now).AddDays(-(i / 20));
                    ads.Add(new AdDailyMetric
                    {
                        AdAccountId = a.Id, ClientAccountId = a.ClientAccountId, Platform = a.Platform, Currency = a.Currency, Date = date,
                        Level = i % 20 < 4 ? AdLevel.Campaign : i % 20 < 10 ? AdLevel.AdGroup : AdLevel.Ad, EntityKey = $"bench-{i % 20}",
                        EntityName = $"Bench entity {i % 20}", Impressions = _rng.Next(100, 10_000), Clicks = _rng.Next(0, 300),
                        Spend = _rng.Next(100, 50_000) / 100m, Source = AdMetricSource.CsvImport, UpdatedAt = _now,
                    });
                }
            rows += await InsertAsync(ads);
        }
        if (profiles.Count > 0)
        {
            var perProfile = N(30_000) / profiles.Count;
            var metrics = new List<SocialPostMetric>();
            foreach (var p in profiles)
                for (var i = 0; i < perProfile; i++)
                    metrics.Add(new SocialPostMetric
                    {
                        ClientAccountId = p.ClientAccountId, ProfileId = p.Id, Network = p.Network, PostKey = $"bench-post-{i % 60}",
                        Date = DateOnly.FromDateTime(_now).AddDays(-(i / 60)), Impressions = _rng.Next(100, 10_000), Reach = _rng.Next(50, 5000),
                        Engagements = _rng.Next(0, 500), Clicks = _rng.Next(0, 100), Source = MetricSource.PlatformExport, UpdatedAt = _now,
                    });
            rows += await InsertAsync(metrics);
        }
        return rows;
    }
}
