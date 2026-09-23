using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Marketing.Achievements;
using OptimizeAll.Api.Modules.Marketing.Tracking;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;

namespace OptimizeAll.Api.Modules.Seed;

internal sealed partial class DemoRun
{
    private const string UrlSafe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string TicketAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    // ------------------------------------------------------------------ experiments

    private async Task CreateExperimentsAsync(CancellationToken ct)
    {
        var nimbus = C("nimbus");
        var running = new Experiment
        {
            CampaignId = nimbus.Id,
            Name = "Campaign title: benefit vs. challenge",
            Hypothesis = "Framing the campaign as a 30-day challenge increases the share of participants who submit a post.",
            Element = ExperimentElement.Title,
            Status = ExperimentStatus.Running,
            StartedAt = Day(-20, 9),
            CreatedByUserId = Manager.Id,
            CreatedAt = Day(-21, 15),
        };
        var runningVariants = new List<ExperimentVariant>
        {
            new() { ExperimentId = running.Id, Key = "A", Name = "Control (benefit)", Weight = 50, Title = nimbus.Title },
            new() { ExperimentId = running.Id, Key = "B", Name = "Challenge framing", Weight = 50, Title = "Take the 30-day Nimbus challenge — and get paid to share it" },
        };
        running.Variants.AddRange(runningVariants);

        var leaf = C("leaf");
        var completed = new Experiment
        {
            CampaignId = leaf.Id,
            Name = "Landing page: savings goals vs. bill reminders",
            Hypothesis = "Leading with bill reminders converts better than savings goals for first-job audiences.",
            Element = ExperimentElement.LandingPage,
            Status = ExperimentStatus.Completed,
            StartedAt = leaf.Campaign.StartsAt.AddDays(3),
            EndedAt = Day(-14, 12),
            CreatedByUserId = Manager.Id,
            CreatedAt = leaf.Campaign.StartsAt.AddDays(2),
        };
        var completedVariants = new List<ExperimentVariant>
        {
            new() { ExperimentId = completed.Id, Key = "A", Name = "Savings goals", Weight = 50, LandingHeadline = "Hit your first savings goal", LandingBody = "Set a goal, round up your spending and watch it grow." },
            new() { ExperimentId = completed.Id, Key = "B", Name = "Bill reminders", Weight = 50, LandingHeadline = "Never miss a bill again", LandingBody = "LedgerLeaf reminds you before every bill is due." },
        };
        completed.Variants.AddRange(completedVariants);
        completed.WinningVariantId = completedVariants[1].Id;

        _db.Set<Experiment>().AddRange(running, completed);
        foreach (var (experiment, variants) in new[] { (running, runningVariants), (completed, completedVariants) })
        {
            _experimentsByCampaign[experiment.CampaignId] = (experiment, variants);
            _clock.Now = experiment.CreatedAt;
            _audit.As(Manager.Id, Role.CampaignManager).Record("experiment.created", nameof(Experiment), experiment.Id,
                after: new { experiment.Name, experiment.Element, Variants = variants.Select(v => v.Key) });
            _clock.Now = experiment.StartedAt!.Value;
            _audit.Record("experiment.started", nameof(Experiment), experiment.Id, new { Status = "Draft" }, new { Status = "Running" });
            if (experiment.EndedAt is { } ended)
            {
                _clock.Now = ended;
                _audit.Record("experiment.completed", nameof(Experiment), experiment.Id, new { Status = "Running" },
                    new { Status = "Completed", experiment.WinningVariantId }, "Variant B converted 31% better on verified sign-ups.");
            }

            // Anonymous landing-page visitors are assigned too (sticky by hashed visitor id).
            var end = experiment.EndedAt ?? _now;
            for (var i = 0; i < 40; i++)
            {
                var subject = VariantAssigner.VisitorSubject(_rng.Hash64());
                var key = VariantAssigner.Assign(experiment.Id, subject, variants.Select(v => new WeightedVariant(v.Key, v.Weight)).ToList());
                _db.Set<ExperimentAssignment>().Add(new ExperimentAssignment
                {
                    ExperimentId = experiment.Id, VariantId = variants.First(v => v.Key == key).Id, SubjectKey = subject,
                    AssignedAt = _rng.Between(experiment.StartedAt!.Value, end),
                });
                Count("experiment assignments");
            }
            Count("experiments");
        }
        await SaveAsync(ct);
    }

    // ------------------------------------------------------------------ tracking, invitations, templates, calendar

    private async Task CreateGrowthDataAsync(CancellationToken ct)
    {
        await CreateTrackingAsync(ct);

        var invitations = new (string Name, DemoCampaign? Campaign, string Source, string Medium, int? MaxUses, int Uses, int Visits, int? ExpiresInDays, bool Active, int CreatedDaysAgo)[]
        {
            ("Aurora Pro creators circle — VIP invites", C("aurora"), "email", "invite", 25, 6, 41, 30, true, 21),
            ("Nimbus launch — Instagram bio link", C("nimbus"), "instagram", "bio", null, 9, 230, null, true, 55),
            ("Karachi university ambassadors", null, "campus", "ambassador", 200, 12, 180, 60, true, 40),
            ("Summer creator meetup (ended)", null, "event", "qr", 100, 34, 96, -20, false, 90),
        };
        foreach (var i in invitations)
        {
            var created = _now.AddDays(-i.CreatedDaysAgo);
            var link = new InvitationLink
            {
                Code = _rng.Chars(UrlSafe, 8),
                CampaignId = i.Campaign?.Id,
                Name = i.Name,
                UtmSource = i.Source,
                UtmMedium = i.Medium,
                UtmCampaign = i.Campaign?.Campaign.UtmCampaign ?? i.Campaign?.Campaign.Slug ?? "platform-growth",
                ExpiresAt = i.ExpiresInDays is { } d ? _now.AddDays(d) : null,
                MaxUses = i.MaxUses,
                UseCount = i.Uses,
                VisitCount = i.Visits,
                IsActive = i.Active,
                CreatedByUserId = Manager.Id,
                CreatedAt = created,
            };
            _db.Set<InvitationLink>().Add(link);
            _clock.Now = created;
            _audit.As(Manager.Id, Role.CampaignManager).Record("invitation.created", nameof(InvitationLink), link.Id,
                after: new { link.Name, link.CampaignId, link.MaxUses, link.ExpiresAt });
            Count("invitation links");
        }

        var templates = new (string Name, SocialPlatform? Platform, string Body, string? Hashtags, string Lang, bool Archived)[]
        {
            ("Launch announcement (Instagram)", SocialPlatform.Instagram,
                "Big news: {brand} is live! I've been trying it for a week and here's what I love: {benefit}. Link in bio.", "#ad #partner", "en", false),
            ("TikTok hook — 3 reasons", SocialPlatform.TikTok,
                "3 reasons I switched to {brand} 👇 1) {reason1} 2) {reason2} 3) {reason3}", "#ad #fyp", "en", false),
            ("إعلان منتج (عربي)", null, "جربت {brand} هذا الأسبوع وهذه رأيي بصراحة: {benefit}. الرابط في البايو.", "#إعلان", "ar", false),
            ("Old giveaway template", SocialPlatform.Instagram, "Giveaway time! Follow {brand} and tag two friends.", "#giveaway", "en", true),
        };
        foreach (var t in templates)
        {
            var created = _now.AddDays(-_rng.Next(30, 90));
            var template = new PostTemplate
            {
                Name = t.Name, Platform = t.Platform, Body = t.Body, Hashtags = t.Hashtags, LanguageCode = t.Lang, IsArchived = t.Archived,
                CreatedByUserId = Manager.Id, CreatedAt = created,
            };
            _db.Set<PostTemplate>().Add(template);
            _clock.Now = created;
            _audit.As(Manager.Id, Role.CampaignManager).Record("template.created", nameof(PostTemplate), template.Id, after: new { template.Name, template.Platform });
            Count("post templates");
        }

        var calendar = new (string Title, DemoCampaign? Campaign, SocialPlatform? Platform, DateTime At, CalendarEntryStatus Status, string? Notes)[]
        {
            ("Weekend push reminder to Nimbus participants", C("nimbus"), SocialPlatform.Instagram, Day(-11, 8), CalendarEntryStatus.Published, "Paired with the weekend push bonus in reward rules v2."),
            ("Orbit Arena season 3 launch-day posts", C("orbit"), SocialPlatform.YouTube, Day(5, 12), CalendarEntryStatus.Scheduled, "Trailer goes live at 12:00 UTC; launch-day bonus active for 24h."),
            ("Desert Bloom Arabic caption refresh", C("bloom"), null, Day(7, 9), CalendarEntryStatus.Planned, "Swap in the new Arabic caption from the client."),
            ("CodeSprout coding week kick-off", C("sprout"), SocialPlatform.Facebook, Day(21, 7), CalendarEntryStatus.Planned, "Pending client creative — campaign still in draft."),
            ("Wanderly autumn destinations reel", C("wander"), SocialPlatform.TikTok, Day(-2, 10), CalendarEntryStatus.Cancelled, "Cancelled: campaign paused by the client."),
            ("Karachi Eats final weekend countdown", C("eats"), SocialPlatform.Instagram, Day(8, 14), CalendarEntryStatus.Scheduled, null),
        };
        foreach (var e in calendar)
        {
            var created = new[] { e.At.AddDays(-10), _now.AddDays(-1) }.Min();
            _db.Set<ContentCalendarEntry>().Add(new ContentCalendarEntry
            {
                CampaignId = e.Campaign?.Id, Title = e.Title, Platform = e.Platform, ScheduledFor = e.At, Status = e.Status, Notes = e.Notes,
                CreatedByUserId = Manager.Id, CreatedAt = created,
            });
            Count("calendar entries");
        }
        await SaveAsync(ct);
    }

    /// <summary>Per-participant tracking links (as TrackingController creates them) with measured clicks and advertiser conversions.</summary>
    private async Task CreateTrackingAsync(CancellationToken ct)
    {
        foreach (var campaign in new[] { C("nimbus"), C("eats"), C("leaf") })
        {
            var c = campaign.Campaign;
            var firstByUser = await _db.Set<Submission>().AsNoTracking().Where(s => s.CampaignId == c.Id)
                .GroupBy(s => s.UserId).Select(g => new { UserId = g.Key, First = g.Min(s => s.SubmittedAt) }).ToListAsync(ct);
            var links = new List<TrackingLink>();
            foreach (var row in firstByUser.OrderBy(r => r.First))
            {
                var person = _participants.First(p => p.Id == row.UserId);
                var link = new TrackingLink
                {
                    Code = _rng.Chars(UrlSafe, 10),
                    CampaignId = c.Id,
                    UserId = row.UserId,
                    DestinationUrl = c.TrackingDestinationUrl!,
                    UtmSource = TrackingService.UtmSource,
                    UtmMedium = TrackingService.UtmMedium,
                    UtmCampaign = string.IsNullOrWhiteSpace(c.UtmCampaign) ? c.Slug : c.UtmCampaign,
                    UtmContent = person.User.ReferralCode,
                    CreatedAt = row.First.AddHours(-_rng.Next(1, 12)),
                };
                links.Add(link);
                _db.Set<TrackingLink>().Add(link);
                Count("tracking links");
            }

            var clicksUntil = new[] { c.SubmissionDeadline.AddDays(5), _now }.Min();
            var visitors = Enumerable.Range(0, 120).Select(_ => _rng.Hash64()).ToList();
            var referrers = new[] { "instagram.com", "l.instagram.com", "t.co", "l.facebook.com", "www.tiktok.com", "www.linkedin.com", null };
            foreach (var link in links)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var clicks = _rng.Next(4, 38);
                for (var i = 0; i < clicks; i++)
                {
                    var at = _rng.Between(link.CreatedAt.AddHours(1), clicksUntil);
                    var bot = _rng.Chance(0.08);
                    // The real redirect hashes IP + user agent + day, so repeat visits on the same day are not unique.
                    var visitor = _rng.Chance(0.3) && seen.Count > 0 ? seen.First() : $"{_rng.Pick(visitors)}|{at:yyyy-MM-dd}";
                    var hash = Normalization.Sha256Hex(visitor);
                    _db.Set<TrackingClick>().Add(new TrackingClick
                    {
                        TrackingLinkId = link.Id,
                        ClickedAt = at,
                        VisitorHash = hash,
                        Referrer = bot ? null : _rng.Pick(referrers),
                        IsUnique = seen.Add(visitor),
                        IsSuspectedBot = bot,
                    });
                    Count(bot ? "tracking clicks (bot)" : "tracking clicks");
                }
            }

            // Server-to-server conversions reported by the advertiser; only signature-verified ones count.
            var conversionCount = campaign.Key switch { "leaf" => 9, "nimbus" => 6, _ => 3 };
            for (var i = 0; i < conversionCount && links.Count > 0; i++)
            {
                var link = _rng.Pick(links);
                var occurred = _rng.Between(link.CreatedAt.AddHours(2), clicksUntil);
                var verified = i != conversionCount - 1; // the last one never passed the HMAC check
                _db.Set<TrackingConversion>().Add(new TrackingConversion
                {
                    TrackingLinkId = link.Id,
                    ExternalReference = $"{campaign.Key.ToUpperInvariant()}-{_rng.Chars(TicketAlphabet, 8)}",
                    Value = campaign.Key switch { "leaf" => 4.99m, "nimbus" => 9.99m, _ => 1_500m },
                    Currency = campaign.Key == "eats" ? "PKR" : "USD",
                    OccurredAt = occurred,
                    ReceivedAt = occurred.AddMinutes(_rng.Next(1, 30)),
                    VerifiedAt = verified ? occurred.AddMinutes(_rng.Next(1, 30)) : null,
                    Source = "postback",
                });
                Count(verified ? "verified conversions" : "unverified conversions");
            }
        }
        await SaveAsync(ct);
    }

    // ------------------------------------------------------------------ content and support

    private async Task CreateContentAndSupportAsync(CancellationToken ct)
    {
        var banners = new (string Title, string Body, string? Cta, string? CtaUrl, ContentAudience Audience, string? Country, string? Lang, int? StartDays, int? EndDays, bool Active, int Sort)[]
        {
            ("Finish setting up your account", "Verify your email and add a social profile that's at least 90 days old to start earning.",
                "Continue setup", AppLinks.ParticipantHome, ContentAudience.Onboarding, null, null, null, null, true, 5),
            ("Nimbus Fitness launch is live", "Share your first workout and earn up to 8.50 USD per approved post.",
                "View campaign", AppLinks.Campaign("nimbus-fitness-app-launch"), ContentAudience.Eligible, null, null, -20, 20, true, 10),
            ("Invite-only campaigns for top creators", "Gold and Platinum creators can now join premium review campaigns like Aurora Pro.",
                "See campaigns", AppLinks.Campaigns, ContentAudience.ActiveEarners, null, null, -10, 30, true, 20),
            ("حملات جديدة في الإمارات", "حملة Desert Bloom متاحة الآن للمشاركين في الإمارات والسعودية.",
                "عرض الحملة", AppLinks.Campaign("desert-bloom-autumn-glow"), ContentAudience.Everyone, "AE", "ar", -25, 25, true, 15),
            ("We miss you — new campaigns match your interests", "Three new campaigns launched since your last visit.",
                "Browse campaigns", AppLinks.Campaigns, ContentAudience.Inactive, null, null, null, null, true, 30),
            ("Summer creator meetup", "Thanks to everyone who joined us in Dubai!", null, null, ContentAudience.Everyone, null, null, -90, -60, false, 40),
        };
        _clock.Now = _now.AddDays(-26);
        _audit.As(Admin.Id, Role.Admin);
        foreach (var b in banners)
        {
            var image = await StorePublicImageAsync(Admin.Id, FilePurpose.ContentImage, 1200, 400, "banner.png", ct);
            var banner = new HomepageBanner
            {
                Title = b.Title, Body = b.Body, CtaLabel = b.Cta, CtaUrl = b.CtaUrl, Audience = b.Audience, CountryCode = b.Country, LanguageCode = b.Lang,
                StartsAt = b.StartDays is { } s ? Day(s, 0) : null, EndsAt = b.EndDays is { } e ? Day(e, 0) : null, IsActive = b.Active, SortOrder = b.Sort,
                ImageUrl = FileUrls.For(image.Id),
                CreatedAt = _clock.Now,
            };
            _db.Set<HomepageBanner>().Add(banner);
            _audit.Record("content.banner_created", nameof(HomepageBanner), banner.Id, after: new { banner.Title, banner.Audience });
            Count("banners");
        }
        var announcements = new[]
        {
            new Announcement
            {
                Title = "Payouts are biweekly", Severity = AnnouncementSeverity.Info, Audience = ContentAudience.Everyone,
                Body = "Approved earnings are paid every two weeks. Earnings become payable 3 days after approval and the minimum payout is 10 USD.",
                PublishAt = _now.AddDays(-12), CreatedAt = _now.AddDays(-12),
            },
            new Announcement
            {
                Title = "Scheduled maintenance on Saturday", Severity = AnnouncementSeverity.Warning, Audience = ContentAudience.Everyone,
                Body = "Submissions will be unavailable for about 30 minutes on Saturday at 02:00 UTC while we upgrade our servers.",
                PublishAt = _now.AddDays(-1), ExpiresAt = _now.AddDays(3), CreatedAt = _now.AddDays(-1),
            },
            new Announcement
            {
                Title = "Congratulations on your first approved post!", Severity = AnnouncementSeverity.Success, Audience = ContentAudience.ActiveEarners,
                Body = "Keep your posts public for at least 30 days so your rewards are never reversed.",
                PublishAt = _now.AddDays(-30), CreatedAt = _now.AddDays(-30),
            },
        };
        foreach (var a in announcements)
        {
            _db.Set<Announcement>().Add(a);
            _audit.Record("content.announcement_created", nameof(Announcement), a.Id, after: new { a.Title, a.Severity, a.Audience });
            Count("announcements");
        }

        // Support tickets across statuses, including staff-only notes.
        var sara = Sara;
        var s9 = await _db.Set<Submission>().AsNoTracking().Where(s => s.UserId == sara.Id && s.Status == SubmissionStatus.NeedsCorrection)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        AddTicket(sara, "When will my next payout arrive?", TicketCategory.Payout, TicketPriority.Normal, TicketStatus.Open, null, _now.AddDays(-1),
            (sara, "Hi! My earnings page shows an amount available for the next payout. Which date should I expect it?", false, 0));
        AddTicket(sara, "Question about the correction on my Wanderly post", TicketCategory.Submission, TicketPriority.Normal, TicketStatus.AwaitingParticipant,
            Reviewer1, _now.AddDays(-4),
            (sara, "The reviewer asked me to add a disclosure but the campaign is now paused. Can I still fix it?", false, 0),
            (Reviewer1, "Internal: campaign paused by the client — resubmission will be possible once it resumes.", true, 2),
            (Reviewer1, "Thanks Sara! The campaign is paused right now. As soon as it resumes you can edit the caption and resubmit from the submission page. Could you confirm you still have the post live?", false, 3))
            .SubmissionId = s9;
        var aisha = Person("hold.participant");
        AddTicket(aisha, "Why are my payouts paused?", TicketCategory.Payout, TicketPriority.High, TicketStatus.AwaitingStaff, Finance1, _now.AddDays(-9),
            (aisha, "My earnings page says my payouts are paused. I recently changed my bank account — is that why?", false, 0),
            (Finance1, "Internal: waiting for compliance to confirm the new account holder. Keep replies neutral; do not mention the review outcome.", true, 1));
        var hamza = Person("hamza.qureshi");
        AddTicket(hamza, "Dispute: reversal of my LedgerLeaf post", TicketCategory.Dispute, TicketPriority.Urgent, TicketStatus.Open, Reviewer2, _now.AddDays(-2),
            (hamza, "One of my LedgerLeaf posts was reversed after I was already paid, and the amount was deducted from my next payout. I did not buy followers.", false, 0),
            (Reviewer2, "Internal: engagement report shows 70% of likes from accounts created in the last week. Escalate to the campaign manager before replying.", true, 1));
        var jack = Person("suspended.participant");
        AddTicket(jack, "My account was suspended", TicketCategory.Account, TicketPriority.Normal, TicketStatus.Resolved, Admin, _now.AddDays(-8),
            (jack, "I can't log in anymore — why was my account suspended?", false, 0),
            (Admin, "Hi Jack, your account was suspended because several submissions used screenshots taken from other creators' posts. You can reply here with any evidence and we'll review it.", false, 1));
        await SaveAsync(ct);
    }

    private SupportTicket AddTicket(DemoPerson owner, string subject, TicketCategory category, TicketPriority priority, TicketStatus status,
        DemoPerson? assignee, DateTime created, params (DemoPerson Author, string Body, bool Internal, int HoursAfter)[] messages)
    {
        var ticket = new SupportTicket
        {
            Reference = NewTicketReference(),
            UserId = owner.Id,
            Subject = subject,
            Category = category,
            Priority = priority,
            Status = status,
            AssignedToUserId = assignee?.Id,
            CreatedAt = created,
            ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed ? created.AddHours(messages.Max(m => m.HoursAfter) + 1) : null,
        };
        foreach (var m in messages)
        {
            ticket.Messages.Add(new SupportMessage
            {
                TicketId = ticket.Id, AuthorUserId = m.Author.Id, Body = m.Body, IsInternalNote = m.Internal, CreatedAt = created.AddHours(m.HoursAfter),
            });
            if (m.Internal)
            {
                _clock.Now = created.AddHours(m.HoursAfter);
                _audit.As(m.Author.Id, m.Author.Role).Record("support.internal_note_added", nameof(SupportTicket), ticket.Id, after: new { ticket.Reference });
            }
        }
        _db.Set<SupportTicket>().Add(ticket);
        Count("support tickets");
        return ticket;
    }

    private string NewTicketReference() => "SUP-" + _rng.Chars(TicketAlphabet, 6);

    /// <summary>Sara reports a missing bonus; finance answers, adds an internal note and resolves it with a credit (next day).</summary>
    private async Task CreateSaraResolvedTicketAsync(SubPlan relatedSubmission)
    {
        var ticket = AddTicket(Sara, "Missing first-post bonus on Karachi Eats", TicketCategory.Payout, TicketPriority.Normal, TicketStatus.Resolved,
            Finance1, Now,
            (Sara, "My first Karachi Eats post was approved but I didn't see the first-post bonus in the estimate. Can you check?", false, 0),
            (Finance1, "Internal: estimate hid the bonus due to the pricing delay on day one. Goodwill credit of 2.50 USD approved by finance lead.", true, 20),
            (Finance1, "Thanks for flagging this, Sara. We've added a 2.50 USD goodwill credit to your earnings; it will be included in your next payout.", false, 24));
        ticket.SubmissionId = relatedSubmission.Created ? relatedSubmission.Id : null;
        _saraTicketReference = ticket.Reference;
        await Task.CompletedTask;
    }

    // ------------------------------------------------------------------ achievements and notifications

    /// <summary>Awards achievements from the same metrics the AchievementEvaluator uses, dated when each threshold was reached.</summary>
    private async Task AwardAchievementsAsync(CancellationToken ct)
    {
        var achievements = await _db.Set<Achievement>().AsNoTracking().Where(a => a.IsActive).ToListAsync(ct);
        if (achievements.Count == 0) return;
        var evaluator = new AchievementEvaluator(_db, _notifications, _clock);
        foreach (var person in _participants)
        {
            var metrics = await evaluator.MetricsAsync(person.Id, ct);
            var approvals = await _db.Set<Submission>().AsNoTracking()
                .Where(s => s.UserId == person.Id && s.Status == SubmissionStatus.Approved && s.DecidedAt != null)
                .OrderBy(s => s.DecidedAt).Select(s => new { s.DecidedAt, s.Platform, s.CampaignId }).ToListAsync(ct);
            foreach (var achievement in achievements.Where(a => metrics.TryGetValue(a.Criterion, out var v) && v >= a.Threshold))
            {
                DateTime? reached = achievement.Criterion switch
                {
                    AchievementCriterion.ApprovedSubmissions => approvals.ElementAtOrDefault((int)achievement.Threshold - 1)?.DecidedAt,
                    AchievementCriterion.PlatformsUsed => approvals.Select((a, i) => (a, i))
                        .FirstOrDefault(x => approvals.Take(x.i + 1).Select(y => y.Platform).Distinct().Count() >= achievement.Threshold).a?.DecidedAt,
                    AchievementCriterion.CampaignsCompleted => approvals.Select((a, i) => (a, i))
                        .FirstOrDefault(x => approvals.Take(x.i + 1).Select(y => y.CampaignId).Distinct().Count() >= achievement.Threshold).a?.DecidedAt,
                    _ => approvals.LastOrDefault()?.DecidedAt,
                };
                var awardedAt = (reached ?? _now.AddHours(-2)).AddMinutes(1);
                _db.Set<UserAchievement>().Add(new UserAchievement { UserId = person.Id, AchievementId = achievement.Id, AwardedAt = awardedAt });
                _db.Set<Notification>().Add(new Notification
                {
                    UserId = person.Id, Type = NotificationTypes.Achievement, Title = $"Achievement unlocked: {achievement.Name}",
                    Body = achievement.Description, LinkUrl = AppLinks.Achievements, CreatedAt = awardedAt,
                });
                Count("achievements awarded");
            }
        }
        await SaveAsync(ct);
    }

    /// <summary>A realistic read/unread mix: older notifications read, the last few days unread (Sara keeps a handful unread).</summary>
    private async Task FinishNotificationsAsync(CancellationToken ct)
    {
        var sara = Sara;
        _db.Set<Notification>().Add(new Notification
        {
            UserId = sara.Id, Type = NotificationTypes.CampaignAlert, Title = "New campaign for you: Aurora Pro creators circle",
            Body = "You've been invited to review the Aurora Pro headphones. Premium reward for Gold creators.",
            LinkUrl = AppLinks.Campaign("aurora-pro-creators-circle"), CreatedAt = _now.AddDays(-19),
        });
        await SaveAsync(ct);

        var participantIds = _participants.Select(p => p.Id).ToList();
        var rows = await _db.Set<Notification>().Where(n => participantIds.Contains(n.UserId) && n.ReadAt == null).ToListAsync(ct);
        var saraOldUnread = 0;
        foreach (var n in rows.OrderByDescending(n => n.CreatedAt))
        {
            var age = _now - n.CreatedAt;
            if (n.UserId == sara.Id)
            {
                if (age < TimeSpan.FromDays(3)) continue;
                if (saraOldUnread++ < 2) continue; // two older ones still unread
                n.ReadAt = n.CreatedAt.AddHours(3);
            }
            else if (age > TimeSpan.FromDays(4) && _rng.Chance(0.8))
            {
                n.ReadAt = n.CreatedAt.AddHours(_rng.Next(1, 30));
            }
            if (n.ReadAt > _now) n.ReadAt = _now.AddMinutes(-5);
        }
        Count("notifications", await _db.Set<Notification>().CountAsync(n => participantIds.Contains(n.UserId), ct));
        await SaveAsync(ct);
    }
}
