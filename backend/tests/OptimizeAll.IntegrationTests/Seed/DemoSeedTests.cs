using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Seed;

/// <summary>
/// Boots the API once with <c>Database:Seed = ["Baseline", "Demo"]</c> (the Development configuration) on a fresh
/// database: the base <see cref="ApiFactory"/> creates the database and runs Baseline, the derived host runs Baseline
/// again (idempotent) and then Demo.
/// </summary>
public sealed class DemoSeedFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public WebApplicationFactory<Program> Demo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Demo = Api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Seed:1"] = "Demo" })));
        await Demo.StartAsync(); // boots the host: migrations + Baseline + Demo
    }

    public async Task DisposeAsync()
    {
        await Demo.DisposeAsync();
        await Api.DisposeAsync();
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Demo.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<HttpResponseMessage> TryLoginAsync(string email, HttpClient client) =>
        await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = DemoAccounts.Password });

    public HttpClient NewClient()
    {
        var client = Demo.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = NewClient();
        var response = await TryLoginAsync(email, client);
        var body = await response.ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
}

public sealed class DemoSeedTests(DemoSeedFixture fx) : IClassFixture<DemoSeedFixture>
{
    private static readonly string[] StaffEmails =
    {
        DemoAccounts.Admin, DemoAccounts.Reviewer1, DemoAccounts.Reviewer2, DemoAccounts.Manager, DemoAccounts.Finance1, DemoAccounts.Finance2,
    };

    [Fact]
    public async Task Seed_runs_and_running_it_again_duplicates_nothing()
    {
        var before = await CountsAsync();
        Assert.True(before["users"] >= 45);
        Assert.True(before["submissions"] >= 120, $"only {before["submissions"]} submissions");
        Assert.True(await fx.WithDbAsync(db => DemoSeeder.IsSeededAsync(db)));

        using (var scope = fx.Demo.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DemoSeeder>();
            await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
            await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
        }

        var after = await CountsAsync();
        Assert.Equal(before, after);
    }

    private Task<Dictionary<string, int>> CountsAsync() => fx.WithDbAsync(async db => new Dictionary<string, int>
    {
        ["users"] = await db.Set<User>().CountAsync(),
        ["socialAccounts"] = await db.Set<SocialAccount>().CountAsync(),
        ["payoutProfiles"] = await db.Set<PayoutProfile>().CountAsync(),
        ["campaigns"] = await db.Set<OptimizeAll.Domain.Campaigns.Campaign>().CountAsync(),
        ["ruleSets"] = await db.Set<RewardRuleSet>().CountAsync(),
        ["submissions"] = await db.Set<Submission>().CountAsync(),
        ["submissionEvents"] = await db.Set<SubmissionEvent>().CountAsync(),
        ["files"] = await db.Set<StoredFile>().CountAsync(),
        ["earnings"] = await db.Set<EarningEntry>().CountAsync(),
        ["batches"] = await db.Set<PayoutBatch>().CountAsync(),
        ["items"] = await db.Set<PayoutItem>().CountAsync(),
        ["referrals"] = await db.Set<Referral>().CountAsync(),
        ["clicks"] = await db.Set<TrackingClick>().CountAsync(),
        ["notifications"] = await db.Set<Notification>().CountAsync(),
        ["tickets"] = await db.Set<SupportTicket>().CountAsync(),
        ["audit"] = await db.Set<AuditLog>().CountAsync(),
        ["rates"] = await db.Set<ExchangeRate>().CountAsync(),
    });

    [Fact]
    public async Task Demo_logins_work_through_the_api()
    {
        foreach (var email in StaffEmails.Concat(new[] { DemoAccounts.Sara, DemoAccounts.NewParticipant, DemoAccounts.Unverified, DemoAccounts.OnHold }))
        {
            var response = await fx.TryLoginAsync(email, fx.NewClient());
            Assert.True(response.IsSuccessStatusCode, $"{email}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
        await (await fx.TryLoginAsync(DemoAccounts.Suspended, fx.NewClient())).ShouldFailAsync(403, "account.suspended");

        var admin = await fx.LoginAsync(DemoAccounts.Admin);
        var me = await (await admin.GetAsync("/api/v1/me/profile")).ReadJsonAsync();
        Assert.Equal(DemoAccounts.Admin, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Reconciliation_is_balanced_for_every_seeded_batch()
    {
        var batches = await fx.WithDbAsync(db => db.Set<PayoutBatch>().AsNoTracking().OrderBy(b => b.CutoffAt).ToListAsync());
        Assert.Equal(4, batches.Count);
        Assert.Equal(2, batches.Count(b => b.Status == PayoutBatchStatus.Completed));
        var finance1 = await fx.WithDbAsync(db => db.Set<User>().Where(u => u.Email == DemoAccounts.Finance1).Select(u => u.Id).FirstAsync());
        // The latest batch is the draft for the last completed period (another test may already have finalized it).
        Assert.Equal(finance1, batches[3].PreparedByUserId);
        Assert.Contains(batches[3].Status, new[] { PayoutBatchStatus.Draft, PayoutBatchStatus.Finalized });
        Assert.Equal(PayoutBatchStatus.Finalized, batches[2].Status);
        var finalizedStatuses = await fx.WithDbAsync(db => db.Set<PayoutItem>().Where(i => i.BatchId == batches[2].Id)
            .Select(i => i.Status).Distinct().ToListAsync());
        Assert.Contains(PayoutItemStatus.Paid, finalizedStatuses);
        Assert.Contains(PayoutItemStatus.AwaitingPayment, finalizedStatuses);

        var client = await fx.LoginAsync(DemoAccounts.Finance1);
        foreach (var batch in batches)
        {
            var report = await (await client.GetAsync($"/api/v1/finance/payout-batches/{batch.Id}/reconciliation")).ReadJsonAsync();
            Assert.True(report.GetProperty("isBalanced").GetBoolean(),
                $"{batch.Reference} ({batch.Status}) not balanced: {report.GetProperty("discrepancies")}");
            if (batch.Status == PayoutBatchStatus.Completed)
                Assert.True(report.GetProperty("recordedPaid").GetDecimal() > 0);
        }
    }

    [Fact]
    public async Task Ledger_respects_constraints_and_production_conventions()
    {
        var entries = await fx.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().ToListAsync());
        Assert.NotEmpty(entries);

        foreach (var e in entries)
        {
            // Database check constraints (MySQL enforces them too; asserted explicitly here).
            Assert.True(e.ExchangeRate > 0);
            Assert.True((e.Amount >= 0 && e.SettlementAmount >= 0) || (e.Amount <= 0 && e.SettlementAmount <= 0), e.IdempotencyKey);
            if (e.Type == EarningType.Reversal) Assert.True(e.Amount < 0 && e.ReversesEntryId is not null, e.IdempotencyKey);
            if (e.Type is EarningType.Adjustment or EarningType.Reversal) Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.IdempotencyKey);

            // Settlement conversion and rounding exactly as the ledger writer does it.
            Assert.Equal("USD", e.SettlementCurrency);
            if (e.Type != EarningType.Reversal)
                Assert.Equal(Money.Convert(e.Amount, e.ExchangeRate, "USD"), e.SettlementAmount);
            if (e.Currency == "USD") Assert.Equal(1m, e.ExchangeRate);
            else Assert.NotNull(e.ExchangeRateId);

            // Lifecycle consistency.
            if (e.Status is EarningStatus.Scheduled or EarningStatus.Paid) Assert.NotNull(e.PayoutItemId);
            if (e.Status == EarningStatus.Paid) Assert.NotNull(e.PaidAt);
            if (e.Status is EarningStatus.Approved) Assert.NotNull(e.AvailableAt);

            // Idempotency key conventions of the real services.
            var expectedPrefix = e.Type switch
            {
                EarningType.Reversal => $"reversal:{e.ReversesEntryId}",
                EarningType.Adjustment => "adjustment:",
                EarningType.ReferralReward => $"referral:{e.ReferralId}",
                EarningType.FirstPostBonus => $"firstpost:{e.CampaignId}:{e.UserId}",
                _ => $"submission:{e.SubmissionId}:{e.Type}",
            };
            Assert.StartsWith(expectedPrefix, e.IdempotencyKey);
        }

        foreach (var status in Enum.GetValues<EarningStatus>())
            Assert.Contains(entries, e => e.Status == status);
        foreach (var type in Enum.GetValues<EarningType>())
            Assert.Contains(entries, e => e.Type == type);
        // Clawback: a paid earning reversed by a negative entry that was netted against a later payout.
        Assert.Contains(entries, e => e.Type == EarningType.Reversal && e.PayoutItemId != null &&
                                      entries.Any(o => o.Id == e.ReversesEntryId && o.Status == EarningStatus.Paid));
        Assert.Contains(entries, e => e.Currency == "AED");
        Assert.Contains(entries, e => e.Currency == "PKR");

        // Four-eyes on positive adjustments: created by finance1, approved by finance2 (one left pending for the demo).
        var (f1, f2) = await fx.WithDbAsync(async db => (
            await db.Set<User>().Where(u => u.Email == DemoAccounts.Finance1).Select(u => u.Id).FirstAsync(),
            await db.Set<User>().Where(u => u.Email == DemoAccounts.Finance2).Select(u => u.Id).FirstAsync()));
        var credits = entries.Where(e => e.Type == EarningType.Adjustment && e.Amount > 0).ToList();
        Assert.All(credits, e => Assert.Equal(f1, e.CreatedByUserId));
        Assert.All(credits.Where(e => e.Status != EarningStatus.PendingApproval), e => Assert.Equal(f2, e.ApprovedByUserId));
        Assert.Contains(credits, e => e.Status == EarningStatus.PendingApproval);

        // No batch was finalized by someone who prepared it, created/approved one of its earnings or benefits from it.
        var batches = await fx.WithDbAsync(db => db.Set<PayoutBatch>().AsNoTracking().ToListAsync());
        var items = await fx.WithDbAsync(db => db.Set<PayoutItem>().AsNoTracking().ToListAsync());
        foreach (var batch in batches)
        {
            var itemIds = items.Where(i => i.BatchId == batch.Id).Select(i => i.Id).ToHashSet();
            var involved = entries.Where(e => e.PayoutItemId is { } id && itemIds.Contains(id))
                .SelectMany(e => new[] { e.CreatedByUserId, e.ApprovedByUserId, e.UserId }).ToHashSet();
            if (batch.FinalizedByUserId is { } finalizer)
            {
                Assert.NotEqual(batch.PreparedByUserId, finalizer);
                Assert.DoesNotContain(finalizer, involved);
            }
            if (batch.Status == PayoutBatchStatus.Draft) Assert.DoesNotContain(f2, involved); // finance2 can finalize the draft
        }

        // Reversals reference their entry both ways.
        var byId = entries.ToDictionary(e => e.Id);
        foreach (var reversal in entries.Where(e => e.Type == EarningType.Reversal))
            Assert.Equal(reversal.Id, byId[reversal.ReversesEntryId!.Value].ReversedByEntryId);

        // SQLite stores decimals as TEXT: compare them numerically; it has LENGTH instead of CHAR_LENGTH.
        var (amount, settlement, rate, length) = ApiFactory.IsSqlite
            ? ("CAST(Amount AS REAL)", "CAST(SettlementAmount AS REAL)", "CAST(ExchangeRate AS REAL)", "LENGTH")
            : ("`Amount`", "`SettlementAmount`", "`ExchangeRate`", "CHAR_LENGTH");
        var violations = await fx.WithDbAsync(db => db.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(*) AS Value FROM earning_entries WHERE {rate} <= 0 " +
            $"OR NOT (({amount} >= 0 AND {settlement} >= 0) OR ({amount} <= 0 AND {settlement} <= 0)) " +
            $"OR (Type = 'Reversal' AND ({amount} >= 0 OR ReversesEntryId IS NULL)) " +
            $"OR (Type IN ('Adjustment','Reversal') AND (Reason IS NULL OR {length}(Reason) = 0))").SingleAsync());
        Assert.Equal(0, violations);
    }

    [Fact]
    public async Task Seeded_submissions_follow_the_submission_and_review_rules()
    {
        var submissions = await fx.WithDbAsync(db => db.Set<Submission>().AsNoTracking().ToListAsync());
        Assert.NotEmpty(submissions);
        foreach (var s in submissions)
        {
            // Posts may be declared at most 7 days before the submission (SubmissionService rejects older ones).
            Assert.True(s.PostedAt >= s.SubmittedAt.AddDays(-7), $"{s.Id}: posted {s.PostedAt:u}, submitted {s.SubmittedAt:u}");
            Assert.True(s.PostedAt <= s.SubmittedAt, $"{s.Id}: posted after it was submitted");

            // The stored key is the canonical post key of the URL, exactly as SubmissionService computes it.
            var parsed = PlatformUrlRules.Parse(s.Platform, s.PostUrl);
            Assert.True(parsed.IsValid, $"{s.Id}: {s.PostUrl} is not a valid {s.Platform} post URL");
            Assert.Equal(parsed.CanonicalKey, s.NormalizedPostUrl);

            // Nobody reviews or live-checks their own post.
            Assert.NotEqual(s.UserId, s.DecidedByUserId);
            Assert.NotEqual(s.UserId, s.LiveCheckedByUserId);
            Assert.NotEqual(s.UserId, s.ClaimedByUserId);
        }
        Assert.Equal(submissions.Count, submissions.Select(s => s.NormalizedPostUrl).Distinct(StringComparer.Ordinal).Count());

        // The participant only ever acts on their own submission as the author (never claims, decides or checks it).
        var participantActions = new[] { "submitted", "resubmitted", "appealed" };
        var selfActions = await fx.WithDbAsync(db => (
            from e in db.Set<SubmissionEvent>()
            join s in db.Set<Submission>() on e.SubmissionId equals s.Id
            where e.ActorUserId == s.UserId
            select e.Action).Distinct().ToListAsync());
        Assert.All(selfActions, a => Assert.Contains(a, participantActions));
    }

    [Fact]
    public async Task Suspended_users_have_no_earnings_approved_after_their_suspension()
    {
        var suspended = await fx.WithDbAsync(db => db.Set<User>().AsNoTracking()
            .Where(u => u.Status == UserStatus.Suspended).Select(u => new { u.Id, u.StatusChangedAt }).ToListAsync());
        Assert.NotEmpty(suspended);
        foreach (var user in suspended)
        {
            Assert.NotNull(user.StatusChangedAt);
            var approvedAfter = await fx.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking()
                .Where(e => e.UserId == user.Id && e.Amount > 0 && e.ApprovedAt != null && e.ApprovedAt > user.StatusChangedAt)
                .Select(e => e.IdempotencyKey).ToListAsync());
            Assert.Empty(approvedAfter);
        }
    }

    [Fact]
    public async Task Seeded_images_are_public_uploads_allowed_by_the_csp()
    {
        await fx.WithDbAsync(async db =>
        {
            var images = new List<string?>();
            images.AddRange(await db.Set<Campaign>().Select(c => c.HeroImageUrl).ToListAsync());
            images.AddRange(await db.Set<CampaignAsset>().Where(a => a.Type == CampaignAssetType.Image).Select(a => a.Url).ToListAsync());
            images.AddRange(await db.Set<OptimizeAll.Domain.Content.HomepageBanner>().Select(b => b.ImageUrl).ToListAsync());
            var present = images.Where(u => u is not null).Select(u => u!).ToList();
            Assert.NotEmpty(present);
            Assert.All(present, url => Assert.True(FieldRules.IsAllowedImageUrl(url, Array.Empty<string>()), url));

            var ids = present.Select(u => Guid.Parse(u[FieldRules.UploadedFilePrefix.Length..])).ToHashSet();
            var files = await db.Set<StoredFile>().Where(f => ids.Contains(f.Id)).ToListAsync();
            Assert.Equal(ids.Count, files.Count);
            Assert.All(files, f => Assert.True(f.IsPublic && f.Purpose is FilePurpose.CampaignAsset or FilePurpose.ContentImage));
            return true;
        });

        // Served anonymously, like any other public upload.
        var hero = await fx.WithDbAsync(db => db.Set<Campaign>().Where(c => c.HeroImageUrl != null).Select(c => c.HeroImageUrl!).FirstAsync());
        var response = await fx.Demo.CreateClient().GetAsync(hero);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Seeded_notification_links_are_app_routes()
    {
        var links = await fx.WithDbAsync(db => db.Set<Notification>().Where(n => n.LinkUrl != null).Select(n => n.LinkUrl!).Distinct().ToListAsync());
        Assert.NotEmpty(links);
        var allowed = new[] { "/app", "/review/", "/finance/", "/login" };
        Assert.All(links, l => Assert.True(allowed.Any(p => l == p || l.StartsWith(p == "/app" ? "/app/" : p, StringComparison.Ordinal)), l));
    }

    [Fact]
    public async Task Seeded_data_covers_every_journey()
    {
        await fx.WithDbAsync(async db =>
        {
            var statuses = await db.Set<Submission>().Select(s => s.Status).Distinct().ToListAsync();
            foreach (var status in Enum.GetValues<SubmissionStatus>()) Assert.Contains(status, statuses);

            var now = fx.Api.Clock.GetUtcNow().UtcDateTime;
            Assert.True(await db.Set<Submission>().AnyAsync(s => s.Status == SubmissionStatus.UnderReview && s.ClaimExpiresAt > now.AddMinutes(-5)));
            var flags = await db.Set<SubmissionFlag>().Select(f => f.Type).Distinct().ToListAsync();
            Assert.Contains(SubmissionFlagType.DuplicateScreenshot, flags);
            Assert.Contains(SubmissionFlagType.OutsideCampaignWindow, flags);
            Assert.Contains(SubmissionFlagType.HighSubmissionVelocity, flags);
            Assert.True(await db.Set<Submission>().AnyAsync(s => s.LiveCheckStatus == LiveCheckStatus.Pending && s.LiveCheckDueAt <= now));
            Assert.True(await db.Set<Submission>().AnyAsync(s => s.LiveCheckStatus == LiveCheckStatus.Pending && s.LiveCheckDueAt > now));
            var appeals = await db.Set<Appeal>().Select(a => a.Status).Distinct().ToListAsync();
            Assert.Contains(AppealStatus.Open, appeals);
            Assert.Contains(AppealStatus.Upheld, appeals);
            Assert.Contains(AppealStatus.Overturned, appeals);

            // Every submission has a history that starts with "submitted" and ends in its current status.
            var events = await db.Set<SubmissionEvent>().AsNoTracking().ToListAsync();
            foreach (var s in await db.Set<Submission>().AsNoTracking().ToListAsync())
            {
                var history = events.Where(e => e.SubmissionId == s.Id).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();
                Assert.Equal("submitted", history[0].Action);
                Assert.Equal(s.Status, history[^1].ToStatus);
                Assert.Equal(PlatformUrlRules.CanonicalKey(s.Platform, s.PostUrl), s.NormalizedPostUrl);
                Assert.True(s.ScreenshotFileId is not null);
            }

            // Historical rate preservation: submissions captured under v1 of Nimbus were paid at the v1 rate.
            var nimbusSets = await db.Set<RewardRuleSet>().Include(r => r.Rules)
                .Where(r => r.CampaignId == db.Set<OptimizeAll.Domain.Campaigns.Campaign>().Where(c => c.Slug.StartsWith("nimbus-fitness")).Select(c => c.Id).First())
                .OrderBy(r => r.Version).ToListAsync();
            Assert.Equal(2, nimbusSets.Count);
            var v1Earnings = await db.Set<EarningEntry>().Where(e => e.RewardRuleSetId == nimbusSets[0].Id && e.Type == EarningType.PostReward).ToListAsync();
            Assert.NotEmpty(v1Earnings);
            Assert.All(v1Earnings, e => Assert.Contains(nimbusSets[0].Rules, r => r.Id == e.RewardRuleId && (r.Amount == e.Amount || e.Amount < r.Amount)));
            Assert.True(await db.Set<EarningEntry>().AnyAsync(e => e.RewardRuleSetId == nimbusSets[0].Id && e.CreatedAt > nimbusSets[1].EffectiveFrom));

            // Screenshots are real stored files that pass the image inspector.
            var file = await db.Set<StoredFile>().FirstAsync();
            Assert.Equal("image/png", file.ContentType);
            Assert.True(file.Width >= 200 && file.Height >= 200);

            Assert.True(await db.Set<Referral>().AnyAsync(r => r.FraudSignals != null && r.FraudSignals.Contains(ReferralFraudSignals.SharedDevice)));
            Assert.True(await db.Set<PayoutHold>().AnyAsync(h => h.ReleasedAt == null));
            Assert.True(await db.Set<User>().AnyAsync(u => u.Status == UserStatus.Suspended));
            Assert.True(await db.Set<ExchangeRate>().AnyAsync(r => r.Source == "demo"));
            foreach (var action in new[] { "campaign.reward_rules_changed", "payout.batch_finalized", "payout.payment_recorded", "payout.batch_prepared" })
                Assert.True(await db.Set<AuditLog>().AnyAsync(a => a.Action == action), action);
            return true;
        });
    }

    [Fact]
    public async Task Sara_has_non_zero_balance_buckets()
    {
        var sara = await fx.LoginAsync(DemoAccounts.Sara);
        var summary = await (await sara.GetAsync("/api/v1/me/earnings/summary")).ReadJsonAsync();
        foreach (var bucket in new[] { "pending", "approved", "onHold", "scheduled", "paid", "reversed", "availableForNextPayout", "lifetimeEarned" })
            Assert.True(summary.GetProperty(bucket).GetDecimal() > 0, $"{bucket} is zero: {summary}");
        Assert.False(summary.GetProperty("activeHold").GetBoolean());

        var unread = await (await sara.GetAsync("/api/v1/me/notifications/unread-count")).ReadJsonAsync();
        Assert.True(unread.GetProperty("count").GetInt32() > 0);
        var saraTotal = await fx.WithDbAsync(db => db.Set<Notification>()
            .CountAsync(n => n.UserId == db.Set<User>().Where(u => u.Email == DemoAccounts.Sara).Select(u => u.Id).First()));
        Assert.True(saraTotal > unread.GetProperty("count").GetInt32(), "Sara should have read and unread notifications");
    }

    [Fact]
    public async Task New_participant_is_ineligible_because_the_account_is_too_new()
    {
        var client = await fx.LoginAsync(DemoAccounts.NewParticipant);
        var accounts = await (await client.GetAsync("/api/v1/me/social-accounts")).ReadJsonAsync();
        var account = Assert.Single(accounts.GetProperty("items").EnumerateArray());
        Assert.False(account.GetProperty("qualifies").GetBoolean());
        Assert.Contains(account.GetProperty("reasons").EnumerateArray(), r => r.GetProperty("code").GetString() == "social.account_too_new");

        var campaign = await (await client.GetAsync("/api/v1/campaigns/nimbus-fitness-app-launch")).ReadJsonAsync();
        var eligibility = campaign.GetProperty("eligibility");
        Assert.False(eligibility.GetProperty("isEligible").GetBoolean());
        Assert.Contains(eligibility.GetProperty("accounts").EnumerateArray().SelectMany(a => a.GetProperty("reasons").EnumerateArray()),
            r => r.GetProperty("code").GetString() == "social.account_too_new");
    }

    [Fact]
    public async Task Analytics_overview_has_counted_measured_and_estimated_numbers()
    {
        var manager = await fx.LoginAsync(DemoAccounts.Manager);
        var overview = await (await manager.GetAsync("/api/v1/analytics/overview")).ReadJsonAsync();

        decimal Metric(string section, string key) =>
            overview.GetProperty(section).GetProperty("metrics").EnumerateArray()
                .First(m => m.GetProperty("key").GetString() == key).GetProperty("value").GetDecimal();

        Assert.Equal("counted", overview.GetProperty("posts").GetProperty("measurement").GetString());
        Assert.True(Metric("funnel", "registrations") > 0);
        Assert.True(Metric("posts", "postsSubmitted") > 0);
        Assert.True(Metric("posts", "postsApproved") > 0);
        Assert.True(Metric("spend", "spend") > 0);
        Assert.Equal("measured", overview.GetProperty("traffic").GetProperty("measurement").GetString());
        Assert.True(Metric("traffic", "trackedClicks") > 0);
        Assert.True(Metric("conversions", "verifiedConversions") > 0);
        Assert.Equal("estimated", overview.GetProperty("reach").GetProperty("measurement").GetString());
        Assert.True(Metric("reach", "estimatedReach") > 0);
    }

    [Fact]
    public async Task A_reviewer_can_claim_and_decide_a_seeded_pending_submission()
    {
        var id = await fx.WithDbAsync(db => db.Set<Submission>()
            .Where(s => s.Status == SubmissionStatus.Pending && s.ClaimedByUserId == null && s.UserId != db.Set<User>()
                .Where(u => u.Email == DemoAccounts.Sara).Select(u => u.Id).First())
            .OrderBy(s => s.SubmittedAt).Select(s => s.Id).FirstAsync());
        var reviewer = await fx.LoginAsync(DemoAccounts.Reviewer1);

        var queue = await (await reviewer.GetAsync("/api/v1/review/queue")).ReadJsonAsync();
        Assert.True(queue.GetProperty("items").GetArrayLength() > 0);

        var claim = await (await reviewer.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).ReadJsonAsync();
        Assert.Equal("UnderReview", claim.GetProperty("status").GetString());
        var decision = await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{id}/decision",
            new { decision = "Approve", concurrencyStamp = claim.GetProperty("concurrencyStamp").GetGuid() })).ReadJsonAsync();
        Assert.Equal("Approved", decision.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Finance2_can_finalize_the_seeded_draft_batch_prepared_by_finance1()
    {
        var draftId = await fx.WithDbAsync(db => db.Set<PayoutBatch>().Where(b => b.Status == PayoutBatchStatus.Draft).Select(b => b.Id).SingleAsync());

        var finance1 = await fx.LoginAsync(DemoAccounts.Finance1);
        var stamp = (await (await finance1.GetAsync($"/api/v1/finance/payout-batches/{draftId}")).ReadJsonAsync()).GetProperty("concurrencyStamp").GetGuid();
        await (await finance1.PostAsJsonAsync($"/api/v1/finance/payout-batches/{draftId}/finalize",
            new { confirm = true, concurrencyStamp = stamp })).ShouldFailAsync(403, "payout.self_finalize");

        var finance2 = await fx.LoginAsync(DemoAccounts.Finance2);
        var result = await (await finance2.PostAsJsonAsync($"/api/v1/finance/payout-batches/{draftId}/finalize",
            new { confirm = true, reason = "Demo: reviewed totals and exclusions", concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Finalized", result.GetProperty("batch").GetProperty("status").GetString());

        var report = await (await finance2.GetAsync($"/api/v1/finance/payout-batches/{draftId}/reconciliation")).ReadJsonAsync();
        Assert.True(report.GetProperty("isBalanced").GetBoolean(), report.GetProperty("discrepancies").ToString());
    }
}
