using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Marketing.Referrals;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class ReferralTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private Task SetProgramAsync(bool manualApproval = true, int max = 50, string action = "FirstApprovedSubmission", bool enabled = true) =>
        api.SetSettingAsync(SettingKeys.ReferralProgram, new ReferralProgramSettings
        {
            Enabled = enabled, ReferrerRewardAmount = 5m, Currency = "USD", QualifyingAction = action, QualifyWithinDays = 60,
            RequireManualApproval = manualApproval, MaxRewardedReferralsPerUser = max,
        });

    private async Task<string> ReferralCodeAsync(Guid userId) =>
        await api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstAsync());

    private HttpClient Anonymous()
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        return client;
    }

    private async Task<Guid> RegisterAsync(string email, string? referralCode, string? deviceId = null, string? inviteCode = null)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, password = "Horizon-Tulip-42", displayName = "Zara Referred", countryCode = "PK", languageCode = "en",
            timeZone = "UTC", acceptTerms = true, referralCode, deviceId, inviteCode,
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await api.WithDbAsync(db => db.Set<User>().Where(u => u.NormalizedEmail == email.ToUpperInvariant()).Select(u => u.Id).FirstAsync());
    }

    /// <summary>Creates a referral through the UserRegistered event (as AuthService publishes it).</summary>
    private async Task<(TestUser Referrer, TestUser Referred, Referral Referral)> ReferralAsync(string? device = null)
    {
        var referrer = await api.CreateUserAsync();
        var referred = await api.CreateUserAsync();
        await api.PublishAsync(new UserRegistered(referred.Id, (await ReferralCodeAsync(referrer.Id)).ToLowerInvariant(), null,
            "iphash-" + Guid.NewGuid().ToString("N")[..20], device, api.Now()));
        var referral = await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.ReferredUserId == referred.Id));
        return (referrer, referred, referral);
    }

    private Task<List<EarningEntry>> RewardsAsync(Guid referrerId) =>
        api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.UserId == referrerId && e.Type == EarningType.ReferralReward).ToListAsync());

    [Fact]
    public async Task Registering_with_a_referral_code_creates_a_registered_referral()
    {
        await SetProgramAsync();
        var referrer = await api.CreateUserAsync();
        var code = await ReferralCodeAsync(referrer.Id);
        var referredId = await RegisterAsync($"ref-{Guid.NewGuid():N}@example.test", code.ToLowerInvariant(), "device-1");

        var referral = await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.ReferredUserId == referredId));
        Assert.Equal(referrer.Id, referral.ReferrerUserId);
        Assert.Equal(ReferralStatus.Registered, referral.Status);
        Assert.Equal(ReferralQualifyingAction.FirstApprovedSubmission, referral.QualifyingAction);
        Assert.Equal(code, referral.CodeUsed);
        Assert.NotNull(referral.DeviceHash);
        Assert.NotEqual("device-1", referral.DeviceHash); // only the hash is stored
        Assert.InRange(referral.QualifyBy, api.Now().AddDays(60).AddMinutes(-1), api.Now().AddDays(60).AddMinutes(1));
        Assert.Null(referral.FraudSignals);
        Assert.Empty(await RewardsAsync(referrer.Id)); // no reward at registration
    }

    [Fact]
    public async Task Unknown_code_self_referral_and_disabled_program_create_nothing()
    {
        await SetProgramAsync();
        var unknown = await RegisterAsync($"ref-{Guid.NewGuid():N}@example.test", "NOPE-NOT-A-CODE");
        Assert.False(await api.WithDbAsync(db => db.Set<Referral>().AnyAsync(r => r.ReferredUserId == unknown)));

        var self = await api.CreateUserAsync();
        await api.PublishAsync(new UserRegistered(self.Id, await ReferralCodeAsync(self.Id), null, null, null, api.Now()));
        Assert.False(await api.WithDbAsync(db => db.Set<Referral>().AnyAsync(r => r.ReferredUserId == self.Id)));

        await SetProgramAsync(enabled: false);
        var referrer = await api.CreateUserAsync();
        var off = await RegisterAsync($"ref-{Guid.NewGuid():N}@example.test", await ReferralCodeAsync(referrer.Id));
        Assert.False(await api.WithDbAsync(db => db.Set<Referral>().AnyAsync(r => r.ReferredUserId == off)));
        await SetProgramAsync();
    }

    [Fact]
    public async Task Fraud_signals_are_recorded_for_shared_device_alias_and_disposable_email()
    {
        await SetProgramAsync();
        var referrer = await api.CreateUserAsync(email: $"owner{Guid.NewGuid():N}@example.test");
        var code = await ReferralCodeAsync(referrer.Id);

        var first = await RegisterAsync($"a-{Guid.NewGuid():N}@example.test", code, "shared-device");
        var second = await RegisterAsync($"b-{Guid.NewGuid():N}@mailinator.com", code, "shared-device");
        var alias = await RegisterAsync(referrer.Email.Replace("@", "+promo@"), code, "other-device");

        var referrals = await api.WithDbAsync(db => db.Set<Referral>().Where(r => r.ReferrerUserId == referrer.Id).ToListAsync());
        Assert.Null(referrals.Single(r => r.ReferredUserId == first).FraudSignals);
        var secondSignals = ReferralFraudRules.Split(referrals.Single(r => r.ReferredUserId == second).FraudSignals);
        Assert.Contains(ReferralFraudSignals.SharedDevice, secondSignals);
        Assert.Contains(ReferralFraudSignals.DisposableEmail, secondSignals);
        Assert.Contains(ReferralFraudSignals.EmailAlias, ReferralFraudRules.Split(referrals.Single(r => r.ReferredUserId == alias).FraudSignals));

        // Marketing can filter flagged referrals.
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var flagged = await (await manager.GetAsync($"/api/v1/marketing/referrals?flagged=true&search={Uri.EscapeDataString(referrer.Email)}")).ReadJsonAsync();
        Assert.Equal(2, flagged.GetProperty("total").GetInt32());
        Assert.All(flagged.GetProperty("items").EnumerateArray(), i => Assert.NotEmpty(i.GetProperty("fraudSignals").EnumerateArray()));
    }

    [Fact]
    public async Task Reward_is_created_only_after_the_qualifying_action_and_duplicate_events_are_idempotent()
    {
        await SetProgramAsync();
        var (referrer, referred, referral) = await ReferralAsync();

        // Email verification is not the qualifying action here.
        await api.PublishAsync(new EmailVerified(referred.Id, api.Now()));
        Assert.Empty(await RewardsAsync(referrer.Id));

        var approved = new SubmissionApproved(Guid.NewGuid(), referred.Id, Guid.NewGuid(), true, api.Now());
        await api.PublishAsync(approved);
        await api.PublishAsync(approved); // duplicate delivery
        await Task.WhenAll(api.PublishAsync(approved), api.PublishAsync(approved)); // concurrent duplicates

        var rewards = await RewardsAsync(referrer.Id);
        var reward = Assert.Single(rewards);
        Assert.Equal(EarningStatus.PendingApproval, reward.Status);
        Assert.Equal(5m, reward.Amount);
        Assert.Equal(referral.Id, reward.ReferralId);
        Assert.Equal($"referral:{referral.Id}", reward.IdempotencyKey);

        var stored = await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.Id == referral.Id));
        Assert.Equal(ReferralStatus.Qualified, stored.Status);
        Assert.Equal(reward.Id, stored.EarningEntryId);
        Assert.NotNull(stored.QualifiedAt);
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Notification>().CountAsync(n =>
            n.UserId == referrer.Id && n.Type == NotificationTypes.ReferralQualified)));
        Assert.True(await api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d =>
            d.UserId == referrer.Id && d.Channel == NotificationChannel.Email)));
    }

    [Fact]
    public async Task Fraud_signals_force_manual_approval_even_when_auto_approval_is_configured()
    {
        await SetProgramAsync(manualApproval: false);
        var clean = await ReferralAsync();
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), clean.Referred.Id, Guid.NewGuid(), true, api.Now()));
        Assert.Equal(EarningStatus.Approved, Assert.Single(await RewardsAsync(clean.Referrer.Id)).Status);

        var referrer = await api.CreateUserAsync();
        var code = await ReferralCodeAsync(referrer.Id);
        var a = await api.CreateUserAsync();
        var b = await api.CreateUserAsync();
        await api.PublishAsync(new UserRegistered(a.Id, code, null, null, "same-device-hash", api.Now()));
        await api.PublishAsync(new UserRegistered(b.Id, code, null, null, "same-device-hash", api.Now()));
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), b.Id, Guid.NewGuid(), true, api.Now()));
        Assert.Equal(EarningStatus.PendingApproval, Assert.Single(await RewardsAsync(referrer.Id)).Status);
        await SetProgramAsync();
    }

    [Fact]
    public async Task Email_verified_and_first_payout_qualifying_actions()
    {
        await SetProgramAsync(action: "EmailVerified");
        var viaEmail = await ReferralAsync();
        Assert.Equal(ReferralQualifyingAction.EmailVerified, viaEmail.Referral.QualifyingAction);
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), viaEmail.Referred.Id, Guid.NewGuid(), true, api.Now()));
        Assert.Empty(await RewardsAsync(viaEmail.Referrer.Id));
        await api.PublishAsync(new EmailVerified(viaEmail.Referred.Id, api.Now()));
        Assert.Single(await RewardsAsync(viaEmail.Referrer.Id));

        await SetProgramAsync(action: "FirstPaidPayout");
        var viaPayout = await ReferralAsync();
        await api.PublishAsync(new PayoutItemPaid(Guid.NewGuid(), viaPayout.Referred.Id, 12m, "USD", api.Now()));
        await api.PublishAsync(new PayoutItemPaid(Guid.NewGuid(), viaPayout.Referred.Id, 15m, "USD", api.Now()));
        Assert.Single(await RewardsAsync(viaPayout.Referrer.Id));
        await SetProgramAsync();
    }

    [Fact]
    public async Task Reward_cap_qualifies_without_reward()
    {
        await SetProgramAsync(max: 1);
        var referrer = await api.CreateUserAsync();
        var code = await ReferralCodeAsync(referrer.Id);
        var a = await api.CreateUserAsync();
        var b = await api.CreateUserAsync();
        await api.PublishAsync(new UserRegistered(a.Id, code, null, null, null, api.Now()));
        await api.PublishAsync(new UserRegistered(b.Id, code, null, null, null, api.Now()));
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), a.Id, Guid.NewGuid(), true, api.Now()));
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), b.Id, Guid.NewGuid(), true, api.Now()));

        Assert.Single(await RewardsAsync(referrer.Id));
        var capped = await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.ReferredUserId == b.Id));
        Assert.Equal(ReferralStatus.Qualified, capped.Status);
        Assert.Null(capped.EarningEntryId);
        Assert.Contains("cap reached", capped.RejectionReason);
        await SetProgramAsync();
    }

    [Fact]
    public async Task Reversal_of_the_only_approved_submission_reverses_the_unpaid_reward()
    {
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(manager.Id);

        foreach (var manual in new[] { true, false })
        {
            await SetProgramAsync(manualApproval: manual);
            var (referrer, referred, referral) = await ReferralAsync();
            var account = await api.CreateSocialAccountAsync(referred.Id);
            var submission = await api.CreateSubmissionAsync(referred.Id, campaign.Id, account, SubmissionStatus.Approved);
            await api.PublishAsync(new SubmissionApproved(submission.Id, referred.Id, campaign.Id, true, api.Now()));
            var reward = Assert.Single(await RewardsAsync(referrer.Id));
            Assert.Equal(manual ? EarningStatus.PendingApproval : EarningStatus.Approved, reward.Status);

            await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == submission.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubmissionStatus.Reversed)));
            var reversed = new SubmissionReversed(submission.Id, referred.Id, campaign.Id, "Post removed", api.Now());
            await api.PublishAsync(reversed);
            await api.PublishAsync(reversed); // idempotent

            var after = await api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.ReferralId == referral.Id).ToListAsync());
            var original = after.Single(e => e.Type == EarningType.ReferralReward);
            if (manual)
            {
                Assert.Equal(EarningStatus.Declined, original.Status);
                Assert.Single(after);
            }
            else
            {
                Assert.Equal(EarningStatus.Reversed, original.Status);
                var leg = after.Single(e => e.Type == EarningType.Reversal);
                Assert.Equal(-5m, leg.Amount);
                Assert.Equal(original.Id, leg.ReversesEntryId);
            }
            var stored = await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.Id == referral.Id));
            Assert.Equal(ReferralStatus.Rejected, stored.Status);
            Assert.Equal(ReferralService.QualifyingSubmissionReversedReason, stored.RejectionReason);
        }
        await SetProgramAsync();
    }

    [Fact]
    public async Task Reversal_keeps_the_reward_when_another_approved_submission_exists_or_it_was_paid()
    {
        await SetProgramAsync();
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var campaign = await api.CreateCampaignAsync(manager.Id);
        var (referrer, referred, referral) = await ReferralAsync();
        var account = await api.CreateSocialAccountAsync(referred.Id);
        var first = await api.CreateSubmissionAsync(referred.Id, campaign.Id, account, SubmissionStatus.Approved);
        await api.CreateSubmissionAsync(referred.Id, campaign.Id, account, SubmissionStatus.Approved);
        await api.PublishAsync(new SubmissionApproved(first.Id, referred.Id, campaign.Id, true, api.Now()));

        await api.WithDbAsync(db => db.Set<Submission>().Where(s => s.Id == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubmissionStatus.Reversed)));
        await api.PublishAsync(new SubmissionReversed(first.Id, referred.Id, campaign.Id, "Removed", api.Now()));

        Assert.Equal(EarningStatus.PendingApproval, Assert.Single(await RewardsAsync(referrer.Id)).Status);
        Assert.Equal(ReferralStatus.Qualified, (await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.Id == referral.Id))).Status);
    }

    [Fact]
    public async Task Expiry_job_expires_referrals_past_their_window_and_they_can_no_longer_qualify()
    {
        await SetProgramAsync();
        var (referrer, referred, referral) = await ReferralAsync();
        var (_, _, fresh) = await ReferralAsync();
        await api.WithDbAsync(db => db.Set<Referral>().Where(r => r.Id == referral.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.QualifyBy, api.Now().AddMinutes(-1))));

        // A late qualifying event does not qualify it (even before the job runs).
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), referred.Id, Guid.NewGuid(), true, api.Now()));
        Assert.Empty(await RewardsAsync(referrer.Id));

        var run = await api.RunJobAsync<ReferralExpiryJob>();
        Assert.Equal(OptimizeAll.Domain.Jobs.JobRunStatus.Succeeded, run!.Status);
        Assert.Equal(ReferralStatus.Expired, (await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.Id == referral.Id))).Status);
        Assert.Equal(ReferralStatus.Registered, (await api.WithDbAsync(db => db.Set<Referral>().SingleAsync(r => r.Id == fresh.Id))).Status);
    }

    [Fact]
    public async Task Participant_sees_code_link_program_stats_and_masked_referrals()
    {
        await SetProgramAsync();
        var referrer = await api.CreateUserAsync();
        var code = await ReferralCodeAsync(referrer.Id);
        var a = await api.CreateUserAsync();
        var b = await api.CreateUserAsync();
        await api.PublishAsync(new UserRegistered(a.Id, code, null, null, null, api.Now()));
        await api.PublishAsync(new UserRegistered(b.Id, code, null, null, null, api.Now()));
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), a.Id, Guid.NewGuid(), true, api.Now()));

        var client = await api.LoginAsync(referrer);
        var me = await (await client.GetAsync("/api/v1/me/referrals")).ReadJsonAsync();
        Assert.Equal(code, me.GetProperty("code").GetString());
        Assert.Equal($"http://app.test/register?ref={code}", me.GetProperty("link").GetString());
        var program = me.GetProperty("program");
        Assert.True(program.GetProperty("enabled").GetBoolean());
        Assert.Equal(5m, program.GetProperty("rewardAmount").GetDecimal());
        Assert.Equal("FirstApprovedSubmission", program.GetProperty("qualifyingAction").GetString());
        var stats = me.GetProperty("stats");
        Assert.Equal(1, stats.GetProperty("registered").GetInt32());
        Assert.Equal(1, stats.GetProperty("qualified").GetInt32());
        Assert.Equal(1, stats.GetProperty("pendingReward").GetInt32());
        Assert.Equal(0, stats.GetProperty("rewarded").GetInt32());
        var items = me.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Matches("^[A-Z]\\*\\*\\*$", i.GetProperty("maskedName").GetString()));
        Assert.Contains(items, i => i.GetProperty("rewardStatus").GetString() == "PendingApproval");
    }

    [Fact]
    public async Task Marketing_rejects_a_referral_and_its_pending_reward_is_declined_with_an_audit_record()
    {
        await SetProgramAsync();
        var (referrer, referred, referral) = await ReferralAsync();
        await api.PublishAsync(new SubmissionApproved(Guid.NewGuid(), referred.Id, Guid.NewGuid(), true, api.Now()));
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);

        await (await manager.PostAsJsonAsync($"/api/v1/marketing/referrals/{referral.Id}/reject", new { reason = "" })).ShouldFailAsync(400);
        var result = await manager.PostJsonAsync($"/api/v1/marketing/referrals/{referral.Id}/reject", new { reason = "Same household" });
        Assert.Equal("Rejected", result.GetProperty("status").GetString());
        Assert.Equal("declined", result.GetProperty("rewardAction").GetString());
        Assert.Equal(EarningStatus.Declined, Assert.Single(await RewardsAsync(referrer.Id)).Status);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a =>
            a.Action == "referral.rejected" && a.EntityId == referral.Id.ToString() && a.Reason == "Same household")));

        await (await manager.PostAsJsonAsync($"/api/v1/marketing/referrals/{referral.Id}/reject", new { reason = "again" }))
            .ShouldFailAsync(409, "referral.already_rejected");
        await (await manager.PostAsJsonAsync($"/api/v1/marketing/referrals/{Guid.NewGuid()}/reject", new { reason = "missing" }))
            .ShouldFailAsync(404, "referral.not_found");

        var list = await (await manager.GetAsync($"/api/v1/marketing/referrals?status=Rejected&search={Uri.EscapeDataString(referred.Email)}")).ReadJsonAsync();
        var row = Assert.Single(list.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal(referrer.Email, row.GetProperty("referrer").GetProperty("email").GetString());
        Assert.Equal("Declined", row.GetProperty("rewardStatus").GetString());
    }
}
