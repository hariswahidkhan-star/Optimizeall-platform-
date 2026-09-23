using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Review;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Review;

public sealed class ReviewTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    private async Task<CreatedCampaign> CampaignAsync(Action<Dictionary<string, object?>>? customize = null, params object[] extraRules)
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(extraRules: extraRules);
        customize?.Invoke(body);
        return await kit.CreateCampaignAsync(manager, body);
    }

    [Fact]
    public async Task Two_reviewers_deciding_simultaneously_exactly_one_wins()
    {
        var campaign = await CampaignAsync(null, new { type = "FirstPostBonus", amount = 2m });
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        var stamp = await CampaignTestKit.StampAsync(a, id);

        var results = await Task.WhenAll(
            CampaignTestKit.DecideAsync(a, id, "Approve", stamp: stamp),
            CampaignTestKit.DecideAsync(b, id, "Approve", stamp: stamp));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        var loser = Assert.Single(results, r => r.StatusCode != HttpStatusCode.OK);
        await loser.ShouldFailAsync(409, "review.already_decided");

        var earnings = await kit.EarningsAsync(id);
        Assert.Equal(2, earnings.Count);
        Assert.Single(earnings, e => e.Type == EarningType.PostReward);
        Assert.Single(earnings, e => e.Type == EarningType.FirstPostBonus);
        var events = await api.WithDbAsync(db => db.Set<SubmissionEvent>().CountAsync(e => e.SubmissionId == id && e.Action == "approved"));
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task Approve_and_reject_racing_produce_one_decision()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        var stamp = await CampaignTestKit.StampAsync(a, id);
        var results = await Task.WhenAll(
            CampaignTestKit.DecideAsync(a, id, "Approve", stamp: stamp),
            CampaignTestKit.DecideAsync(b, id, "Reject", "Content does not match", stamp: stamp));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        var status = await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == id).Select(s => s.Status).FirstAsync());
        var earnings = await kit.EarningsAsync(id);
        Assert.Equal(status == SubmissionStatus.Approved ? 1 : 0, earnings.Count);
    }

    [Fact]
    public async Task First_post_bonus_is_paid_once_even_with_concurrent_approvals()
    {
        var campaign = await CampaignAsync(null, new { type = "FirstPostBonus", amount = 3m });
        var p = await kit.ParticipantAsync();
        var s1 = await kit.SubmitOkAsync(p, campaign.Id);
        var s2 = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        var stamp1 = await CampaignTestKit.StampAsync(a, s1);
        var stamp2 = await CampaignTestKit.StampAsync(b, s2);

        var results = await Task.WhenAll(
            CampaignTestKit.DecideAsync(a, s1, "Approve", stamp: stamp1),
            CampaignTestKit.DecideAsync(b, s2, "Approve", stamp: stamp2));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var all = await api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.UserId == p.User.Id).ToListAsync());
        Assert.Single(all, e => e.Type == EarningType.FirstPostBonus);
        Assert.Equal(2, all.Count(e => e.Type == EarningType.PostReward));
    }

    [Fact]
    public async Task Claims_block_other_reviewers_until_released_or_expired()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (userA, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();

        var claim = await (await a.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).ReadJsonAsync();
        Assert.Equal("UnderReview", claim.GetProperty("status").GetString());
        Assert.Equal(userA.Id, claim.GetProperty("claimedBy").GetProperty("id").GetGuid());
        // Re-claiming extends; others are refused with who/until.
        (await a.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).EnsureSuccessStatusCode();
        await (await b.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).ShouldFailAsync(409, "review.claimed_by_other");
        await (await CampaignTestKit.DecideAsync(b, id, "Approve")).ShouldFailAsync(409, "review.claimed_by_other");

        var queue = await CampaignTestKit.GetJsonAsync(b, $"/api/v1/review/queue?campaignId={campaign.Id}");
        var item = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(userA.Id, item.GetProperty("claimedBy").GetProperty("id").GetGuid());
        Assert.Contains("NewParticipant", item.GetProperty("flagTypes").EnumerateArray().Select(f => f.GetString()));

        await (await b.PostAsync($"/api/v1/review/submissions/{id}/release", null)).ShouldFailAsync(409, "review.not_claimed");
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/api/v1/review/submissions/{id}/release", null)).StatusCode);
        (await b.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(b, id, "Approve")).EnsureSuccessStatusCode();
        await (await a.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).ShouldFailAsync(409, "review.already_decided");

        var events = await api.WithDbAsync(db => db.Set<SubmissionEvent>().Where(e => e.SubmissionId == id).Select(e => e.Action).ToListAsync());
        Assert.Single(events, e => e == "claimed");
        var flags = await api.WithDbAsync(db => db.Set<SubmissionFlag>().Where(f => f.SubmissionId == id).ToListAsync());
        Assert.All(flags, f => Assert.Equal("approved", f.ResolutionNote));
    }

    [Fact]
    public async Task Stale_concurrency_stamp_is_rejected()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        var stamp = await CampaignTestKit.StampAsync(reviewer, id);
        (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "Please show the caption", stamp: stamp)).EnsureSuccessStatusCode();
        (await p.Client.PutAsync($"/api/v1/me/submissions/{id}",
            new MultipartFormDataContent { { new StringContent("Now with caption #ad"), "captionText" } })).EnsureSuccessStatusCode();
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve", stamp: stamp)).ShouldFailAsync(409, "review.already_decided");
        (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Caps_and_budget_are_applied_at_approval()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(baseAmount: 4m);
        body["budgetAmount"] = 10m;
        body["rewardRules"] = new Dictionary<string, object?>
        {
            ["currency"] = "USD", ["dailyCapPerParticipant"] = 6m,
            ["rules"] = new object[] { new { type = "BaseRate", amount = 4m } },
        };
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var (_, reviewer) = await kit.ReviewerAsync();

        var p = await kit.ParticipantAsync();
        var postedAt = kit.Now.AddHours(-2);
        var s1 = await kit.SubmitOkAsync(p, campaign.Id, postedAt: postedAt);
        var s2 = await kit.SubmitOkAsync(p, campaign.Id, postedAt: postedAt);
        var q = await kit.ParticipantAsync();
        var s3 = await kit.SubmitOkAsync(q, campaign.Id, postedAt: postedAt);
        var s4 = await kit.SubmitOkAsync(q, campaign.Id, postedAt: postedAt);

        (await CampaignTestKit.DecideAsync(reviewer, s1, "Approve")).EnsureSuccessStatusCode();
        var second = await (await CampaignTestKit.DecideAsync(reviewer, s2, "Approve")).ReadJsonAsync();
        Assert.Equal(2m, second.GetProperty("reward").GetProperty("total").GetDecimal());
        Assert.Equal("daily_cap", second.GetProperty("reward").GetProperty("appliedCaps")[0].GetString());

        // Budget: 10 - 6 spent = 4 remaining → participant q gets 4, then nothing.
        var third = await (await CampaignTestKit.DecideAsync(reviewer, s3, "Approve")).ReadJsonAsync();
        Assert.Equal(4m, third.GetProperty("reward").GetProperty("total").GetDecimal());
        var fourth = await (await CampaignTestKit.DecideAsync(reviewer, s4, "Approve")).ReadJsonAsync();
        Assert.Equal("Approved", fourth.GetProperty("status").GetString());
        Assert.Equal(0m, fourth.GetProperty("reward").GetProperty("total").GetDecimal());
        Assert.Contains("campaign_budget", fourth.GetProperty("reward").GetProperty("appliedCaps").EnumerateArray().Select(c => c.GetString()));
        Assert.Empty(await kit.EarningsAsync(s4));
        Assert.Contains("caps applied", await api.WithDbAsync(db => db.Set<SubmissionEvent>()
            .Where(e => e.SubmissionId == s4 && e.Action == "approved").Select(e => e.Reason!).FirstAsync()));

        var admin = await CampaignTestKit.GetJsonAsync(manager, $"/api/v1/admin/campaigns/{campaign.Id}");
        Assert.Equal(10m, admin.GetProperty("spent").GetDecimal());
        Assert.Equal(0m, admin.GetProperty("budgetRemaining").GetDecimal());
    }

    [Fact]
    public async Task Quality_bonus_needs_a_rule_and_is_bounded_and_pending()
    {
        var campaign = await CampaignAsync(null, new { type = "QualityBonus", amount = 3m, approvalMode = "ManualApproval" });
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        var result = await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve", quality: 10m)).ReadJsonAsync();
        Assert.Equal(8m, result.GetProperty("reward").GetProperty("total").GetDecimal());
        var earnings = await kit.EarningsAsync(id);
        Assert.Equal(EarningStatus.Approved, earnings.Single(e => e.Type == EarningType.PostReward).Status);
        Assert.Equal(EarningStatus.PendingApproval, earnings.Single(e => e.Type == EarningType.QualityBonus && e.Amount == 3m).Status);

        var plain = await CampaignAsync();
        var id2 = await kit.SubmitOkAsync(p, plain.Id);
        await (await CampaignTestKit.DecideAsync(reviewer, id2, "Approve", quality: 1m)).ShouldFailAsync(400, "reward.quality_bonus_not_configured");
        Assert.Equal(SubmissionStatus.Pending, await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == id2).Select(s => s.Status).FirstAsync()));
    }

    [Fact]
    public async Task Reversal_of_an_approved_submission_creates_reversal_entries()
    {
        var campaign = await CampaignAsync(null, new { type = "FirstPostBonus", amount = 2m });
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).EnsureSuccessStatusCode();

        // Reviewers lack submissions.reverse; finance has it.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{id}/reverse", new { reason = "Fraud detected", confirm = true })).StatusCode);
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{id}/reverse", new { reason = "Fraud detected" }))
            .ShouldFailAsync(400, "confirmation.required");
        var reversed = await (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{id}/reverse",
            new { reason = "Bought engagement detected", confirm = true })).ReadJsonAsync();
        Assert.Equal("Reversed", reversed.GetProperty("status").GetString());

        var earnings = await kit.EarningsAsync(id);
        Assert.Equal(4, earnings.Count);
        Assert.Equal(2, earnings.Count(e => e.Type == EarningType.Reversal && e.Amount < 0));
        Assert.All(earnings.Where(e => e.Type != EarningType.Reversal), e => Assert.Equal(EarningStatus.Reversed, e.Status));
        Assert.Equal(0m, earnings.Sum(e => e.Amount));

        await (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{id}/reverse",
            new { reason = "Again", confirm = true })).ShouldFailAsync(409, "review.not_approved");
        var mine = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions/{id}");
        Assert.Equal("Reversed", mine.GetProperty("status").GetString());
        Assert.True(mine.GetProperty("canAppeal").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == p.User.Id && n.Type == NotificationTypes.SubmissionReversed)));
    }

    [Fact]
    public async Task Reversed_then_appeal_overturned_creates_new_earnings()
    {
        var campaign = await CampaignAsync(null, new { type = "FirstPostBonus", amount = 2m });
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).EnsureSuccessStatusCode();
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{id}/reverse", new { reason = "Post looked removed", confirm = true })).EnsureSuccessStatusCode();
        (await p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal", new { reason = "The post is still live, it was a loading error." })).EnsureSuccessStatusCode();

        var appeals = await CampaignTestKit.GetJsonAsync(reviewer, "/api/v1/review/appeals?pageSize=200");
        var item = appeals.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("submissionId").GetGuid() == id);
        (await reviewer.PostAsJsonAsync($"/api/v1/review/appeals/{item.GetProperty("id").GetGuid()}/resolve",
            new { outcome = "Overturned", note = "Post is live", concurrencyStamp = item.GetProperty("concurrencyStamp").GetGuid() })).EnsureSuccessStatusCode();

        var earnings = await kit.EarningsAsync(id);
        var live = earnings.Where(e => e.Status is EarningStatus.Approved or EarningStatus.PendingApproval && e.Type != EarningType.Reversal).ToList();
        // A fresh post reward, and (because the only first-post bonus was reversed) the first-post bonus once more,
        // under the next first-post key. Never two live first-post bonuses.
        Assert.Equal(2, live.Count);
        Assert.Single(live, e => e.Type == EarningType.PostReward);
        Assert.Equal($"firstpost:{campaign.Id}:{p.User.Id}:1", Assert.Single(live, e => e.Type == EarningType.FirstPostBonus).IdempotencyKey);
        Assert.Equal(2, earnings.Count(e => e.Status == EarningStatus.Reversed && e.Type != EarningType.Reversal));
    }

    [Fact]
    public async Task Stats_reviewers_and_assignment()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var (reviewerUser, reviewer) = await kit.ReviewerAsync();
        var (_, manager) = await kit.ManagerAsync();

        var reviewers = await CampaignTestKit.GetJsonAsync(manager, "/api/v1/review/reviewers");
        Assert.Contains(reviewers.EnumerateArray(), r => r.GetProperty("id").GetGuid() == reviewerUser.Id);
        await (await manager.PostAsJsonAsync("/api/v1/review/assign", new { submissionIds = new[] { id }, reviewerId = p.User.Id }))
            .ShouldFailAsync(400, "review.not_a_reviewer");
        var assigned = await (await manager.PostAsJsonAsync("/api/v1/review/assign", new { submissionIds = new[] { id, Guid.NewGuid() }, reviewerId = reviewerUser.Id })).ReadJsonAsync();
        Assert.Equal(1, assigned.GetProperty("updated").GetInt32());
        Assert.Single(assigned.GetProperty("skippedIds").EnumerateArray());

        var mine = await CampaignTestKit.GetJsonAsync(reviewer, "/api/v1/review/queue?assignedToMe=true");
        Assert.Equal(id, Assert.Single(mine.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).EnsureSuccessStatusCode();
        var stats = await CampaignTestKit.GetJsonAsync(reviewer, "/api/v1/review/stats");
        Assert.Equal(1, stats.GetProperty("myDecisionsToday").GetInt32());
        Assert.True(stats.GetProperty("queueByStatus").TryGetProperty("Pending", out _));

        var detail = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{id}");
        Assert.Equal("#ad", detail.GetProperty("requirements").GetProperty("disclosureText").GetString());
        Assert.Equal(p.User.Email, detail.GetProperty("participant").GetProperty("email").GetString());
        Assert.Equal(1, detail.GetProperty("participant").GetProperty("approvedCount").GetInt32());
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), e => e.GetProperty("action").GetString() == "approved" &&
                                                                             e.GetProperty("actor").GetProperty("id").GetGuid() == reviewerUser.Id);
        Assert.Equal(1, detail.GetProperty("earnings").GetArrayLength());
    }

    // ------------------------------------------------------------------ helpers for the integrity tests

    private async Task<Participant> StaffParticipantAsync(params Role[] staffRoles)
    {
        var user = await api.CreateUserAsync(new[] { Role.Participant }.Concat(staffRoles).ToArray());
        var accountId = await kit.AddAccountAsync(user.Id, SocialPlatform.Instagram);
        return new Participant(user, await api.LoginAsync(user), accountId, SocialPlatform.Instagram);
    }

    private static async Task<(Guid Id, Guid Stamp)> AppealItemAsync(HttpClient staff, Guid submissionId)
    {
        var appeals = await CampaignTestKit.GetJsonAsync(staff, "/api/v1/review/appeals?status=Open&pageSize=200");
        var item = appeals.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("submissionId").GetGuid() == submissionId);
        return (item.GetProperty("id").GetGuid(), item.GetProperty("concurrencyStamp").GetGuid());
    }

    private static Task<HttpResponseMessage> ResolveAsync(HttpClient staff, (Guid Id, Guid Stamp) appeal, string outcome, string note) =>
        staff.PostAsJsonAsync($"/api/v1/review/appeals/{appeal.Id}/resolve", new { outcome, note, concurrencyStamp = appeal.Stamp });

    private static Task<HttpResponseMessage> AppealAsync(Participant p, Guid id, string? reason = null) =>
        p.Client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/appeal", new { reason = reason ?? "Please look again, the post meets every requirement." });

    private Task SetUserStatusAsync(Guid userId, UserStatus status) =>
        api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, status)));

    // ------------------------------------------------------------------ H2: self-review

    [Fact]
    public async Task Staff_can_never_review_their_own_submissions_or_appeals()
    {
        var campaign = await CampaignAsync();
        var me = await StaffParticipantAsync(Role.Admin);
        var mine = await kit.SubmitOkAsync(me, campaign.Id);

        await (await me.Client.PostAsync($"/api/v1/review/submissions/{mine}/claim", null)).ShouldFailAsync(403, "review.self_review");
        await (await CampaignTestKit.DecideAsync(me.Client, mine, "Approve")).ShouldFailAsync(403, "review.self_review");
        await (await CampaignTestKit.DecideAsync(me.Client, mine, "Reject", "Rejecting my own post")).ShouldFailAsync(403, "review.self_review");
        Assert.Empty(await kit.EarningsAsync(mine));

        // Approved by someone else: still can't confirm the live check or reverse it.
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, mine, "Approve")).EnsureSuccessStatusCode();
        await (await me.Client.PostAsJsonAsync($"/api/v1/review/submissions/{mine}/live-check", new { result = "ConfirmedLive" }))
            .ShouldFailAsync(403, "review.self_review");
        await (await me.Client.PostAsJsonAsync($"/api/v1/review/submissions/{mine}/reverse", new { reason = "Reversing my own post", confirm = true }))
            .ShouldFailAsync(403, "review.self_review");

        // Rejected by someone else and appealed: can't resolve the own appeal.
        var second = await kit.SubmitOkAsync(me, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, second, "Reject", "Product not visible")).EnsureSuccessStatusCode();
        (await AppealAsync(me, second)).EnsureSuccessStatusCode();
        var appeal = await AppealItemAsync(me.Client, second);
        await (await ResolveAsync(me.Client, appeal, "Overturned", "Looks fine to me")).ShouldFailAsync(403, "review.self_review");
        Assert.Equal(SubmissionStatus.Rejected, await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == second).Select(s => s.Status).FirstAsync()));
    }

    // ------------------------------------------------------------------ H3: suspended participants

    [Fact]
    public async Task Suspended_participants_cannot_be_approved_or_overturned_but_can_be_rejected_and_reversed()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var toApprove = await kit.SubmitOkAsync(p, campaign.Id);
        var toReject = await kit.SubmitOkAsync(p, campaign.Id);
        var approvedEarlier = await kit.SubmitOkAsync(p, campaign.Id);
        var appealed = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(a, approvedEarlier, "Approve")).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(a, appealed, "Reject", "Product not visible")).EnsureSuccessStatusCode();
        (await AppealAsync(p, appealed)).EnsureSuccessStatusCode();

        await SetUserStatusAsync(p.User.Id, UserStatus.Suspended);

        await (await CampaignTestKit.DecideAsync(a, toApprove, "Approve")).ShouldFailAsync(409, "participant.not_active");
        Assert.Empty(await kit.EarningsAsync(toApprove));
        Assert.Equal(SubmissionStatus.Pending, await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == toApprove).Select(s => s.Status).FirstAsync()));
        (await CampaignTestKit.DecideAsync(a, toReject, "Reject", "Account under investigation")).EnsureSuccessStatusCode();

        var appeal = await AppealItemAsync(b, appealed);
        await (await ResolveAsync(b, appeal, "Overturned", "Product is visible")).ShouldFailAsync(409, "participant.not_active");
        Assert.Empty(await kit.EarningsAsync(appealed));
        (await ResolveAsync(b, appeal, "Upheld", "Decision stands")).EnsureSuccessStatusCode();

        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{approvedEarlier}/reverse",
            new { reason = "Fraud investigation", confirm = true })).EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------------ M3: overturn limits

    [Fact]
    public async Task Overturn_rechecks_the_submission_limit_and_archived_campaigns()
    {
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();

        var limited = await CampaignAsync(body => body["maxSubmissionsPerParticipant"] = 1);
        var p = await kit.ParticipantAsync();
        var rejected = await kit.SubmitOkAsync(p, limited.Id);
        (await CampaignTestKit.DecideAsync(a, rejected, "Reject", "Product not visible")).EnsureSuccessStatusCode();
        (await AppealAsync(p, rejected)).EnsureSuccessStatusCode();
        await kit.SubmitOkAsync(p, limited.Id); // allowed: the rejected one no longer counts
        var appeal = await AppealItemAsync(b, rejected);
        await (await ResolveAsync(b, appeal, "Overturned", "Product is visible")).ShouldFailAsync(409, "appeal.submission_limit_reached");
        Assert.Empty(await kit.EarningsAsync(rejected));

        var archived = await CampaignAsync();
        var q = await kit.ParticipantAsync();
        var qs = await kit.SubmitOkAsync(q, archived.Id);
        (await CampaignTestKit.DecideAsync(a, qs, "Reject", "Product not visible")).EnsureSuccessStatusCode();
        (await AppealAsync(q, qs)).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Campaigns.Campaign>().Where(c => c.Id == archived.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, OptimizeAll.Domain.Campaigns.CampaignStatus.Archived)));
        await (await ResolveAsync(b, await AppealItemAsync(b, qs), "Overturned", "Product is visible")).ShouldFailAsync(409, "appeal.campaign_archived");
        Assert.Empty(await kit.EarningsAsync(qs));
    }

    // ------------------------------------------------------------------ L1 / M1: reasons

    [Fact]
    public async Task Reasons_are_validated_after_trimming()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var id = await kit.SubmitOkAsync(p, campaign.Id);
        var approved = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();

        await (await CampaignTestKit.DecideAsync(a, id, "Reject", "    ab     ")).ShouldFailAsync(400, "review.reason_too_short");
        await (await CampaignTestKit.DecideAsync(a, id, "Reject", "      ")).ShouldFailAsync(400, "review.reason_required");

        (await CampaignTestKit.DecideAsync(a, approved, "Approve")).EnsureSuccessStatusCode();
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{approved}/reverse", new { reason = "   abc     ", confirm = true }))
            .ShouldFailAsync(400, "review.reason_too_short");

        (await CampaignTestKit.DecideAsync(a, id, "Reject", "Product not visible")).EnsureSuccessStatusCode();
        (await AppealAsync(p, id)).EnsureSuccessStatusCode();
        await (await ResolveAsync(b, await AppealItemAsync(b, id), "Upheld", "  ok      ")).ShouldFailAsync(400, "review.reason_too_short");
    }

    [Fact]
    public async Task Maximum_length_reasons_fit_on_every_path()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(baseAmount: 4m);
        body["rewardRules"] = new Dictionary<string, object?>
        {
            ["currency"] = "USD", ["dailyCapPerParticipant"] = 6m, ["rules"] = new object[] { new { type = "BaseRate", amount = 4m } },
        };
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var p = await kit.ParticipantAsync();
        var s1 = await kit.SubmitOkAsync(p, campaign.Id);
        var s2 = await kit.SubmitOkAsync(p, campaign.Id);
        var s3 = await kit.SubmitOkAsync(p, campaign.Id);
        var s4 = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var max = new string('m', 900);

        await (await CampaignTestKit.DecideAsync(a, s1, "Reject", max + "x")).ShouldFailAsync(400);
        (await CampaignTestKit.DecideAsync(a, s1, "Approve", max)).EnsureSuccessStatusCode();
        // Capped approval: "{900 chars} (caps applied: daily_cap)" is composed and must be cut to the column.
        (await CampaignTestKit.DecideAsync(a, s2, "Approve", max)).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(a, s3, "Reject", max)).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(a, s4, "RequestCorrection", max)).EnsureSuccessStatusCode();
        (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{s1}/reverse", new { reason = max, confirm = true })).EnsureSuccessStatusCode();

        (await AppealAsync(p, s3, new string('a', 2000))).EnsureSuccessStatusCode();
        await (await ResolveAsync(b, await AppealItemAsync(b, s3), "Overturned", max + "x")).ShouldFailAsync(400);
        (await ResolveAsync(b, await AppealItemAsync(b, s3), "Overturned", max)).EnsureSuccessStatusCode();
        (await AppealAsync(p, s1, new string('b', 2000))).EnsureSuccessStatusCode();
        (await ResolveAsync(b, await AppealItemAsync(b, s1), "Upheld", max)).EnsureSuccessStatusCode();

        var ids = new[] { s1, s2, s3, s4 };
        var reasons = await api.WithDbAsync(db => db.Set<SubmissionEvent>().Where(e => ids.Contains(e.SubmissionId) && e.Reason != null)
            .Select(e => new { e.Action, e.Reason }).ToListAsync());
        Assert.All(reasons, r => Assert.True(r.Reason!.Length <= 1000, r.Action));
        Assert.Contains(reasons, r => r.Action == "approved" && r.Reason!.StartsWith(max) && r.Reason.EndsWith("(caps applied: daily_cap)"));
        Assert.Contains(reasons, r => r.Action == "appealed" && r.Reason!.Length == 1000 && r.Reason.EndsWith("…"));
        var decisionReasons = await api.WithDbAsync(db => db.Set<Submission>().Where(s => ids.Contains(s.Id)).Select(s => s.DecisionReason).ToListAsync());
        Assert.All(decisionReasons, r => Assert.True((r?.Length ?? 0) <= 1000));
        Assert.Equal(SubmissionStatus.Approved, await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == s3).Select(s => s.Status).FirstAsync()));
    }

    // ------------------------------------------------------------------ H1: caps by submission day

    [Fact]
    public async Task Daily_cap_counts_the_submission_day_not_the_declared_post_day()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(baseAmount: 4m);
        body["startsAt"] = kit.Now.AddDays(-5);
        body["rewardRules"] = new Dictionary<string, object?>
        {
            ["currency"] = "USD", ["dailyCapPerParticipant"] = 6m, ["rules"] = new object[] { new { type = "BaseRate", amount = 4m } },
        };
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var p = await kit.ParticipantAsync();
        // Declared on different days, submitted the same day: they share one daily cap.
        var s1 = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddDays(-3));
        var s2 = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddMinutes(-5));
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, s1, "Approve")).EnsureSuccessStatusCode();
        var second = await (await CampaignTestKit.DecideAsync(reviewer, s2, "Approve")).ReadJsonAsync();
        Assert.Equal(2m, second.GetProperty("reward").GetProperty("total").GetDecimal());
        Assert.Equal("daily_cap", second.GetProperty("reward").GetProperty("appliedCaps")[0].GetString());
    }

    // ------------------------------------------------------------------ L5: first-post bonus after a reversal

    [Fact]
    public async Task Reversed_first_post_bonus_can_be_earned_again_exactly_once_under_concurrency()
    {
        var campaign = await CampaignAsync(null, new { type = "FirstPostBonus", amount = 3m });
        var p = await kit.ParticipantAsync();
        var s1 = await kit.SubmitOkAsync(p, campaign.Id);
        var s2 = await kit.SubmitOkAsync(p, campaign.Id);
        var s3 = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(a, s1, "Approve")).EnsureSuccessStatusCode();
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        (await finance.PostAsJsonAsync($"/api/v1/review/submissions/{s1}/reverse", new { reason = "Post was deleted", confirm = true })).EnsureSuccessStatusCode();

        var stamp2 = await CampaignTestKit.StampAsync(a, s2);
        var stamp3 = await CampaignTestKit.StampAsync(b, s3);
        var results = await Task.WhenAll(
            CampaignTestKit.DecideAsync(a, s2, "Approve", stamp: stamp2),
            CampaignTestKit.DecideAsync(b, s3, "Approve", stamp: stamp3));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bonuses = await api.WithDbAsync(db => db.Set<EarningEntry>()
            .Where(e => e.UserId == p.User.Id && e.Type == EarningType.FirstPostBonus).ToListAsync());
        Assert.Equal(2, bonuses.Count);
        Assert.Equal(EarningStatus.Reversed, Assert.Single(bonuses, e => e.IdempotencyKey == $"firstpost:{campaign.Id}:{p.User.Id}").Status);
        var again = Assert.Single(bonuses, e => e.IdempotencyKey == $"firstpost:{campaign.Id}:{p.User.Id}:1");
        Assert.Equal(EarningStatus.Approved, again.Status);
        Assert.Contains(again.SubmissionId!.Value, new[] { s2, s3 });
    }

    // ------------------------------------------------------------------ M2: budget query index

    [Fact]
    public async Task Budget_spent_query_can_use_the_campaign_status_index()
    {
        var campaign = await CampaignAsync();
        var plan = await api.WithDbAsync(async db =>
        {
            var sql = OptimizeAll.Api.Modules.Rewards.RewardQuoteService.SpentQuery(db, campaign.Id).Select(e => e.Amount).ToQueryString();
            Assert.Contains("IN (", sql);
            Assert.DoesNotContain("JSON_TABLE", sql, StringComparison.OrdinalIgnoreCase);
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // ToQueryString declares parameters as SET statements; run them, then EXPLAIN the SELECT.
            var statements = sql.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var set in statements[..^1])
            {
                cmd.CommandText = set;
                await cmd.ExecuteNonQueryAsync();
            }
            cmd.CommandText = "EXPLAIN " + statements[^1];
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return reader["possible_keys"] as string ?? string.Empty;
        });
        Assert.Contains("IX_earning_entries_CampaignId_Status", plan);
    }

    // ------------------------------------------------------------------ portal gaps

    [Fact]
    public async Task Detail_includes_participant_status_and_quality_bonus_max_of_the_recorded_rule_set()
    {
        var withBonus = await CampaignAsync(null, new { type = "QualityBonus", amount = 7.5m });
        var without = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var bonusId = await kit.SubmitOkAsync(p, withBonus.Id);
        var plainId = await kit.SubmitOkAsync(p, without.Id);
        var (_, reviewer) = await kit.ReviewerAsync();

        var detail = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{bonusId}");
        Assert.Equal(7.5m, detail.GetProperty("qualityBonusMax").GetDecimal());
        Assert.Equal("Active", detail.GetProperty("participant").GetProperty("status").GetString());

        var plain = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{plainId}");
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("qualityBonusMax").ValueKind);

        await SetUserStatusAsync(p.User.Id, UserStatus.Suspended);
        detail = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{bonusId}");
        Assert.Equal("Suspended", detail.GetProperty("participant").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Queue_filters_to_submissions_claimed_by_me()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var mine = await kit.SubmitOkAsync(p, campaign.Id);
        var theirs = await kit.SubmitOkAsync(p, campaign.Id);
        var open = await kit.SubmitOkAsync(p, campaign.Id);
        var (_, a) = await kit.ReviewerAsync();
        var (_, b) = await kit.ReviewerAsync();
        (await a.PostAsync($"/api/v1/review/submissions/{mine}/claim", null)).EnsureSuccessStatusCode();
        (await b.PostAsync($"/api/v1/review/submissions/{theirs}/claim", null)).EnsureSuccessStatusCode();

        var queue = await CampaignTestKit.GetJsonAsync(a, $"/api/v1/review/queue?campaignId={campaign.Id}&claimedByMe=true");
        Assert.Equal(new[] { mine }, queue.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray());

        var all = await CampaignTestKit.GetJsonAsync(a, $"/api/v1/review/queue?campaignId={campaign.Id}");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Contains(open, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));

        // An expired claim is no longer "mine".
        await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == mine)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClaimExpiresAt, kit.Now.AddMinutes(-1))));
        queue = await CampaignTestKit.GetJsonAsync(a, $"/api/v1/review/queue?campaignId={campaign.Id}&claimedByMe=true");
        Assert.Equal(0, queue.GetProperty("total").GetInt32());
    }
}

/// <summary>Live checks advance the shared clock, so they get their own database/host.</summary>
public sealed class LiveCheckTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    [Fact]
    public async Task Live_check_keeps_earnings_pending_until_confirmed_or_reverses_when_removed()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody(extraRules: new object[] { new { type = "QualityBonus", amount = 3m, approvalMode = "ManualApproval" } });
        body["minPostLiveHours"] = 48;
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var reviewerUser = await api.CreateUserAsync(new[] { Role.Reviewer });
        var reviewer = await api.LoginAsync(reviewerUser);

        var p = await kit.ParticipantAsync();
        var kept = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddHours(-1));
        var removed = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddHours(-1));

        var approved = await (await CampaignTestKit.DecideAsync(reviewer, kept, "Approve", quality: 3m)).ReadJsonAsync();
        Assert.Equal("Pending", approved.GetProperty("liveCheckStatus").GetString());
        (await CampaignTestKit.DecideAsync(reviewer, removed, "Approve")).EnsureSuccessStatusCode();
        Assert.All(await kit.EarningsAsync(kept), e => Assert.Equal(EarningStatus.PendingApproval, e.Status));

        // Not due yet.
        var due = await CampaignTestKit.GetJsonAsync(reviewer, "/api/v1/review/live-checks?due=true");
        Assert.Equal(0, due.GetProperty("total").GetInt32());
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{kept}/live-check", new { result = "ConfirmedLive" }))
            .ShouldFailAsync(409, "review.live_check_not_due");

        api.Clock.Advance(TimeSpan.FromHours(48));
        reviewer = await api.LoginAsync(reviewerUser);
        due = await CampaignTestKit.GetJsonAsync(reviewer, "/api/v1/review/live-checks?due=true");
        Assert.Equal(2, due.GetProperty("total").GetInt32());

        await kit.RunJobAsync<LiveCheckReminderJob>();
        await kit.RunJobAsync<LiveCheckReminderJob>();
        var reminders = await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n => n.UserId == reviewerUser.Id && n.Type == NotificationTypes.ReviewLiveCheckDue));
        Assert.Equal(1, reminders);

        var confirmed = await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{kept}/live-check",
            new { result = "ConfirmedLive", note = "Still visible" })).ReadJsonAsync();
        Assert.Equal("ConfirmedLive", confirmed.GetProperty("liveCheckStatus").GetString());
        var keptEarnings = await kit.EarningsAsync(kept);
        Assert.Equal(EarningStatus.Approved, keptEarnings.Single(e => e.Type == EarningType.PostReward).Status);
        // Manual-approval bonuses stay pending for finance.
        Assert.Equal(EarningStatus.PendingApproval, keptEarnings.Single(e => e.Type == EarningType.QualityBonus).Status);
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{kept}/live-check", new { result = "ConfirmedLive" }))
            .ShouldFailAsync(409, "review.live_check_not_pending");

        var gone = await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{removed}/live-check",
            new { result = "Removed", note = "Post was deleted" })).ReadJsonAsync();
        Assert.Equal("Reversed", gone.GetProperty("status").GetString());
        var removedEarnings = await kit.EarningsAsync(removed);
        Assert.Contains(removedEarnings, e => e.Type == EarningType.Reversal);
        Assert.All(removedEarnings.Where(e => e.Type != EarningType.Reversal), e => Assert.Equal(EarningStatus.Reversed, e.Status));
    }

    [Fact]
    public async Task Live_check_due_time_is_measured_from_the_submission_not_a_backdated_post()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody();
        body["minPostLiveHours"] = 48;
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var reviewerUser = await api.CreateUserAsync(new[] { Role.Reviewer });
        var reviewer = await api.LoginAsync(reviewerUser);
        var p = await kit.ParticipantAsync();
        var submittedAt = kit.Now;
        var id = await kit.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddHours(-47));

        var approved = await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        var due = approved.GetProperty("liveCheckDueAt").GetDateTime().ToUniversalTime();
        Assert.True(due >= submittedAt.AddHours(48) - TimeSpan.FromSeconds(1), $"due {due:O}");

        api.Clock.Advance(TimeSpan.FromHours(2)); // PostedAt + 48h has passed, SubmittedAt + 48h has not
        reviewer = await api.LoginAsync(reviewerUser);
        await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{id}/live-check", new { result = "ConfirmedLive" }))
            .ShouldFailAsync(409, "review.live_check_not_due");
    }

    [Fact]
    public async Task Suspended_participant_live_check_cannot_be_confirmed_and_long_removal_notes_fit()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody();
        body["minPostLiveHours"] = 24;
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var reviewerUser = await api.CreateUserAsync(new[] { Role.Reviewer });
        var reviewer = await api.LoginAsync(reviewerUser);
        var p = await kit.ParticipantAsync();
        var kept = await kit.SubmitOkAsync(p, campaign.Id);
        var removed = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, kept, "Approve")).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(reviewer, removed, "Approve")).EnsureSuccessStatusCode();

        api.Clock.Advance(TimeSpan.FromHours(25));
        reviewer = await api.LoginAsync(reviewerUser);
        await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == p.User.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Suspended)));

        await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{kept}/live-check", new { result = "ConfirmedLive" }))
            .ShouldFailAsync(409, "participant.not_active");
        Assert.All(await kit.EarningsAsync(kept), e => Assert.Equal(EarningStatus.PendingApproval, e.Status));

        await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{removed}/live-check", new { result = "Removed", note = "   ab   " }))
            .ShouldFailAsync(400, "review.reason_too_short");
        var gone = await (await reviewer.PostAsJsonAsync($"/api/v1/review/submissions/{removed}/live-check",
            new { result = "Removed", note = new string('n', 900) })).ReadJsonAsync();
        Assert.Equal("Reversed", gone.GetProperty("status").GetString());
        var reason = await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == removed).Select(s => s.DecisionReason!).FirstAsync());
        Assert.True(reason.Length <= 1000);
        Assert.Equal("Post removed before the minimum live duration: " + new string('n', 900), reason);
    }
}
