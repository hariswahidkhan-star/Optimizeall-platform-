using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Marketing.Referrals;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Settings;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Payouts;

namespace OptimizeAll.IntegrationTests.Marketing;

/// <summary>H3: a rejected referral's reward is undone whatever its payout state (paid, scheduled in a draft or finalized batch).</summary>
public sealed class ReferralRewardReversalTests : FreshDatabaseTest
{
    private const string Batches = "/api/v1/finance/payout-batches";

    private sealed record Rewarded(TestUser Referrer, TestUser Referred, Guid ReferralId, EarningEntry Reward, EarningEntry Other);

    /// <summary>A referrer with an auto-approved 5 USD referral reward plus a 20 USD post reward (above the payout minimum).</summary>
    private async Task<Rewarded> RewardedReferralAsync()
    {
        await Api.SetSettingAsync(SettingKeys.ReferralProgram, new ReferralProgramSettings
        {
            Enabled = true, ReferrerRewardAmount = 5m, Currency = "USD", RequireManualApproval = false,
        });
        var referrer = await Api.ParticipantAsync();
        var other = await Api.EarnAsync(referrer.Id, 20m);
        var referred = await Api.CreateUserAsync();
        var code = await Api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == referrer.Id).Select(u => u.ReferralCode).FirstAsync());
        await Api.PublishAsync(new UserRegistered(referred.Id, code, null, "iphash-" + Guid.NewGuid().ToString("N")[..20], null, Api.Now()));
        await Api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), referred.Id, Guid.NewGuid(), true, Api.Now()));
        var referral = await Api.WithDbAsync(db => db.Set<Referral>().AsNoTracking().SingleAsync(r => r.ReferredUserId == referred.Id));
        var reward = await Api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().SingleAsync(e => e.ReferralId == referral.Id));
        Assert.Equal(EarningStatus.Approved, reward.Status);
        return new Rewarded(referrer, referred, referral.Id, reward, other);
    }

    private Task<Referral> ReferralAsync(Guid id) =>
        Api.WithDbAsync(db => db.Set<Referral>().AsNoTracking().SingleAsync(r => r.Id == id));

    [Fact]
    public async Task Rejecting_a_referral_whose_reward_is_in_a_draft_batch_holds_the_item_and_reverses_the_reward()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var r = await RewardedReferralAsync();
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await finance.PrepareAsync()).GetProperty("batch").Id();
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        Assert.Equal(item.Id, (await Api.EarningAsync(r.Reward.Id)).PayoutItemId);

        var (_, manager) = await Api.CreateClientAsync(Role.CampaignManager);
        var result = await manager.PostJsonAsync($"/api/v1/marketing/referrals/{r.ReferralId}/reject", new { reason = "Same household as the referrer" });
        Assert.Equal("Rejected", result.Str("status"));
        Assert.Equal("held_and_reversed", result.Str("rewardAction"));

        var heldItem = Assert.Single(await Api.ItemsAsync(batchId));
        Assert.Equal(PayoutItemStatus.Held, heldItem.Status);
        Assert.Equal(ReferralService.RewardReversalHoldReason, heldItem.HoldReason);
        var reward = await Api.EarningAsync(r.Reward.Id);
        Assert.Equal(EarningStatus.Reversed, reward.Status);
        Assert.Null(reward.PayoutItemId);
        var other = await Api.EarningAsync(r.Other.Id);
        Assert.Equal(EarningStatus.Approved, other.Status); // released for a later batch
        Assert.Null(other.PayoutItemId);
        var referral = await ReferralAsync(r.ReferralId);
        Assert.Equal(ReferralStatus.Rejected, referral.Status);
        Assert.Equal("Same household as the referrer", referral.RejectionReason);

        var report = await finance.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.True(report.GetProperty("isBalanced").GetBoolean(), report.ToString());
    }

    [Fact]
    public async Task Reversal_of_the_qualifying_submission_while_the_reward_is_in_a_finalized_batch_flags_it_for_review()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var r = await RewardedReferralAsync();
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));

        await Api.PublishAsync(new SubmissionReversed(Guid.NewGuid(), r.Referred.Id, Guid.NewGuid(), "Post removed", Api.Now()));

        var referral = await ReferralAsync(r.ReferralId);
        Assert.Equal(ReferralStatus.Rejected, referral.Status);
        Assert.Equal(ReferralService.QualifyingSubmissionReversedReason, referral.RejectionReason);
        Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(r.Reward.Id)).Status); // frozen batch: untouched
        var note = await Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == "referral.reward_pending_reversal" && a.EntityId == r.Reward.Id.ToString()));
        Assert.Contains("pendingReversal", note.AfterJson);

        var report = await finance.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.True(report.GetProperty("isBalanced").GetBoolean(), report.ToString()); // a warning, not an error
        var warning = Assert.Single(report.GetProperty("discrepancies").EnumerateArray(), d => d.Str("type") == "pending_reversal");
        Assert.Equal("warning", warning.Str("severity"));
        Assert.Equal(item.Id, warning.Id("itemId"));
        Assert.Equal(r.Reward.Id, warning.Id("earningId"));

        // Finance pays the item, then reverses the paid reward (a clawback) and the flag clears.
        await finance.PostJsonAsync($"{Batches}/{batchId}/items/{item.Id}/record-payment", new { paymentReference = "WIRE-REF-1", paidAt = Api.Now() });
        var reversal = await finance.PostJsonAsync($"/api/v1/finance/earnings/{r.Reward.Id}/reverse",
            new { reason = "Referral rejected: qualifying post removed", confirm = true });
        Assert.Equal(-5m, reversal.GetProperty("reversal").Dec("settlementAmount"));
        var after = await finance.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.DoesNotContain(after.GetProperty("discrepancies").EnumerateArray(), d => d.Str("type") == "pending_reversal");
    }

    [Fact]
    public async Task Rejecting_a_referral_whose_reward_was_paid_claws_it_back()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var r = await RewardedReferralAsync();
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        await finance.PostJsonAsync($"{Batches}/{batchId}/items/{item.Id}/record-payment", new { paymentReference = "WIRE-REF-2", paidAt = Api.Now() });
        Assert.Equal(EarningStatus.Paid, (await Api.EarningAsync(r.Reward.Id)).Status);

        var (_, manager) = await Api.CreateClientAsync(Role.CampaignManager);
        var result = await manager.PostJsonAsync($"/api/v1/marketing/referrals/{r.ReferralId}/reject", new { reason = "Referred account is a duplicate" });
        Assert.Equal("clawback", result.Str("rewardAction"));
        Assert.Equal(ReferralStatus.Rejected, (await ReferralAsync(r.ReferralId)).Status);
        var leg = await Api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking()
            .SingleAsync(e => e.ReversesEntryId == r.Reward.Id));
        Assert.Equal(EarningType.Reversal, leg.Type);
        Assert.Equal(EarningStatus.Approved, leg.Status); // netted against the referrer's next payout
        Assert.Equal(-5m, leg.SettlementAmount);
    }
}
