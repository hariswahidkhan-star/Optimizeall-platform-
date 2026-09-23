using System.Net.Http.Json;
using OptimizeAll.Api.Modules.Review;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Payouts;

namespace OptimizeAll.IntegrationTests.Review;

/// <summary>L2: reversing a submission whose earnings are already in a payout batch.</summary>
public sealed class ReversalOfScheduledEarningsTests : FreshDatabaseTest
{
    private async Task<(Guid BatchId, Guid SubmissionId, TestUser Author, EarningEntry Reward, EarningEntry Unrelated, TestUser Bystander)> ScheduledAsync()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var author = await Api.ParticipantAsync();
        var bystander = await Api.ParticipantAsync();
        var submissionId = await Api.SubmissionAsync(author.Id, SubmissionStatus.Approved, 20m);
        var reward = await Api.EarnAsync(author.Id, 20m, submissionId: submissionId);
        var unrelated = await Api.EarnAsync(author.Id, 15m);
        await Api.EarnAsync(bystander.Id, 30m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(reward.Id)).Status);
        return (batchId, submissionId, author, reward, unrelated, bystander);
    }

    private static Task<HttpResponseMessage> ReverseAsync(HttpClient client, Guid submissionId) =>
        client.PostAsJsonAsync($"/api/v1/review/submissions/{submissionId}/reverse", new { reason = "Bought engagement detected", confirm = true });

    [Fact]
    public async Task Reversing_a_submission_whose_earning_is_in_a_draft_batch_holds_the_item_and_reverses_it()
    {
        var (batchId, submissionId, author, reward, unrelated, bystander) = await ScheduledAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);

        var reversed = await (await ReverseAsync(finance, submissionId)).ReadJsonAsync();
        Assert.Equal("Reversed", reversed.Str("status"));

        var items = await Api.ItemsAsync(batchId);
        var authorItem = items.Single(i => i.UserId == author.Id);
        Assert.Equal(PayoutItemStatus.Held, authorItem.Status);
        Assert.Equal(ReviewService.ReversalHoldReason, authorItem.HoldReason);
        Assert.Equal(PayoutItemStatus.Pending, items.Single(i => i.UserId == bystander.Id).Status);
        var storedReward = await Api.EarningAsync(reward.Id);
        Assert.Equal(EarningStatus.Reversed, storedReward.Status);
        Assert.Null(storedReward.PayoutItemId);
        var storedUnrelated = await Api.EarningAsync(unrelated.Id);
        Assert.Equal(EarningStatus.Approved, storedUnrelated.Status);
        Assert.Null(storedUnrelated.PayoutItemId);

        var batch = await finance.BatchAsync(batchId);
        Assert.Equal(30m, batch.GetProperty("batch").Dec("totalAmount"));
        Assert.True((await finance.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}/reconciliation")).GetProperty("isBalanced").GetBoolean());
        // Finalizing afterwards pays only the bystander.
        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);
        var finalized = await finalizer.FinalizeAsync(batchId);
        Assert.Single(finalized.GetProperty("dispatch").EnumerateArray());
    }

    [Fact]
    public async Task Reversing_a_submission_whose_earning_is_in_a_finalized_batch_tells_the_reviewer_to_have_finance_fail_the_item()
    {
        var (batchId, submissionId, author, reward, _, _) = await ScheduledAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);

        var response = await ReverseAsync(finance, submissionId);
        var text = await response.Content.ReadAsStringAsync();
        await response.ShouldFailAsync(409, "ledger.in_payout_batch");
        Assert.Contains("mark the participant's payout item", text);
        Assert.Contains("failed first", text);
        Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(reward.Id)).Status);

        // After finance marks the item failed, the reversal goes through.
        var item = (await Api.ItemsAsync(batchId)).Single(i => i.UserId == author.Id);
        await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/mark-failed",
            new { reason = "Held back: submission under reversal" });
        (await ReverseAsync(finance, submissionId)).EnsureSuccessStatusCode();
        Assert.Equal(EarningStatus.Reversed, (await Api.EarningAsync(reward.Id)).Status);
    }
}
