using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Submissions;

public sealed class SubmissionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    private async Task<CreatedCampaign> CampaignAsync(Action<Dictionary<string, object?>>? customize = null, object[]? extraRules = null)
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(extraRules: extraRules);
        customize?.Invoke(body);
        return await kit.CreateCampaignAsync(manager, body);
    }

    [Fact]
    public async Task Happy_path_captures_rule_version_estimate_event_and_notification()
    {
        var campaign = await CampaignAsync(extraRules: new object[] { new { type = "FirstPostBonus", amount = 2m } });
        var p = await kit.ParticipantAsync();
        var response = await kit.SubmitAsync(p, campaign.Id, caption: "Loving the launch #optimizeall #ad");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await response.ReadJsonAsync();

        Assert.Equal("Pending", detail.GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("rewardRuleSetVersion").GetInt32());
        Assert.Equal(7m, detail.GetProperty("estimatedReward").GetDecimal());
        Assert.Equal("USD", detail.GetProperty("currency").GetString());
        Assert.StartsWith("/api/v1/files/", detail.GetProperty("screenshotUrl").GetString());
        Assert.Equal("submitted", detail.GetProperty("timeline")[0].GetProperty("action").GetString());
        Assert.Equal("You", detail.GetProperty("timeline")[0].GetProperty("actor").GetString());
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.False(detail.GetProperty("canAppeal").GetBoolean());
        // Fraud signals are never exposed to participants.
        Assert.False(detail.TryGetProperty("riskScore", out _));
        Assert.False(detail.TryGetProperty("flags", out _));

        var id = detail.GetProperty("id").GetGuid();
        var list = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions?campaignId={campaign.Id}");
        Assert.Equal(id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        var notified = await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == p.User.Id && n.Type == NotificationTypes.SubmissionReceived));
        Assert.True(notified);

        // Other participants can't read it.
        var other = await kit.ParticipantAsync();
        await (await other.Client.GetAsync($"/api/v1/me/submissions/{id}")).ShouldFailAsync(404);
    }

    [Theory]
    [InlineData("https://instagram.com/p/{0}")]
    [InlineData("https://m.instagram.com/p/{0}/")]
    [InlineData("http://www.instagram.com/p/{0}?utm_source=ig_web&igshid=abc")]
    [InlineData("https://www.instagram.com/p/{0}/#comments")]
    [InlineData("https://www.instagram.com./p/{0}/")]
    [InlineData("https://instagram.com./p/{0}")]
    [InlineData("https://instagram.com/p/{0}?x=1")]
    [InlineData("https://instagram.com/reel/{0}/")]
    [InlineData("https://m.instagram.com/reels/{0}?igsh=q&x=1")]
    [InlineData("https://www.instagram.com/tv/{0}")]
    [InlineData("https://www.instagram.com/someone.else/p/{0}/")]
    public async Task Duplicate_urls_are_rejected_including_variants(string variant)
    {
        var campaign = await CampaignAsync();
        var code = Guid.NewGuid().ToString("N");
        var first = await kit.ParticipantAsync();
        await kit.SubmitOkAsync(first, campaign.Id, $"https://www.instagram.com/p/{code}/");

        var second = await kit.ParticipantAsync();
        await (await kit.SubmitAsync(second, campaign.Id, string.Format(variant, code))).ShouldFailAsync(409, "submission.duplicate_url");
        // The same participant resubmitting the same post is also refused.
        await (await kit.SubmitAsync(first, campaign.Id, string.Format(variant, code))).ShouldFailAsync(409, "submission.duplicate_url");
    }

    [Fact]
    public async Task Duplicate_screenshot_and_repeated_caption_are_flagged_not_rejected()
    {
        var campaign = await CampaignAsync();
        var screenshot = CampaignTestKit.Png();
        var a = await kit.ParticipantAsync();
        var b = await kit.ParticipantAsync();
        await kit.SubmitOkAsync(a, campaign.Id, screenshot: screenshot, caption: "Same words here!");
        var second = await kit.SubmitOkAsync(b, campaign.Id, screenshot: screenshot, caption: "same WORDS here");

        var flags = await api.WithDbAsync(db => db.Set<SubmissionFlag>().Where(f => f.SubmissionId == second).ToListAsync());
        Assert.Contains(flags, f => f.Type == SubmissionFlagType.DuplicateScreenshot && f.Weight == 40);
        Assert.Contains(flags, f => f.Type == SubmissionFlagType.RepeatedContent && f.Weight == 20);
        var s = await api.WithDbAsync(db => db.Set<Submission>().FirstAsync(x => x.Id == second));
        Assert.Equal(flags.Sum(f => f.Weight), s.RiskScore);
        Assert.Equal(SubmissionStatus.Pending, s.Status);

        var (_, reviewer) = await kit.ReviewerAsync();
        var detail = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{second}");
        Assert.Contains(detail.GetProperty("relatedSubmissions").EnumerateArray(), r => r.GetProperty("match").GetString() == "screenshot_and_content");
    }

    [Fact]
    public async Task Post_outside_campaign_window_is_flagged_and_future_posts_rejected()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddDays(-3));
        var flags = await api.WithDbAsync(db => db.Set<SubmissionFlag>().Where(f => f.SubmissionId == id).Select(f => f.Type).ToListAsync());
        Assert.Contains(SubmissionFlagType.OutsideCampaignWindow, flags);
        Assert.Contains(SubmissionFlagType.NewParticipant, flags);
        Assert.DoesNotContain(SubmissionFlagType.AccountNotVerified, flags);

        await (await kit.SubmitAsync(p, campaign.Id, postedAt: kit.Now.AddMinutes(30))).ShouldFailAsync(400, "submission.posted_at_in_future");
        (await kit.SubmitAsync(p, campaign.Id, postedAt: kit.Now.AddMinutes(5))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Submission_limit_is_enforced_and_rejected_ones_do_not_count()
    {
        var campaign = await CampaignAsync(b => b["maxSubmissionsPerParticipant"] = 1);
        var p = await kit.ParticipantAsync();
        var first = await kit.SubmitOkAsync(p, campaign.Id);
        await (await kit.SubmitAsync(p, campaign.Id)).ShouldFailAsync(409, "submission.limit_reached");

        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, first, "Reject", "Wrong content posted")).EnsureSuccessStatusCode();
        (await kit.SubmitAsync(p, campaign.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parallel_submissions_cannot_exceed_the_limit()
    {
        var campaign = await CampaignAsync(b => b["maxSubmissionsPerParticipant"] = 1);
        var p = await kit.ParticipantAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => kit.SubmitAsync(p, campaign.Id)));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Url_must_match_platform_and_account_must_be_owned_and_on_that_platform()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        await (await kit.SubmitAsync(p, campaign.Id, "https://www.tiktok.com/@me/video/123")).ShouldFailAsync(400, "submission.url_platform_mismatch");
        await (await kit.SubmitAsync(p, campaign.Id, "https://www.instagram.com/")).ShouldFailAsync(400, "submission.url_platform_mismatch");
        await (await kit.SubmitAsync(p, campaign.Id, "javascript:alert(1)")).ShouldFailAsync(400, "submission.invalid_url");

        var other = await kit.ParticipantAsync();
        var stolen = p with { AccountId = other.AccountId };
        await (await kit.SubmitAsync(stolen, campaign.Id)).ShouldFailAsync(400, "submission.social_account_invalid");

        // Declaring TikTok with an Instagram profile.
        var mismatch = await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, "https://www.tiktok.com/@me/video/1", kit.Now, "TikTok"));
        await mismatch.ShouldFailAsync(400, "submission.platform_mismatch");

        // Platform not part of the campaign.
        var youtube = await kit.AddAccountAsync(p.User.Id, SocialPlatform.YouTube);
        var notAllowed = await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(campaign.Id, youtube, "https://youtu.be/abc", kit.Now, "YouTube"));
        await notAllowed.ShouldFailAsync(400, "submission.platform_not_allowed");
    }

    [Fact]
    public async Task Screenshot_is_required_and_must_be_an_image()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var noShot = await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now, includeScreenshot: false));
        await noShot.ShouldFailAsync(400, "submission.screenshot_required");

        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\n" + new string('x', 500));
        var fake = await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now, screenshot: pdf, screenshotName: "shot.png"));
        await fake.ShouldFailAsync(400, "file.unsupported_type");

        var tiny = await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now, screenshot: CampaignTestKit.Png(50, 50)));
        await tiny.ShouldFailAsync(400, "file.too_small");

        var optional = await CampaignAsync(b => b["requireScreenshot"] = false);
        (await p.Client.PostAsync("/api/v1/me/submissions",
            CampaignTestKit.SubmissionForm(optional.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now, includeScreenshot: false))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Closed_or_unknown_campaigns_refuse_submissions()
    {
        var (_, manager) = await kit.ManagerAsync();
        var paused = await kit.CreateCampaignAsync(manager);
        await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{paused.Id}/pause", new { reason = "Paused for review" })).ReadJsonAsync();
        var draft = await kit.CreateCampaignAsync(manager, publish: false);
        var p = await kit.ParticipantAsync();
        await (await kit.SubmitAsync(p, paused.Id)).ShouldFailAsync(409, "submission.campaign_closed");
        await (await kit.SubmitAsync(p, draft.Id)).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Correction_resubmission_keeps_original_rule_version_then_approves()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager);
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();

        await (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "no")).ShouldFailAsync(400, "review.reason_too_short");
        await (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "   ")).ShouldFailAsync(400, "review.reason_required");
        (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "Please add the #ad disclosure")).EnsureSuccessStatusCode();
        var mine = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions/{id}");
        Assert.Equal("NeedsCorrection", mine.GetProperty("status").GetString());
        Assert.True(mine.GetProperty("canEdit").GetBoolean());
        Assert.Equal("Please add the #ad disclosure", mine.GetProperty("decisionReason").GetString());
        Assert.Equal("Reviewer", mine.GetProperty("timeline").EnumerateArray().Last().GetProperty("actor").GetString());

        // Rates change while the participant fixes the post.
        (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/reward-rules", new
        {
            currency = "USD", rules = new object[] { new { type = "BaseRate", amount = 8m } }, reason = "Raise rates", confirm = true,
        })).EnsureSuccessStatusCode();

        var form = new MultipartFormDataContent
        {
            { new StringContent(CampaignTestKit.InstagramUrl()), "postUrl" },
            { new StringContent("Fixed caption #ad"), "captionText" },
        };
        var resubmitted = await (await p.Client.PutAsync($"/api/v1/me/submissions/{id}", form)).ReadJsonAsync();
        Assert.Equal("Pending", resubmitted.GetProperty("status").GetString());
        Assert.Equal(1, resubmitted.GetProperty("correctionCount").GetInt32());
        Assert.Equal(1, resubmitted.GetProperty("rewardRuleSetVersion").GetInt32());
        Assert.Equal("resubmitted", resubmitted.GetProperty("timeline").EnumerateArray().Last().GetProperty("action").GetString());

        // Not editable any more.
        await (await p.Client.PutAsync($"/api/v1/me/submissions/{id}", new MultipartFormDataContent { { new StringContent("x"), "captionText" } }))
            .ShouldFailAsync(409, "submission.not_editable");

        (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).EnsureSuccessStatusCode();
        var earning = Assert.Single(await kit.EarningsAsync(id));
        Assert.Equal(5m, earning.Amount);
        Assert.Equal(1, earning.RewardRuleSetVersion);
    }

    [Fact]
    public async Task Reject_appeal_overturned_by_a_different_reviewer_creates_earnings()
    {
        var campaign = await CampaignAsync(extraRules: new object[] { new { type = "FirstPostBonus", amount = 2m } });
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewerA) = await kit.ReviewerAsync();
        var (_, reviewerB) = await kit.ReviewerAsync();

        (await CampaignTestKit.DecideAsync(reviewerA, id, "Reject", "Post does not show the product")).EnsureSuccessStatusCode();
        var rejected = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions/{id}");
        Assert.True(rejected.GetProperty("canAppeal").GetBoolean());

        await (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal", new { reason = "too short" })).ShouldFailAsync(400);
        var appealed = await (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal",
            new { reason = "The product is visible in the second image of the carousel." })).ReadJsonAsync();
        Assert.Equal("Open", appealed.GetProperty("appeal").GetProperty("status").GetString());
        Assert.False(appealed.GetProperty("canAppeal").GetBoolean());
        await (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal",
            new { reason = "Appealing a second time about the same decision." })).ShouldFailAsync(409, "appeal.not_allowed");

        var appeals = await CampaignTestKit.GetJsonAsync(reviewerB, "/api/v1/review/appeals?status=Open&pageSize=200");
        var item = appeals.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("submissionId").GetGuid() == id);
        var appealId = item.GetProperty("id").GetGuid();
        var stamp = item.GetProperty("concurrencyStamp").GetGuid();
        var appealDetail = await CampaignTestKit.GetJsonAsync(reviewerB, $"/api/v1/review/appeals/{appealId}");
        Assert.Equal(id, appealDetail.GetProperty("review").GetProperty("submission").GetProperty("id").GetGuid());

        // The reviewer who rejected it cannot decide the appeal.
        await (await reviewerA.PostAsJsonAsync($"/api/v1/review/appeals/{appealId}/resolve",
            new { outcome = "Overturned", note = "I changed my mind", concurrencyStamp = stamp })).ShouldFailAsync(403, "appeal.same_reviewer");

        var resolution = await (await reviewerB.PostAsJsonAsync($"/api/v1/review/appeals/{appealId}/resolve",
            new { outcome = "Overturned", note = "Product visible in carousel", concurrencyStamp = stamp })).ReadJsonAsync();
        Assert.Equal("Overturned", resolution.GetProperty("appeal").GetProperty("status").GetString());
        Assert.Equal("Approved", resolution.GetProperty("submissionStatus").GetString());

        var earnings = await kit.EarningsAsync(id);
        Assert.Equal(2, earnings.Count);
        Assert.Contains(earnings, e => e.Type == EarningType.PostReward && e.Amount == 5m && e.IdempotencyKey == $"submission:{id}:PostReward:appeal:{appealId}");
        Assert.Contains(earnings, e => e.Type == EarningType.FirstPostBonus && e.IdempotencyKey == $"firstpost:{campaign.Id}:{p.User.Id}");

        await (await reviewerB.PostAsJsonAsync($"/api/v1/review/appeals/{appealId}/resolve",
            new { outcome = "Overturned", note = "again", concurrencyStamp = stamp })).ShouldFailAsync(409, "appeal.already_resolved");

        var mine = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions/{id}");
        Assert.Equal("Approved", mine.GetProperty("status").GetString());
        Assert.Equal("Overturned", mine.GetProperty("appeal").GetProperty("status").GetString());
        Assert.Equal(2, mine.GetProperty("earnings").GetArrayLength());
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == p.User.Id && n.Type == NotificationTypes.AppealResolved)));
    }

    [Fact]
    public async Task Upheld_appeal_changes_nothing_and_admins_cannot_resolve_their_own_decisions()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        (await CampaignTestKit.DecideAsync(admin, id, "Reject", "Post is private")).EnsureSuccessStatusCode();
        await (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal",
            new { reason = "My account is public now, please look again." })).ReadJsonAsync();
        var appeals = await CampaignTestKit.GetJsonAsync(admin, "/api/v1/review/appeals?pageSize=200");
        var item = appeals.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("submissionId").GetGuid() == id);
        var body = new { outcome = "Upheld", note = "Post still private", concurrencyStamp = item.GetProperty("concurrencyStamp").GetGuid() };

        // Four eyes applies to admins too.
        await (await admin.PostAsJsonAsync($"/api/v1/review/appeals/{item.GetProperty("id").GetGuid()}/resolve", body))
            .ShouldFailAsync(403, "appeal.same_reviewer");
        var (_, otherAdmin) = await api.CreateClientAsync(Role.Admin);
        var resolution = await (await otherAdmin.PostAsJsonAsync($"/api/v1/review/appeals/{item.GetProperty("id").GetGuid()}/resolve", body)).ReadJsonAsync();
        Assert.Equal("Rejected", resolution.GetProperty("submissionStatus").GetString());
        Assert.Empty(await kit.EarningsAsync(id));
    }

    [Fact]
    public async Task Screenshots_are_private_to_owner_reviewers_and_the_campaign_creator()
    {
        var (_, creator) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(creator, kit.CampaignBody());
        var p = await kit.ParticipantAsync();
        var detail = await (await kit.SubmitAsync(p, campaign.Id)).ReadJsonAsync();
        var url = detail.GetProperty("screenshotUrl").GetString()!;

        var own = await p.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("image/png", own.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", own.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; sandbox", own.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(own.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromSeconds(300), own.Headers.CacheControl.MaxAge);
        Assert.Equal("inline", own.Content.Headers.ContentDisposition!.DispositionType);

        var other = await kit.ParticipantAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync(url)).StatusCode);
        var (_, reviewer) = await kit.ReviewerAsync();
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync(url)).StatusCode);
        // Campaign managers only see screenshots of submissions to campaigns they created (data minimization)…
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync(url)).StatusCode);
        var (_, otherManager) = await kit.ManagerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.GetAsync(url)).StatusCode);
        // …unless they also review submissions.
        var (_, managerReviewer) = await api.CreateClientAsync(Role.CampaignManager, Role.Reviewer);
        Assert.Equal(HttpStatusCode.OK, (await managerReviewer.GetAsync(url)).StatusCode);
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        Assert.Equal(HttpStatusCode.NotFound, (await finance.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await p.Client.GetAsync($"/api/v1/files/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Experiment_variant_is_kept_only_for_running_experiments_of_the_campaign()
    {
        var campaign = await CampaignAsync();
        var (manager, _) = await kit.ManagerAsync();
        var experiment = new OptimizeAll.Domain.Marketing.Experiment
        {
            CampaignId = campaign.Id, Name = "Title test", Status = OptimizeAll.Domain.Marketing.ExperimentStatus.Running, CreatedByUserId = manager.Id,
        };
        var variant = new OptimizeAll.Domain.Marketing.ExperimentVariant { ExperimentId = experiment.Id, Key = "B", Name = "Variant B" };
        experiment.Variants.Add(variant);
        await api.WithDbAsync(async db => { db.Add(experiment); await db.SaveChangesAsync(); });

        var p = await kit.ParticipantAsync();
        var form = CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now);
        form.Add(new StringContent(variant.Id.ToString()), "experimentVariantId");
        var id = (await (await p.Client.PostAsync("/api/v1/me/submissions", form)).ReadJsonAsync()).GetProperty("id").GetGuid();
        var bogus = CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, CampaignTestKit.InstagramUrl(), kit.Now);
        bogus.Add(new StringContent(Guid.NewGuid().ToString()), "experimentVariantId");
        var id2 = (await (await p.Client.PostAsync("/api/v1/me/submissions", bogus)).ReadJsonAsync()).GetProperty("id").GetGuid();

        var stored = await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == id || s.Id == id2)
            .ToDictionaryAsync(s => s.Id, s => s.ExperimentVariantId));
        Assert.Equal(variant.Id, stored[id]);
        Assert.Null(stored[id2]);
    }

    // ------------------------------------------------------------------ C1: canonical post keys

    [Fact]
    public async Task Unknown_subdomains_are_rejected_as_platform_mismatch()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var code = Guid.NewGuid().ToString("N");
        await (await kit.SubmitAsync(p, campaign.Id, $"https://de.instagram.com/p/{code}/")).ShouldFailAsync(400, "submission.url_platform_mismatch");
        await (await kit.SubmitAsync(p, campaign.Id, $"https://instagram.com.evil.example/p/{code}/")).ShouldFailAsync(400, "submission.url_platform_mismatch");
    }

    [Fact]
    public async Task Stored_key_is_canonical_and_codes_differing_only_by_case_are_distinct_posts()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var code = "CxY" + Guid.NewGuid().ToString("N")[..10];
        var upper = await kit.SubmitOkAsync(p, campaign.Id, $"https://www.instagram.com/reel/{code.ToUpperInvariant()}/?igsh=1");
        var lower = await kit.SubmitOkAsync(p, campaign.Id, $"https://instagram.com/p/{code.ToLowerInvariant()}");

        var keys = await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == upper || s.Id == lower)
            .ToDictionaryAsync(s => s.Id, s => s.NormalizedPostUrl));
        Assert.Equal($"instagram:{code.ToUpperInvariant()}", keys[upper]);
        Assert.Equal($"instagram:{code.ToLowerInvariant()}", keys[lower]);

        // The same case is still a duplicate, and the column itself compares case-sensitively.
        await (await kit.SubmitAsync(p, campaign.Id, $"https://instagram.com./p/{code.ToLowerInvariant()}/?x=1")).ShouldFailAsync(409, "submission.duplicate_url");
        var collation = await api.WithDbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // SQLite: the column keeps the default BINARY (case-sensitive) collation, i.e. declares no COLLATE clause.
            cmd.CommandText = ApiFactory.IsSqlite
                ? "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'submissions'"
                : "SELECT COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                  "AND TABLE_NAME = 'submissions' AND COLUMN_NAME = 'NormalizedPostUrl'";
            return (string?)await cmd.ExecuteScalarAsync();
        });
        if (ApiFactory.IsSqlite)
        {
            Assert.Contains("\"NormalizedPostUrl\" TEXT NOT NULL", collation);
            Assert.DoesNotContain("COLLATE", collation!, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal("utf8mb4_bin", collation);
        }
    }

    [Fact]
    public async Task YouTube_link_forms_of_one_video_collide()
    {
        var campaign = await CampaignAsync(b => b["platforms"] = new[] { "Instagram", "YouTube" });
        var first = await kit.ParticipantAsync(SocialPlatform.YouTube);
        var second = await kit.ParticipantAsync(SocialPlatform.YouTube);
        var videoId = "Yt" + Guid.NewGuid().ToString("N")[..9];
        await kit.SubmitOkAsync(first, campaign.Id, $"https://youtu.be/{videoId}?si=abc");
        foreach (var variant in new[]
                 {
                     $"https://www.youtube.com/watch?v={videoId}&t=10s", $"https://m.youtube.com/shorts/{videoId}",
                     $"https://youtube.com./live/{videoId}?feature=share",
                 })
            await (await kit.SubmitAsync(second, campaign.Id, variant)).ShouldFailAsync(409, "submission.duplicate_url");
    }

    [Fact]
    public async Task TikTok_short_links_are_accepted_and_flagged_for_reviewers()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync(SocialPlatform.TikTok);
        var code = "ZM" + Guid.NewGuid().ToString("N")[..8];
        var id = await kit.SubmitOkAsync(p, campaign.Id, $"https://vm.tiktok.com/{code}/");
        var s = await api.WithDbAsync(db => db.Set<Submission>().Include(x => x.Flags).FirstAsync(x => x.Id == id));
        Assert.Equal($"tiktok-short:{code}", s.NormalizedPostUrl);
        Assert.Contains(s.Flags, f => f.Type == SubmissionFlagType.UnresolvedShortLink);
        var other = await kit.ParticipantAsync(SocialPlatform.TikTok);
        await (await kit.SubmitAsync(other, campaign.Id, $"https://www.tiktok.com/t/{code}")).ShouldFailAsync(409, "submission.duplicate_url");
    }

    // ------------------------------------------------------------------ H1: participant-chosen PostedAt

    [Fact]
    public async Task Posted_at_more_than_seven_days_before_submission_is_refused_and_long_gaps_are_flagged()
    {
        var campaign = await CampaignAsync(b => b["startsAt"] = kit.Now.AddDays(-20));
        var p = await kit.ParticipantAsync();
        await (await kit.SubmitAsync(p, campaign.Id, postedAt: kit.Now.AddDays(-7).AddMinutes(-1))).ShouldFailAsync(400, "submission.posted_at_too_old");

        var old = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddDays(-7).AddMinutes(5));
        var recent = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddHours(-47));
        var flags = await api.WithDbAsync(db => db.Set<SubmissionFlag>().Where(f => f.SubmissionId == old || f.SubmissionId == recent).ToListAsync());
        var flag = Assert.Single(flags, f => f.Type == SubmissionFlagType.PostedLongBeforeSubmission);
        Assert.Equal(old, flag.SubmissionId);
        Assert.Equal(15, flag.Weight);
    }

    [Fact]
    public async Task Resubmission_re_runs_the_posted_at_rules()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddHours(-1));
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "Please fix the post time")).EnsureSuccessStatusCode();

        var tooOld = new MultipartFormDataContent { { new StringContent(kit.Now.AddDays(-8).ToString("O")), "postedAt" } };
        await (await p.Client.PutAsync($"/api/v1/me/submissions/{id}", tooOld)).ShouldFailAsync(400, "submission.posted_at_too_old");

        var backdated = new MultipartFormDataContent { { new StringContent(kit.Now.AddDays(-3).ToString("O")), "postedAt" } };
        (await p.Client.PutAsync($"/api/v1/me/submissions/{id}", backdated)).EnsureSuccessStatusCode();
        var flags = await api.WithDbAsync(db => db.Set<SubmissionFlag>().Where(f => f.SubmissionId == id && f.ResolvedAt == null).ToListAsync());
        Assert.Contains(flags, f => f.Type == SubmissionFlagType.PostedLongBeforeSubmission);
    }

    [Fact]
    public async Task A_future_posted_at_cannot_reach_into_a_bonus_window_that_has_not_started()
    {
        var start = kit.Now.AddMinutes(2);
        var campaign = await CampaignAsync(extraRules: new object[]
        {
            new { type = "TimeLimitedBonus", amount = 2m, validFrom = start, validTo = start.AddDays(1), label = "Launch bonus" },
        });
        var p = await kit.ParticipantAsync();
        // Declared 5 minutes in the future (inside the allowed clock skew) and inside the bonus window.
        var detail = await (await kit.SubmitAsync(p, campaign.Id, postedAt: kit.Now.AddMinutes(5))).ReadJsonAsync();
        Assert.Equal(5m, detail.GetProperty("estimatedReward").GetDecimal());
    }

    // ------------------------------------------------------------------ M1: long appeal text

    [Fact]
    public async Task Maximum_length_appeal_is_stored_and_its_timeline_copy_truncated()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, id, "Reject", new string('r', 900))).EnsureSuccessStatusCode();
        var reason = new string('a', 2000);
        (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal", new { reason })).EnsureSuccessStatusCode();

        var stored = await api.WithDbAsync(db => db.Set<Appeal>().Where(a => a.SubmissionId == id).Select(a => a.Reason).FirstAsync());
        Assert.Equal(2000, stored.Length);
        var evt = await api.WithDbAsync(db => db.Set<SubmissionEvent>().Where(e => e.SubmissionId == id && e.Action == "appealed").Select(e => e.Reason!).FirstAsync());
        Assert.Equal(1000, evt.Length);
        Assert.EndsWith("…", evt);
    }
}
