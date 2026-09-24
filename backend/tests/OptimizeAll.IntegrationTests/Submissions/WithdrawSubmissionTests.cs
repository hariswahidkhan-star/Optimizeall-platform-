using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Submissions;

/// <summary>Participant withdrawal of an undecided submission (Pending / UnderReview / NeedsCorrection → Withdrawn).</summary>
public sealed class WithdrawSubmissionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    private async Task<CreatedCampaign> CampaignAsync(int maxSubmissions = 5)
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody();
        body["maxSubmissionsPerParticipant"] = maxSubmissions;
        return await kit.CreateCampaignAsync(manager, body);
    }

    private static Task<HttpResponseMessage> WithdrawAsync(HttpClient client, Guid id, bool confirm = true, string? reason = null) =>
        client.PostAsJsonAsync($"/api/v1/me/submissions/{id}/withdraw", new { confirm, reason });

    [Fact]
    public async Task Pending_submission_is_withdrawn_audited_leaves_the_queue_and_frees_the_slot_and_post()
    {
        var campaign = await CampaignAsync(maxSubmissions: 1);
        var p = await kit.ParticipantAsync();
        var url = CampaignTestKit.InstagramUrl();
        var id = await kit.SubmitOkAsync(p, campaign.Id, url);

        var before = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/me/submissions/{id}");
        Assert.True(before.GetProperty("canWithdraw").GetBoolean());

        var detail = await (await WithdrawAsync(p.Client, id, reason: "  Posted the wrong link  ")).ReadJsonAsync();
        Assert.Equal("Withdrawn", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canWithdraw").GetBoolean());
        var last = detail.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("withdrawn", last.GetProperty("action").GetString());
        Assert.Equal("You", last.GetProperty("actor").GetString());
        Assert.Equal("Posted the wrong link", last.GetProperty("reason").GetString());

        Assert.Empty(await kit.EarningsAsync(id));
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "submission.withdrawn" && a.EntityId == id.ToString())));

        // Reviewers no longer see it in the queue, and see why when they open it.
        var (_, reviewer) = await kit.ReviewerAsync();
        var queue = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/queue?campaignId={campaign.Id}");
        Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
        var review = await CampaignTestKit.GetJsonAsync(reviewer, $"/api/v1/review/submissions/{id}");
        Assert.Equal("Withdrawn", review.GetProperty("submission").GetProperty("status").GetString());
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ShouldFailAsync(409, "review.already_decided");
        await (await reviewer.PostAsync($"/api/v1/review/submissions/{id}/claim", null)).ShouldFailAsync(409, "review.already_decided");

        // The campaign slot (limit 1) and the post itself are free again.
        var again = await kit.SubmitAsync(p, campaign.Id, url);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Needs_correction_and_claimed_submissions_can_be_withdrawn()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var (_, reviewer) = await kit.ReviewerAsync();

        var corrected = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, corrected, "RequestCorrection", "Please add the hashtag")).EnsureSuccessStatusCode();
        Assert.Equal("Withdrawn", (await (await WithdrawAsync(p.Client, corrected)).ReadJsonAsync()).GetProperty("status").GetString());

        var claimed = await kit.SubmitOkAsync(p, campaign.Id);
        (await reviewer.PostAsync($"/api/v1/review/submissions/{claimed}/claim", null)).EnsureSuccessStatusCode();
        Assert.Equal("Withdrawn", (await (await WithdrawAsync(p.Client, claimed)).ReadJsonAsync()).GetProperty("status").GetString());
        var row = await api.WithDbAsync(db => db.Set<Submission>().AsNoTracking().FirstAsync(s => s.Id == claimed));
        Assert.Null(row.ClaimedByUserId);
    }

    [Fact]
    public async Task Decided_or_withdrawn_submissions_cannot_be_withdrawn_and_confirmation_is_required()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var (_, reviewer) = await kit.ReviewerAsync();

        var pending = await kit.SubmitOkAsync(p, campaign.Id);
        await (await WithdrawAsync(p.Client, pending, confirm: false)).ShouldFailAsync(400, "confirmation.required");
        var tooLong = await WithdrawAsync(p.Client, pending, reason: new string('x', 1001));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var approved = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, approved, "Approve")).EnsureSuccessStatusCode();
        await (await WithdrawAsync(p.Client, approved)).ShouldFailAsync(409, "submission.not_withdrawable");
        Assert.NotEmpty(await kit.EarningsAsync(approved));

        var rejected = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, rejected, "Reject", "Post is not public")).EnsureSuccessStatusCode();
        await (await WithdrawAsync(p.Client, rejected)).ShouldFailAsync(409, "submission.not_withdrawable");

        (await WithdrawAsync(p.Client, pending)).EnsureSuccessStatusCode();
        await (await WithdrawAsync(p.Client, pending)).ShouldFailAsync(409, "submission.not_withdrawable");

        // Not someone else's, and not for staff through the participant endpoint.
        var other = await kit.ParticipantAsync();
        var theirs = await kit.SubmitOkAsync(other, campaign.Id);
        await (await WithdrawAsync(p.Client, theirs)).ShouldFailAsync(404);
        await (await WithdrawAsync(reviewer, theirs)).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Withdrawal_racing_a_reviewer_decision_has_exactly_one_winner()
    {
        var campaign = await CampaignAsync();
        var p = await kit.ParticipantAsync();
        var (_, reviewer) = await kit.ReviewerAsync();
        for (var round = 0; round < 3; round++)
        {
            var id = await kit.SubmitOkAsync(p, campaign.Id);
            var stamp = await CampaignTestKit.StampAsync(reviewer, id);
            var results = await Task.WhenAll(
                CampaignTestKit.DecideAsync(reviewer, id, "Approve", stamp: stamp),
                WithdrawAsync(p.Client, id));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

            var status = await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == id).Select(s => s.Status).FirstAsync());
            var earnings = await kit.EarningsAsync(id);
            if (status == SubmissionStatus.Withdrawn)
                Assert.Empty(earnings);
            else
            {
                Assert.Equal(SubmissionStatus.Approved, status);
                Assert.Contains(earnings, e => e.Type == EarningType.PostReward);
            }
        }
    }
}
