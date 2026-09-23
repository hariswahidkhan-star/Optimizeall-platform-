using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Campaigns;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Campaigns;

public sealed class CampaignTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly CampaignTestKit kit = new(api);

    [Fact]
    public async Task Baseline_categories_are_seeded_and_listed_anonymously()
    {
        var categories = await CampaignTestKit.GetJsonAsync(api.CreateClient(), "/api/v1/campaign-categories");
        var slugs = categories.EnumerateArray().Select(c => c.GetProperty("slug").GetString()).ToList();
        Assert.Equal(new[] { "technology", "fashion-beauty", "food-drink", "travel", "finance", "gaming", "health-fitness", "education", "lifestyle" }, slugs);
        Assert.Equal("gamepad-2", categories.EnumerateArray().Single(c => c.GetProperty("slug").GetString() == "gaming").GetProperty("icon").GetString());
    }

    [Fact]
    public async Task Create_validates_and_publish_moves_to_active_or_scheduled()
    {
        var (_, manager) = await kit.ManagerAsync();

        var badDates = kit.CampaignBody();
        badDates["endsAt"] = kit.Now.AddDays(-2);
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", badDates)).ShouldFailAsync(400, "campaign.invalid_dates");

        var badDeadline = kit.CampaignBody();
        badDeadline["submissionDeadline"] = kit.Now.AddDays(5);
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", badDeadline)).ShouldFailAsync(400, "campaign.invalid_deadline");

        var badZone = kit.CampaignBody();
        badZone["timeZone"] = "Mars/Olympus";
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", badZone)).ShouldFailAsync(400, "campaign.invalid_time_zone");

        var budgetCurrency = kit.CampaignBody();
        budgetCurrency["budgetAmount"] = 100m;
        budgetCurrency["budgetCurrency"] = "EUR";
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", budgetCurrency)).ShouldFailAsync(400, "campaign.budget_currency_mismatch");

        var tracking = kit.CampaignBody();
        tracking["trackingDestinationUrl"] = "http://insecure.example";
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", tracking)).ShouldFailAsync(400, "campaign.invalid_tracking_url");

        var noBase = kit.CampaignBody();
        noBase["rewardRules"] = new { currency = "USD", rules = new object[] { new { type = "FirstPostBonus", amount = 1m } } };
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", noBase)).ShouldFailAsync(400, "reward.invalid_rules");

        var tooMany = kit.CampaignBody();
        tooMany["maxSubmissionsPerParticipant"] = 101;
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", tooMany)).StatusCode);

        // Valid draft → Active (starts in the past) with rules v1 and an audited creation.
        var body = kit.CampaignBody("Autumn Launch!");
        var draft = await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", body)).ReadJsonAsync();
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.StartsWith("autumn-launch", draft.GetProperty("slug").GetString());
        Assert.Equal(1, draft.GetProperty("currentRuleSet").GetProperty("version").GetInt32());
        var id = draft.GetProperty("id").GetGuid();

        var second = await (await manager.PostAsJsonAsync("/api/v1/admin/campaigns", kit.CampaignBody("Autumn Launch!"))).ReadJsonAsync();
        Assert.NotEqual(draft.GetProperty("slug").GetString(), second.GetProperty("slug").GetString());

        var published = await (await manager.PostAsync($"/api/v1/admin/campaigns/{id}/publish", null)).ReadJsonAsync();
        Assert.Equal("Active", published.GetProperty("status").GetString());
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{id}/publish", null)).ShouldFailAsync(409, "campaign.invalid_transition");

        var future = kit.CampaignBody();
        future["startsAt"] = kit.Now.AddDays(2);
        var scheduled = await kit.CreateCampaignAsync(manager, future);
        var scheduledDto = await CampaignTestKit.GetJsonAsync(manager, $"/api/v1/admin/campaigns/{scheduled.Id}");
        Assert.Equal("Scheduled", scheduledDto.GetProperty("status").GetString());

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("campaign.created", actions);
        Assert.Contains("campaign.published", actions);
    }

    [Fact]
    public async Task Publish_requires_assets_or_instructions()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody();
        body["postingInstructions"] = "";
        var draft = await kit.CreateCampaignAsync(manager, body, publish: false);
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/publish", null)).ShouldFailAsync(400, "campaign.incomplete");

        var asset = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{draft.Id}/assets",
            new { type = "Caption", title = "Approved caption", body = "Loving the new #optimizeall range #ad" })).ReadJsonAsync();
        Assert.Equal(0, asset.GetProperty("sortOrder").GetInt32());
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/publish", null)).ReadJsonAsync();
    }

    [Fact]
    public async Task Update_requires_current_stamp_and_budget_changes_need_confirmation()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager, publish: false);
        var dto = await CampaignTestKit.GetJsonAsync(manager, $"/api/v1/admin/campaigns/{campaign.Id}");
        var stamp = dto.GetProperty("concurrencyStamp").GetGuid();

        var update = kit.CampaignBody("Renamed campaign");
        update.Remove("rewardRules");
        update["concurrencyStamp"] = stamp;
        var updated = await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}", update)).ReadJsonAsync();
        Assert.Equal("Renamed campaign", updated.GetProperty("title").GetString());

        // Stale stamp.
        await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}", update)).ShouldFailAsync(409, "concurrency.conflict");

        update["concurrencyStamp"] = updated.GetProperty("concurrencyStamp").GetGuid();
        update["budgetAmount"] = 500m;
        await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}", update)).ShouldFailAsync(400, "campaign.budget_change_unconfirmed");
        update["confirm"] = true;
        update["reason"] = "Client increased the budget";
        var withBudget = await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}", update)).ReadJsonAsync();
        Assert.Equal(500m, withBudget.GetProperty("budgetAmount").GetDecimal());
        Assert.Equal(500m, withBudget.GetProperty("budgetRemaining").GetDecimal());

        var audit = await api.WithDbAsync(db => db.Set<AuditLog>()
            .Where(a => a.EntityId == campaign.Id.ToString() && a.Action == "campaign.updated").ToListAsync());
        Assert.Equal(2, audit.Count);
        var rename = audit.Single(a => a.AfterJson!.Contains("Renamed campaign"));
        Assert.DoesNotContain("postingInstructions", rename.AfterJson!);
        var budget = audit.Single(a => a.AfterJson!.Contains("budgetAmount"));
        Assert.Equal("Client increased the budget", budget.Reason);
    }

    [Fact]
    public async Task Status_transitions_follow_the_lifecycle()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager);
        await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/pause", new { })).ShouldFailAsync(400, "reason.required");
        var paused = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/pause", new { reason = "Client asked to pause" })).ReadJsonAsync();
        Assert.Equal("Paused", paused.GetProperty("status").GetString());
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/archive", null)).ShouldFailAsync(409, "campaign.invalid_transition");
        Assert.Equal("Active", (await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/resume", null)).ReadJsonAsync()).GetProperty("status").GetString());
        Assert.Equal("Ended", (await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/end", new { reason = "Budget spent early" })).ReadJsonAsync()).GetProperty("status").GetString());
        Assert.Equal("Archived", (await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/archive", null)).ReadJsonAsync()).GetProperty("status").GetString());
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/resume", null)).ShouldFailAsync(409, "campaign.invalid_transition");
    }

    [Fact]
    public async Task Schedule_job_activates_and_ends_campaigns()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody();
        body["startsAt"] = kit.Now.AddMinutes(30);
        body["endsAt"] = kit.Now.AddHours(2);
        body["submissionDeadline"] = kit.Now.AddHours(3);
        var campaign = await kit.CreateCampaignAsync(manager, body);

        async Task<CampaignStatus> Status() => await api.WithDbAsync(db => db.Set<Campaign>().Where(c => c.Id == campaign.Id).Select(c => c.Status).FirstAsync());
        Assert.Equal(CampaignStatus.Scheduled, await Status());
        await kit.RunJobAsync<CampaignScheduleJob>();
        Assert.Equal(CampaignStatus.Scheduled, await Status());

        // Move the start into the past instead of advancing the shared clock (keeps other tests' tokens valid).
        await api.WithDbAsync(db => db.Set<Campaign>().Where(c => c.Id == campaign.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.StartsAt, kit.Now.AddMinutes(-1))));
        await kit.RunJobAsync<CampaignScheduleJob>();
        Assert.Equal(CampaignStatus.Active, await Status());

        await api.WithDbAsync(db => db.Set<Campaign>().Where(c => c.Id == campaign.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.SubmissionDeadline, kit.Now.AddMinutes(-1))));
        await kit.RunJobAsync<CampaignScheduleJob>();
        Assert.Equal(CampaignStatus.Ended, await Status());
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.EntityId == campaign.Id.ToString() && a.Action == "campaign.ended" && a.ActorType == "system")));
    }

    [Fact]
    public async Task Browse_filters_and_evaluates_eligibility()
    {
        var (_, manager) = await kit.ManagerAsync();
        var category = await CampaignTestKit.GetJsonAsync(api.CreateClient(), "/api/v1/campaign-categories");
        var gamingId = category.EnumerateArray().Single(c => c.GetProperty("slug").GetString() == "gaming").GetProperty("id").GetGuid();
        var tag = "t" + Guid.NewGuid().ToString("N")[..8];

        var cheap = kit.CampaignBody("Cheap " + tag, baseAmount: 2m);
        cheap["topics"] = new[] { tag };
        var rich = kit.CampaignBody("Rich " + tag, baseAmount: 20m, extraRules: new object[]
        {
            new { type = "RateOverride", amount = 30m, platform = "TikTok" },
            new { type = "FirstPostBonus", amount = 3m },
        });
        rich["topics"] = new[] { tag, "gaming" };
        rich["categoryId"] = gamingId;
        rich["platforms"] = new[] { "TikTok" };
        var invite = kit.CampaignBody("Invite " + tag);
        invite["topics"] = new[] { tag };
        invite["visibility"] = "InviteOnly";
        var draft = kit.CampaignBody("Draft " + tag);
        draft["topics"] = new[] { tag };

        var cheapC = await kit.CreateCampaignAsync(manager, cheap);
        var richC = await kit.CreateCampaignAsync(manager, rich);
        var inviteC = await kit.CreateCampaignAsync(manager, invite);
        var draftC = await kit.CreateCampaignAsync(manager, draft, publish: false);

        var p = await kit.ParticipantAsync(SocialPlatform.Instagram);
        var all = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?topic={tag}&pageSize=50");
        var slugs = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()).ToList();
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Contains(cheapC.Slug, slugs);
        Assert.Contains(richC.Slug, slugs);
        Assert.DoesNotContain(inviteC.Slug, slugs);
        Assert.DoesNotContain(draftC.Slug, slugs);

        var richCard = all.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slug").GetString() == richC.Slug);
        Assert.Equal(20m, richCard.GetProperty("reward").GetProperty("baseAmount").GetDecimal());
        Assert.Equal(30m, richCard.GetProperty("reward").GetProperty("maxAmount").GetDecimal());
        Assert.True(richCard.GetProperty("reward").GetProperty("hasBonuses").GetBoolean());
        Assert.Equal("gaming", richCard.GetProperty("category").GetProperty("slug").GetString());
        // Instagram-only participant is not eligible for a TikTok-only campaign.
        Assert.False(richCard.GetProperty("eligibility").GetProperty("isEligible").GetBoolean());
        Assert.Contains(richCard.GetProperty("eligibility").GetProperty("reasons").EnumerateArray(),
            r => r.GetProperty("code").GetString() == "participant.no_qualifying_account");

        var eligibleOnly = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?topic={tag}&eligibleOnly=true");
        Assert.Equal(cheapC.Slug, Assert.Single(eligibleOnly.GetProperty("items").EnumerateArray()).GetProperty("slug").GetString());

        var minReward = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?topic={tag}&minReward=10");
        Assert.Equal(richC.Slug, Assert.Single(minReward.GetProperty("items").EnumerateArray()).GetProperty("slug").GetString());

        var byPlatform = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?topic={tag}&platform=Instagram");
        Assert.Equal(cheapC.Slug, Assert.Single(byPlatform.GetProperty("items").EnumerateArray()).GetProperty("slug").GetString());

        var byCategory = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?categoryId={gamingId}&topic={tag}");
        Assert.Single(byCategory.GetProperty("items").EnumerateArray());

        var bySearch = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?search=Cheap%20{tag}");
        Assert.Equal(cheapC.Slug, Assert.Single(bySearch.GetProperty("items").EnumerateArray()).GetProperty("slug").GetString());

        var sorted = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns?topic={tag}&sort=reward");
        Assert.Equal(richC.Slug, sorted.GetProperty("items")[0].GetProperty("slug").GetString());

        // Unlisted invite-only campaign is reachable by slug; drafts are not.
        var inviteDetail = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns/{inviteC.Slug}");
        Assert.Equal("InviteOnly", inviteDetail.GetProperty("visibility").GetString());
        await (await p.Client.GetAsync($"/api/v1/campaigns/{draftC.Slug}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task New_social_account_is_ineligible_until_old_enough()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager);
        var p = await kit.ParticipantAsync(SocialPlatform.Instagram, accountAgeDays: 10);

        var detail = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns/{campaign.Slug}");
        Assert.False(detail.GetProperty("eligibility").GetProperty("isEligible").GetBoolean());
        var account = Assert.Single(detail.GetProperty("eligibility").GetProperty("accounts").EnumerateArray());
        Assert.False(account.GetProperty("isEligible").GetBoolean());
        Assert.Equal("social.account_too_new", account.GetProperty("reasons")[0].GetProperty("code").GetString());
        Assert.NotEqual(JsonValueKind.Null, account.GetProperty("eligibleFrom").ValueKind);

        await (await kit.SubmitAsync(p, campaign.Id)).ShouldFailAsync(409, "submission.account_ineligible");
    }

    [Fact]
    public async Task Detail_resolves_disclosures_and_reward_terms()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager, kit.CampaignBody(extraRules: new object[]
        {
            new { type = "RateOverride", amount = 7m, countryCode = "PK", label = "Pakistan rate" },
            new { type = "QualityBonus", amount = 4m, approvalMode = "ManualApproval" },
        }));
        await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/disclosures", new
        {
            disclosures = new object[]
            {
                new { platform = "Instagram", text = "Paid partnership" },
                new { platform = "Instagram", countryCode = "PK", text = "Sponsored (PK)" },
                new { countryCode = "PK", text = "#ad PK" },
            },
        })).ReadJsonAsync();
        await (await manager.PutAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/disclosures", new
        {
            disclosures = new object[] { new { platform = "TikTok", text = "a" }, new { platform = "TikTok", text = "b" } },
        })).ShouldFailAsync(400, "campaign.duplicate_disclosure");

        var p = await kit.ParticipantAsync();
        var detail = await CampaignTestKit.GetJsonAsync(p.Client, $"/api/v1/campaigns/{campaign.Slug}");
        var disclosures = detail.GetProperty("disclosures").EnumerateArray().ToDictionary(d => d.GetProperty("platform").GetString()!, d => d.GetProperty("text").GetString());
        Assert.Equal("Sponsored (PK)", disclosures["Instagram"]);
        Assert.Equal("#ad PK", disclosures["TikTok"]);

        var terms = detail.GetProperty("rewardTerms");
        Assert.Equal(5m, terms.GetProperty("baseAmount").GetDecimal());
        Assert.Equal("Pakistan rate", terms.GetProperty("overrides")[0].GetProperty("label").GetString());
        Assert.Equal("ManualApproval", terms.GetProperty("bonuses")[0].GetProperty("approvalMode").GetString());
        Assert.Equal(1, terms.GetProperty("ruleSetVersion").GetInt32());
        Assert.True(detail.GetProperty("isOpenForSubmissions").GetBoolean());
        Assert.Equal(5, detail.GetProperty("remainingSubmissions").GetInt32());
    }

    [Fact]
    public async Task Recommended_campaigns_match_interests()
    {
        var (_, manager) = await kit.ManagerAsync();
        var interest = "i" + Guid.NewGuid().ToString("N")[..8];
        var body = kit.CampaignBody("Recommended " + interest);
        body["topics"] = new[] { interest };
        var match = await kit.CreateCampaignAsync(manager, body);

        var p = await kit.ParticipantAsync(interests: new[] { interest });
        var recommended = await CampaignTestKit.GetJsonAsync(p.Client, "/api/v1/campaigns/recommended?limit=24");
        var top = recommended.EnumerateArray().First(r => r.GetProperty("campaign").GetProperty("slug").GetString() == match.Slug);
        Assert.Equal($"Matches your interest in {interest}", top.GetProperty("reason").GetString());
        Assert.Equal(match.Slug, recommended[0].GetProperty("campaign").GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Duplicate_copies_assets_disclosures_and_current_rules()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager, kit.CampaignBody(extraRules: new object[] { new { type = "FirstPostBonus", amount = 2m } }));
        await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/assets",
            new { type = "Link", title = "Landing page", url = "https://brand.example/launch" })).ReadJsonAsync();
        var copy = await (await manager.PostAsync($"/api/v1/admin/campaigns/{campaign.Id}/duplicate", null)).ReadJsonAsync();
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal(campaign.Slug + "-copy", copy.GetProperty("slug").GetString());
        Assert.Single(copy.GetProperty("assets").EnumerateArray());
        Assert.Equal(1, copy.GetProperty("currentRuleSet").GetProperty("version").GetInt32());
        Assert.Equal(2, copy.GetProperty("currentRuleSet").GetProperty("rules").GetArrayLength());
    }

    [Fact]
    public async Task Categories_crud_deactivates_referenced_categories()
    {
        var (_, manager) = await kit.ManagerAsync();
        var slug = "cat-" + Guid.NewGuid().ToString("N")[..8];
        var created = await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories", new { name = "Pets", slug, icon = "dog" })).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories", new { name = "Pets again", slug })).ShouldFailAsync(409, "category.slug_taken");

        var body = kit.CampaignBody();
        body["categoryId"] = id;
        await kit.CreateCampaignAsync(manager, body, publish: false);
        var deleted = await (await manager.DeleteAsync($"/api/v1/admin/campaign-categories/{id}")).ReadJsonAsync();
        Assert.False(deleted.GetProperty("deleted").GetBoolean());
        Assert.True(deleted.GetProperty("deactivated").GetBoolean());
        var active = await CampaignTestKit.GetJsonAsync(api.CreateClient(), "/api/v1/campaign-categories");
        Assert.DoesNotContain(active.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        var unused = await (await manager.PostAsJsonAsync("/api/v1/admin/campaign-categories", new { name = "Temporary" })).ReadJsonAsync();
        var removed = await (await manager.DeleteAsync($"/api/v1/admin/campaign-categories/{unused.GetProperty("id").GetGuid()}")).ReadJsonAsync();
        Assert.True(removed.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task Admin_list_includes_submission_counts_and_spend()
    {
        var (_, manager) = await kit.ManagerAsync();
        var body = kit.CampaignBody("Listed " + Guid.NewGuid().ToString("N")[..6]);
        body["budgetAmount"] = 100m;
        var campaign = await kit.CreateCampaignAsync(manager, body);
        var p = await kit.ParticipantAsync();
        var s1 = await kit.SubmitOkAsync(p, campaign.Id);
        await kit.SubmitOkAsync(p, campaign.Id);
        var (_, reviewer) = await kit.ReviewerAsync();
        (await CampaignTestKit.DecideAsync(reviewer, s1, "Approve")).EnsureSuccessStatusCode();

        var list = await CampaignTestKit.GetJsonAsync(manager, $"/api/v1/admin/campaigns?search={Uri.EscapeDataString((string)body["title"]!)}");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("submissions").GetProperty("total").GetInt32());
        Assert.Equal(1, item.GetProperty("submissions").GetProperty("pending").GetInt32());
        Assert.Equal(1, item.GetProperty("submissions").GetProperty("approved").GetInt32());
        Assert.Equal(5m, item.GetProperty("spent").GetDecimal());
        Assert.Equal(95m, item.GetProperty("budgetRemaining").GetDecimal());
    }

    [Fact]
    public async Task Rule_changes_create_versions_and_preserve_historical_rates()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CreateCampaignAsync(manager);
        var p = await kit.ParticipantAsync();
        var (_, reviewer) = await kit.ReviewerAsync();

        var approvedEarly = await kit.SubmitOkAsync(p, campaign.Id);
        var pendingOld = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, approvedEarly, "Approve")).EnsureSuccessStatusCode();
        var before = Assert.Single(await kit.EarningsAsync(approvedEarly));
        Assert.Equal(5m, before.Amount);

        var newRules = new { currency = "USD", rules = new object[] { new { type = "BaseRate", amount = 9m } }, reason = "Rate increase for week two", confirm = true };
        await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/reward-rules", new { newRules.currency, newRules.rules, newRules.reason }))
            .ShouldFailAsync(400, "confirmation.required");
        var v2 = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/reward-rules", newRules)).ReadJsonAsync();
        Assert.Equal(2, v2.GetProperty("version").GetInt32());

        var versions = await CampaignTestKit.GetJsonAsync(manager, $"/api/v1/admin/campaigns/{campaign.Id}/reward-rules");
        Assert.Equal(new[] { 2, 1 }, versions.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal(2, versions[1].GetProperty("inUseBySubmissions").GetInt32());
        Assert.True(versions[0].GetProperty("isCurrent").GetBoolean());
        Assert.Equal("Rate increase for week two", versions[0].GetProperty("reason").GetString());

        var pendingNew = await kit.SubmitOkAsync(p, campaign.Id);
        (await CampaignTestKit.DecideAsync(reviewer, pendingOld, "Approve")).EnsureSuccessStatusCode();
        (await CampaignTestKit.DecideAsync(reviewer, pendingNew, "Approve")).EnsureSuccessStatusCode();

        var oldEarning = Assert.Single(await kit.EarningsAsync(pendingOld));
        Assert.Equal(5m, oldEarning.Amount);
        Assert.Equal(1, oldEarning.RewardRuleSetVersion);
        var newEarning = Assert.Single(await kit.EarningsAsync(pendingNew));
        Assert.Equal(9m, newEarning.Amount);
        Assert.Equal(2, newEarning.RewardRuleSetVersion);

        // The earning approved before the change is untouched.
        var after = Assert.Single(await kit.EarningsAsync(approvedEarly));
        Assert.Equal(before.Amount, after.Amount);
        Assert.Equal(before.RewardRuleSetVersion, after.RewardRuleSetVersion);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);

        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a =>
            a.EntityId == campaign.Id.ToString() && a.Action == "campaign.reward_rules_changed" && a.Reason == "Rate increase for week two" && a.BeforeJson != null)));

        var preview = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/reward-rules/preview",
            new { platform = "Instagram", countryCode = "PK", tier = "Standard", ruleSetVersion = 1 })).ReadJsonAsync();
        Assert.Equal(5m, preview.GetProperty("total").GetDecimal());
        var draftPreview = await (await manager.PostAsJsonAsync($"/api/v1/admin/campaigns/{campaign.Id}/reward-rules/preview", new
        {
            platform = "Instagram", countryCode = "PK", tier = "Standard", earnedToday = 8m,
            draft = new { currency = "USD", dailyCapPerParticipant = 10m, rules = new object[] { new { type = "BaseRate", amount = 5m } } },
        })).ReadJsonAsync();
        Assert.Equal(2m, draftPreview.GetProperty("total").GetDecimal());
        Assert.Equal("daily_cap", draftPreview.GetProperty("appliedCaps")[0].GetString());
    }

    public static IEnumerable<object[]> StaffEndpoints() => new[]
    {
        new object[] { "GET", "/api/v1/admin/campaigns" },
        new object[] { "POST", "/api/v1/admin/campaigns" },
        new object[] { "GET", $"/api/v1/admin/campaigns/{Guid.Empty}" },
        new object[] { "POST", $"/api/v1/admin/campaigns/{Guid.Empty}/publish" },
        new object[] { "GET", $"/api/v1/admin/campaigns/{Guid.Empty}/reward-rules" },
        new object[] { "POST", $"/api/v1/admin/campaigns/{Guid.Empty}/reward-rules" },
        new object[] { "GET", "/api/v1/admin/campaign-categories" },
        new object[] { "POST", "/api/v1/admin/files" },
        new object[] { "GET", "/api/v1/review/queue" },
        new object[] { "GET", $"/api/v1/review/submissions/{Guid.Empty}" },
        new object[] { "POST", $"/api/v1/review/submissions/{Guid.Empty}/claim" },
        new object[] { "POST", $"/api/v1/review/submissions/{Guid.Empty}/decision" },
        new object[] { "POST", $"/api/v1/review/submissions/{Guid.Empty}/reverse" },
        new object[] { "GET", "/api/v1/review/live-checks" },
        new object[] { "GET", "/api/v1/review/appeals" },
        new object[] { "POST", $"/api/v1/review/appeals/{Guid.Empty}/resolve" },
        new object[] { "GET", "/api/v1/review/reviewers" },
        new object[] { "POST", "/api/v1/review/assign" },
        new object[] { "GET", "/api/v1/review/stats" },
    };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task Participants_get_403_on_staff_endpoints(string method, string url)
    {
        var p = await kit.ParticipantAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { });
        var response = await p.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reviewer_cannot_manage_campaigns_and_staff_cannot_use_participant_endpoints()
    {
        var (_, reviewer) = await kit.ReviewerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/v1/admin/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/v1/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/v1/campaigns")).StatusCode);

        // A manager without rewards.edit cannot exist by role, but finance (no campaigns.manage) is refused.
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync("/api/v1/admin/campaigns", kit.CampaignBody())).StatusCode);
    }
}
