using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Review;
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
        // A fresh post reward; the first-post bonus is never paid twice.
        Assert.Equal(EarningType.PostReward, Assert.Single(live).Type);
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
}
