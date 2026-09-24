using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Rates.RatesKit;

namespace OptimizeAll.IntegrationTests.Rates;

/// <summary>
/// Person-level rates through the real flows: submission estimate, approval earnings, ledger rate source, locking of
/// historical prices, currency conversion, campaign policy, budgets, participant/reviewer/finance views.
/// </summary>
public sealed class RatePricingTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly RatesKit kit = new(api);
    private CampaignTestKit C => kit.Campaigns;

    private async Task<(HttpClient Manager, CreatedCampaign Campaign, Participant Person, HttpClient Reviewer)> SetupAsync(
        decimal baseAmount = 5m, string currency = "USD", Action<Dictionary<string, object?>>? customize = null, params object[] extraRules)
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CampaignAsync(manager, baseAmount, currency, customize, true, extraRules);
        var person = await C.ParticipantAsync();
        var (_, reviewer) = await C.ReviewerAsync();
        return (manager, campaign, person, reviewer);
    }

    private async Task<Guid> SubmitAndApproveAsync(Participant p, Guid campaignId, HttpClient reviewer)
    {
        var id = await C.SubmitOkAsync(p, campaignId);
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        return id;
    }

    [Fact]
    public async Task Without_person_level_rates_campaigns_price_exactly_as_before()
    {
        var (_, campaign, p, reviewer) = await SetupAsync(5m, extraRules: new object[]
        {
            new { type = "RateOverride", amount = 6.5m, countryCode = "PK" }, new { type = "FirstPostBonus", amount = 1m },
        });
        var id = await C.SubmitOkAsync(p, campaign.Id);
        Assert.Equal(7.5m, await kit.EstimateAsync(p.Client, id));
        Assert.Null(await kit.SnapshotAsync(id));
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        var earnings = await C.EarningsAsync(id);
        var post = earnings.Single(e => e.Type == EarningType.PostReward);
        Assert.Equal(6.5m, post.Amount);
        Assert.NotNull(post.RewardRuleId);
        Assert.Equal(RateSourceLevel.CampaignRules, post.RateSource);
        Assert.Null(post.RateCardId);
        var bonus = earnings.Single(e => e.Type == EarningType.FirstPostBonus);
        Assert.Equal(1m, bonus.Amount);
        Assert.Null(bonus.RateSource);
    }

    [Fact]
    public async Task A_group_rate_prices_the_submission_and_the_ledger_line_carries_its_source()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m, extraRules: new object[] { new { type = "FirstPostBonus", amount = 1m } });
        var card = await CardAsync(manager, "Micro " + Guid.NewGuid().ToString("N")[..6], lines: new[] { Line(10m), Line(12m, "Instagram", label: "Creator fee") });
        var group = await GroupAsync(manager, "Micro group " + Guid.NewGuid().ToString("N")[..6], 30);
        await AddMembersAsync(manager, Id(group), p.User.Id);
        await AssignAsync(manager, Id(card), groupId: Id(group));

        var id = await C.SubmitOkAsync(p, campaign.Id);
        Assert.Equal(13m, await kit.EstimateAsync(p.Client, id)); // 12 + first-post bonus stacks
        var snapshot = await kit.SnapshotAsync(id);
        Assert.NotNull(snapshot);
        Assert.Equal(RateSourceLevel.GlobalGroup, snapshot!.Level);
        Assert.Equal(12m, snapshot.Amount);

        // Reviewer sees the source (generic, without rates.view).
        var detail = await (await reviewer.GetAsync($"/api/v1/review/submissions/{id}")).ReadJsonAsync();
        var source = detail.GetProperty("rewardQuote").GetProperty("rateSource");
        Assert.Equal("GlobalGroup", source.GetProperty("level").GetString());
        Assert.Equal("Group rate (all campaigns)", source.GetProperty("label").GetString());
        Assert.Equal(5m, source.GetProperty("campaignRateAmount").GetDecimal());

        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        var post = await kit.PostRewardAsync(id);
        Assert.Equal(12m, post.Amount);
        Assert.Equal(RateSourceLevel.GlobalGroup, post.RateSource);
        Assert.Equal(Id(card), post.RateCardId);
        Assert.Equal(1, post.RateCardVersion);
        Assert.Equal(Id(group), post.RateGroupId);
        Assert.Null(post.RewardRuleId);
        Assert.Contains(group.GetProperty("name").GetString()!, post.RateSourceLabel);
        Assert.StartsWith("Creator fee", post.Description);

        // Finance sees the source on the ledger and in the export.
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var ledger = await (await finance.GetAsync($"/api/v1/finance/ledger?search={id}")).ReadJsonAsync();
        var row = ledger.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("type").GetString() == "PostReward");
        Assert.Equal("GlobalGroup", row.GetProperty("rateSource").GetString());
        Assert.Equal(Id(card), row.GetProperty("rateCardId").GetGuid());
        var csv = await (await finance.GetAsync($"/api/v1/finance/ledger/export.csv?search={id}")).Content.ReadAsStringAsync();
        Assert.Contains("Rate source", csv);
        Assert.Contains("GlobalGroup", csv);
    }

    [Fact]
    public async Task Precedence_campaign_scoped_group_beats_a_global_personal_deal_and_explain_says_why()
    {
        var (manager, campaign, p, _) = await SetupAsync(5m);
        var groupCard = await CardAsync(manager, lines: new[] { Line(9m) });
        var scopedCard = await CardAsync(manager, lines: new[] { Line(7m) });
        var group = await GroupAsync(manager, priority: 10);
        await AddMembersAsync(manager, Id(group), p.User.Id);
        await AssignAsync(manager, Id(groupCard), groupId: Id(group));
        await CustomRateAsync(manager, p.User.Id, new[] { Line(20m) });
        await AssignAsync(manager, Id(scopedCard), groupId: Id(group), campaignId: campaign.Id);

        var explain = await (await manager.GetAsync($"/api/v1/admin/users/{p.User.Id}/rates/explain?platform=Instagram&campaignId={campaign.Id}")).ReadJsonAsync();
        Assert.Equal("CampaignGroup", explain.GetProperty("winner").GetString());
        // (Automatic groups of other tests show up as "not applicable" candidates; only the three that apply rank.)
        var candidates = explain.GetProperty("candidates").EnumerateArray()
            .Where(c => c.GetProperty("outcome").GetString() != "NotApplicable").ToList();
        Assert.Equal(3, candidates.Count);
        Assert.Equal("Won", candidates[0].GetProperty("outcome").GetString());
        Assert.All(candidates.Skip(1), c => Assert.Equal("Outranked", c.GetProperty("outcome").GetString()));
        Assert.Equal(7m, explain.GetProperty("quote").GetProperty("total").GetDecimal());
        Assert.Equal(9, explain.GetProperty("precedence").GetArrayLength());

        // Without the campaign (global view) the personal deal wins.
        var global = await (await manager.GetAsync($"/api/v1/admin/users/{p.User.Id}/rates/explain?platform=Instagram")).ReadJsonAsync();
        Assert.Equal("GlobalPersonalCustom", global.GetProperty("winner").GetString());

        var id = await C.SubmitOkAsync(p, campaign.Id);
        Assert.Equal(7m, await kit.EstimateAsync(p.Client, id));

        // Rates tab.
        var tab = await (await manager.GetAsync($"/api/v1/admin/users/{p.User.Id}/rates?campaignId={campaign.Id}")).ReadJsonAsync();
        Assert.Contains(tab.GetProperty("groups").EnumerateArray(), g => Id(g) == Id(group));
        var ig = tab.GetProperty("effective").EnumerateArray().First(e => e.GetProperty("platform").GetString() == "Instagram");
        Assert.Equal("CampaignGroup", ig.GetProperty("level").GetString());
        Assert.Equal(3, tab.GetProperty("assignments").GetArrayLength());

        // Simulator (campaign editor).
        var sim = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/rates/simulate",
            new { userId = p.User.Id, platform = "TikTok" })).ReadJsonAsync();
        Assert.Equal(7m, sim.GetProperty("quote").GetProperty("total").GetDecimal());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/rates/simulate", new { platform = "TikTok" })).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Prices_lock_at_submission_later_rate_changes_removals_and_expiry_never_change_them()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m);
        var card = await CardAsync(manager, lines: new[] { Line(10m) });
        var group = await GroupAsync(manager);
        await AddMembersAsync(manager, Id(group), p.User.Id);
        await AssignAsync(manager, Id(card), groupId: Id(group));
        var deal = await CustomRateAsync(manager, p.User.Id, new[] { Line(15m) }, validTo: kit.Now.AddMinutes(2));

        var locked = await C.SubmitOkAsync(p, campaign.Id); // deal (15) applies
        Assert.Equal(15m, await kit.EstimateAsync(p.Client, locked));
        var viaGroup = await C.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now.AddMinutes(-5));

        // Everything changes before approval: the deal expires, a new card version, the person leaves the group.
        api.Clock.Advance(TimeSpan.FromMinutes(3));
        await PostVersionAsync(manager, Id(card), new[] { Line(11m) });
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{Id(group)}/members/remove",
            new { userIds = new[] { p.User.Id }, reason = "Moved out" })).ReadJsonAsync();

        await (await CampaignTestKit.DecideAsync(reviewer, locked, "Approve")).ReadJsonAsync();
        Assert.Equal(15m, (await kit.PostRewardAsync(locked)).Amount);
        Assert.Equal(RateSourceLevel.GlobalPersonalCustom, (await kit.PostRewardAsync(locked)).RateSource);
        await (await CampaignTestKit.DecideAsync(reviewer, viaGroup, "Approve")).ReadJsonAsync();
        Assert.Equal(15m, (await kit.PostRewardAsync(viaGroup)).Amount);

        // A new submission is priced with today's rates: no deal, no group → campaign rate.
        var fresh = await C.SubmitOkAsync(p, campaign.Id, postedAt: kit.Now);
        await (await CampaignTestKit.DecideAsync(reviewer, fresh, "Approve")).ReadJsonAsync();
        Assert.Equal(5m, (await kit.PostRewardAsync(fresh)).Amount);
        Assert.Equal(RateSourceLevel.CampaignRules, (await kit.PostRewardAsync(fresh)).RateSource);

        // Changing the card again never touches recorded earnings.
        await PostVersionAsync(manager, Id(card), new[] { Line(50m) });
        Assert.Equal(15m, (await kit.PostRewardAsync(locked)).Amount);
        _ = deal;
    }

    [Fact]
    public async Task A_correction_keeps_the_locked_rate()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m);
        var deal = await CustomRateAsync(manager, p.User.Id, new[] { Line(9m) });
        var id = await C.SubmitOkAsync(p, campaign.Id);
        await (await CampaignTestKit.DecideAsync(reviewer, id, "RequestCorrection", "Please add the hashtag")).ReadJsonAsync();
        var stamp = Stamp(deal);
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-assignments/{Id(deal)}/end", new { reason = "Deal over", concurrencyStamp = stamp })).ReadJsonAsync();

        var form = new MultipartFormDataContent { { new StringContent("Now with #optimizeall"), "captionText" } };
        await (await p.Client.PutAsync($"/api/v1/me/submissions/{id}", form)).ReadJsonAsync();
        Assert.Equal(9m, await kit.EstimateAsync(p.Client, id));
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        Assert.Equal(9m, (await kit.PostRewardAsync(id)).Amount);
    }

    [Fact]
    public async Task Card_currency_is_converted_at_submission_and_the_rate_used_is_recorded()
    {
        await kit.AddExchangeRateAsync("GBP", "USD", 1.25m);
        await kit.AddExchangeRateAsync("USD", "JPY", 149.37m);
        await kit.AddExchangeRateAsync("KWD", "USD", 3.25m);
        var (manager, campaign, p, reviewer) = await SetupAsync(5m);
        var gbp = await CardAsync(manager, currency: "GBP", lines: new[] { Line(8.99m) });
        await AssignAsync(manager, Id(gbp), p.User.Id, campaignId: campaign.Id);
        var id = await SubmitAndApproveAsync(p, campaign.Id, reviewer);
        var snap = (await kit.SnapshotAsync(id))!;
        Assert.Equal("GBP", snap.CardCurrency);
        Assert.Equal(8.99m, snap.CardAmount);
        Assert.Equal(1.25m, snap.ExchangeRate);
        Assert.NotNull(snap.ExchangeRateId);
        Assert.Equal(11.24m, snap.Amount); // 11.2375 rounded half away from zero
        Assert.Equal(11.24m, (await kit.PostRewardAsync(id)).Amount);
        Assert.Equal("USD", (await kit.PostRewardAsync(id)).Currency);

        // JPY campaign: USD card converted and rounded to whole yen.
        var jpy = await kit.CampaignAsync(manager, 500m, "JPY");
        var q = await C.ParticipantAsync();
        var usd = await CardAsync(manager, lines: new[] { Line(7.77m) });
        await AssignAsync(manager, Id(usd), q.User.Id, campaignId: jpy.Id);
        var jid = await C.SubmitOkAsync(q, jpy.Id);
        Assert.Equal(1161m, (await kit.SnapshotAsync(jid))!.Amount); // 7.77 × 149.37 = 1160.6049
        Assert.Equal(1161m, await kit.EstimateAsync(q.Client, jid));

        // KWD card on a USD campaign keeps KWD precision on the card and converts to cents.
        var r = await C.ParticipantAsync();
        var kwd = await CardAsync(manager, currency: "KWD", lines: new[] { Line(1.2345m) });
        await AssignAsync(manager, Id(kwd), r.User.Id, campaignId: campaign.Id);
        var kid = await C.SubmitOkAsync(r, campaign.Id);
        var ks = (await kit.SnapshotAsync(kid))!;
        Assert.Equal(1.235m, ks.CardAmount);
        Assert.Equal(4.01m, ks.Amount); // 1.235 × 3.25 = 4.01375
    }

    [Fact]
    public async Task A_missing_exchange_rate_at_pricing_time_refuses_the_submission_instead_of_pricing_at_zero()
    {
        var (manager, campaign, p, _) = await SetupAsync(5m);
        await kit.AddExchangeRateAsync("QAR", "USD", 0.27m);
        var qar = await CardAsync(manager, currency: "QAR", lines: new[] { Line(40m) });
        await AssignAsync(manager, Id(qar), p.User.Id, campaignId: campaign.Id);
        // The rate row disappears (e.g. restored database): pricing must refuse, not fall back to 0 or the campaign rate.
        await api.WithDbAsync(db => db.Set<ExchangeRate>().Where(x => x.BaseCurrency == "QAR").ExecuteDeleteAsync());
        await (await C.SubmitAsync(p, campaign.Id)).ShouldFailAsync(409, "rates.fx_missing");
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<Domain.Submissions.Submission>().CountAsync(s => s.UserId == p.User.Id)));
    }

    [Fact]
    public async Task Campaign_policy_campaign_rates_only_and_max_multiplier()
    {
        var (manager, only, p, reviewer) = await SetupAsync(5m, customize: b =>
            ((Dictionary<string, object?>)b["rewardRules"]!)["personalRatesMode"] = "CampaignRatesOnly");
        await CustomRateAsync(manager, p.User.Id, new[] { Line(40m) });
        var id = await SubmitAndApproveAsync(p, only.Id, reviewer);
        Assert.Null(await kit.SnapshotAsync(id));
        Assert.Equal(5m, (await kit.PostRewardAsync(id)).Amount);

        var capped = await kit.CampaignAsync(manager, 5m, customize: b =>
            ((Dictionary<string, object?>)b["rewardRules"]!)["personalRateMaxMultiplier"] = 3m);
        var cid = await C.SubmitOkAsync(p, capped.Id);
        Assert.Equal(15m, await kit.EstimateAsync(p.Client, cid));
        var decision = await (await CampaignTestKit.DecideAsync(reviewer, cid, "Approve")).ReadJsonAsync();
        Assert.Contains("personal_rate_limit", decision.GetProperty("reward").GetProperty("appliedCaps").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(15m, (await kit.PostRewardAsync(cid)).Amount);
    }

    [Fact]
    public async Task High_personal_rates_still_stop_at_the_campaign_budget_and_caps()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m, customize: b =>
        {
            b["budgetAmount"] = 30m;
            b["budgetCurrency"] = "USD";
            ((Dictionary<string, object?>)b["rewardRules"]!)["dailyCapPerParticipant"] = 100m;
        });
        await CustomRateAsync(manager, p.User.Id, new[] { Line(25m) });
        var first = await SubmitAndApproveAsync(p, campaign.Id, reviewer);
        Assert.Equal(25m, (await kit.PostRewardAsync(first)).Amount);
        var second = await C.SubmitOkAsync(p, campaign.Id);
        var decision = await (await CampaignTestKit.DecideAsync(reviewer, second, "Approve")).ReadJsonAsync();
        Assert.Contains("campaign_budget", decision.GetProperty("reward").GetProperty("appliedCaps").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(5m, (await kit.PostRewardAsync(second)).Amount);
        var third = await C.SubmitOkAsync(p, campaign.Id);
        await (await CampaignTestKit.DecideAsync(reviewer, third, "Approve")).ReadJsonAsync();
        Assert.Empty(await C.EarningsAsync(third));

        // Card caps apply too.
        var q = await C.ParticipantAsync();
        var capCard = await CardAsync(manager, lines: new[] { Line(8m) }, dailyCap: 10m);
        var other = await kit.CampaignAsync(manager, 5m);
        await AssignAsync(manager, Id(capCard), q.User.Id);
        await SubmitAndApproveAsync(q, other.Id, reviewer);
        var capped = await C.SubmitOkAsync(q, other.Id);
        var d2 = await (await CampaignTestKit.DecideAsync(reviewer, capped, "Approve")).ReadJsonAsync();
        Assert.Contains("rate_card_daily_cap", d2.GetProperty("reward").GetProperty("appliedCaps").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2m, (await kit.PostRewardAsync(capped)).Amount);
    }

    [Fact]
    public async Task Bonuses_stack_unless_the_card_turns_them_off()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m, extraRules: new object[] { new { type = "FirstPostBonus", amount = 2m } });
        var noStack = await CardAsync(manager, lines: new[] { Line(10m) }, stack: false);
        await AssignAsync(manager, Id(noStack), p.User.Id);
        var id = await SubmitAndApproveAsync(p, campaign.Id, reviewer);
        var earnings = await C.EarningsAsync(id);
        Assert.Single(earnings);
        Assert.Equal(10m, earnings[0].Amount);
    }

    [Fact]
    public async Task Format_specific_rates_and_format_validation()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m);
        var card = await CardAsync(manager, lines: new[] { Line(10m, "Instagram"), Line(16m, "Instagram", "ShortVideo") });
        await AssignAsync(manager, Id(card), p.User.Id);
        var reel = await C.SubmitOkAsync(p, campaign.Id, url: $"https://www.instagram.com/reel/{Guid.NewGuid():N}/");
        Assert.Equal(16m, await kit.EstimateAsync(p.Client, reel));
        var mine = await (await p.Client.GetAsync($"/api/v1/me/submissions/{reel}")).ReadJsonAsync();
        Assert.Equal("ShortVideo", mine.GetProperty("format").GetString());
        Assert.Equal("Personal", mine.GetProperty("rateKind").GetString());
        var post = await C.SubmitOkAsync(p, campaign.Id);
        Assert.Equal(10m, await kit.EstimateAsync(p.Client, post));

        var form = CampaignTestKit.SubmissionForm(campaign.Id, p.AccountId, $"https://www.instagram.com/reel/{Guid.NewGuid():N}/", kit.Now.AddHours(-1));
        form.Add(new StringContent("Carousel"), "format");
        await (await p.Client.PostAsync("/api/v1/me/submissions", form)).ShouldFailAsync(400, "submission.format_mismatch");
        _ = reviewer;
    }

    [Fact]
    public async Task Participants_see_only_their_own_rate_and_never_group_or_card_names()
    {
        var (manager, campaign, p, _) = await SetupAsync(5m);
        var groupName = "Secret VIP " + Guid.NewGuid().ToString("N")[..6];
        var cardName = "Secret card " + Guid.NewGuid().ToString("N")[..6];
        var group = await GroupAsync(manager, groupName);
        var card = await CardAsync(manager, cardName, lines: new[] { Line(9m), Line(11m, "TikTok") });
        await AddMembersAsync(manager, Id(group), p.User.Id);
        await AssignAsync(manager, Id(card), groupId: Id(group));
        var validTo = kit.Now.AddDays(4);
        await CustomRateAsync(manager, p.User.Id, new[] { Line(14m, "Instagram") }, campaignId: campaign.Id, validTo: validTo);

        var detailText = await (await p.Client.GetAsync($"/api/v1/campaigns/{campaign.Slug}")).Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(detailText);
        var yourRate = detail.GetProperty("yourRate");
        Assert.Equal("Personal", yourRate.GetProperty("kind").GetString());
        Assert.Equal(11m, yourRate.GetProperty("minAmount").GetDecimal());
        Assert.Equal(14m, yourRate.GetProperty("maxAmount").GetDecimal());
        Assert.Equal(validTo, yourRate.GetProperty("validTo").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(groupName, detailText);
        Assert.DoesNotContain(cardName, detailText);

        var list = await (await p.Client.GetAsync("/api/v1/campaigns?pageSize=50")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(groupName, list);
        var card2 = JsonSerializer.Deserialize<JsonElement>(list).GetProperty("items").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == campaign.Id);
        Assert.Equal(14m, card2.GetProperty("yourRate").GetProperty("maxAmount").GetDecimal());

        // Someone else sees no personal rate.
        var other = await C.ParticipantAsync();
        var theirs = await (await other.Client.GetAsync($"/api/v1/campaigns/{campaign.Slug}")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, theirs.GetProperty("yourRate").ValueKind);

        // A participant can't read anyone's rates.
        await (await p.Client.GetAsync($"/api/v1/admin/users/{p.User.Id}/rates")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Automatic_segment_rates_follow_tier_and_verified_followers()
    {
        var (manager, campaign, _, reviewer) = await SetupAsync(5m);
        var group = await GroupAsync(manager, mode: "Automatic", tiers: new[] { "Gold" });
        var card = await CardAsync(manager, lines: new[] { Line(8m) });
        await AssignAsync(manager, Id(card), groupId: Id(group));
        var gold = await C.ParticipantAsync(tier: ParticipantTier.Gold);
        var standard = await C.ParticipantAsync();
        var g = await SubmitAndApproveAsync(gold, campaign.Id, reviewer);
        var s = await SubmitAndApproveAsync(standard, campaign.Id, reviewer);
        Assert.Equal(8m, (await kit.PostRewardAsync(g)).Amount);
        Assert.Equal(RateSourceLevel.GlobalSegment, (await kit.PostRewardAsync(g)).RateSource);
        Assert.Equal(5m, (await kit.PostRewardAsync(s)).Amount);
    }

    [Fact]
    public async Task An_archived_card_keeps_pricing_submissions_it_already_priced()
    {
        var (manager, campaign, p, reviewer) = await SetupAsync(5m);
        var card = await CardAsync(manager, lines: new[] { Line(12m) });
        await AssignAsync(manager, Id(card), p.User.Id);
        var id = await C.SubmitOkAsync(p, campaign.Id);
        var current = await (await manager.GetAsync($"/api/v1/admin/rate-cards/{Id(card)}")).ReadJsonAsync();
        Assert.Equal(1, current.GetProperty("usedBySubmissions").GetInt32());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{Id(card)}/archive",
            new { reason = "Retired", endAssignments = true, concurrencyStamp = Stamp(current) })).ReadJsonAsync();
        await (await CampaignTestKit.DecideAsync(reviewer, id, "Approve")).ReadJsonAsync();
        Assert.Equal(12m, (await kit.PostRewardAsync(id)).Amount);
        var next = await SubmitAndApproveAsync(p, campaign.Id, reviewer);
        Assert.Equal(5m, (await kit.PostRewardAsync(next)).Amount);
    }

    [Fact]
    public async Task The_campaign_rates_panel_lists_scoped_and_global_assignments()
    {
        var (manager, campaign, p, _) = await SetupAsync(5m);
        var card = await CardAsync(manager);
        await AssignAsync(manager, Id(card), p.User.Id, campaignId: campaign.Id);
        var panel = await (await manager.GetAsync($"/api/v1/admin/campaigns/{campaign.Id}/rates")).ReadJsonAsync();
        Assert.Equal("Allowed", panel.GetProperty("personalRatesMode").GetString());
        Assert.Single(panel.GetProperty("campaignAssignments").EnumerateArray());
        Assert.True(panel.GetProperty("peopleWithPersonalRates").GetInt32() >= 1);
        Assert.Empty(panel.GetProperty("fxProblems").EnumerateArray());
    }
}
