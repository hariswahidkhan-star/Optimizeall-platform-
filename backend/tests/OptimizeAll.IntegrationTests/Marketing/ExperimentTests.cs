using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Marketing;

public sealed class ExperimentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object TitleExperiment(Guid campaignId, string name = "Title test") => new
    {
        campaignId, name, hypothesis = "A benefit-led title increases submissions", element = "Title",
        variants = new object[]
        {
            new { key = "A", name = "Control", weight = 50, title = "Share our spring collection" },
            new { key = "B", name = "Benefit", weight = 50, title = "Earn by sharing our spring collection" },
        },
    };

    [Fact]
    public async Task Lifecycle_start_pause_resume_complete_with_draft_only_edits_and_one_running_per_element()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id);

        var created = await manager.PostAsJsonAsync("/api/v1/marketing/experiments", TitleExperiment(campaign.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var experiment = await created.ReadJsonAsync();
        var id = experiment.GetProperty("id").GetGuid();
        Assert.Equal("Draft", experiment.GetProperty("status").GetString());

        // Draft edits are allowed (and guarded by the concurrency stamp).
        var edit = TitleExperiment(campaign.Id, "Title test v2");
        var stale = JsonSerializer.SerializeToElement(edit, ApiFactory.Json);
        var withStamp = new Dictionary<string, object?>(stale.EnumerateObject().Select(p => new KeyValuePair<string, object?>(p.Name, p.Value)))
        {
            ["concurrencyStamp"] = experiment.GetProperty("concurrencyStamp").GetGuid(),
        };
        var updated = await (await manager.PutAsJsonAsync($"/api/v1/marketing/experiments/{id}", withStamp)).ReadJsonAsync();
        Assert.Equal("Title test v2", updated.GetProperty("name").GetString());
        await (await manager.PutAsJsonAsync($"/api/v1/marketing/experiments/{id}", withStamp)).ShouldFailAsync(409, "concurrency.conflict");

        var started = await (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/start", null)).ReadJsonAsync();
        Assert.Equal("Running", started.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, started.GetProperty("startedAt").ValueKind);
        await (await manager.PutAsJsonAsync($"/api/v1/marketing/experiments/{id}", edit)).ShouldFailAsync(409, "experiment.not_draft");
        await (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/start", null)).ShouldFailAsync(409, "experiment.invalid_transition");
        await (await manager.DeleteAsync($"/api/v1/marketing/experiments/{id}")).ShouldFailAsync(409, "experiment.not_draft");

        // Only one running experiment per campaign and element.
        var rival = await manager.PostJsonAsync("/api/v1/marketing/experiments", TitleExperiment(campaign.Id, "Rival"));
        await (await manager.PostAsync($"/api/v1/marketing/experiments/{rival.GetProperty("id").GetGuid()}/start", null))
            .ShouldFailAsync(409, "experiment.already_running");

        Assert.Equal("Paused", (await (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/pause", null)).ReadJsonAsync()).GetProperty("status").GetString());
        await (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/pause", null)).ShouldFailAsync(409, "experiment.invalid_transition");
        Assert.Equal("Running", (await (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/resume", null)).ReadJsonAsync()).GetProperty("status").GetString());

        var winner = started.GetProperty("variants")[1].GetProperty("id").GetGuid();
        await (await manager.PostAsJsonAsync($"/api/v1/marketing/experiments/{id}/complete", new { winningVariantId = Guid.NewGuid() }))
            .ShouldFailAsync(400, "experiment.invalid_winner");
        var completed = await (await manager.PostAsJsonAsync($"/api/v1/marketing/experiments/{id}/complete", new { winningVariantId = winner })).ReadJsonAsync();
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(winner, completed.GetProperty("winningVariantId").GetGuid());

        // Now the rival may run; the unused draft can be deleted.
        (await manager.PostAsync($"/api/v1/marketing/experiments/{rival.GetProperty("id").GetGuid()}/start", null)).EnsureSuccessStatusCode();
        var draft = await manager.PostJsonAsync("/api/v1/marketing/experiments", TitleExperiment(campaign.Id, "Throwaway"));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/marketing/experiments/{draft.GetProperty("id").GetGuid()}")).StatusCode);

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("experiment.created", actions);
        Assert.Contains("experiment.updated", actions);
        Assert.Contains("experiment.started", actions);
        Assert.Contains("experiment.paused", actions);
        Assert.Contains("experiment.resumed", actions);
        Assert.Contains("experiment.completed", actions);
    }

    [Fact]
    public async Task Definitions_are_validated()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        var other = await api.CreateCampaignAsync(managerUser.Id);
        var foreignAsset = other.Assets[0].Id;

        await (await manager.PostAsJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "Creative", element = "CreativeAsset",
            variants = new object[] { new { key = "A", name = "A", weight = 50, assetId = campaign.Assets[0].Id }, new { key = "B", name = "B", weight = 50, assetId = foreignAsset } },
        })).ShouldFailAsync(400, "experiment.invalid");

        await (await manager.PostAsJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "No control", element = "Title",
            variants = new object[] { new { key = "B", name = "B", weight = 50, title = "x" }, new { key = "C", name = "C", weight = 50, title = "y" } },
        })).ShouldFailAsync(400, "experiment.invalid");

        await (await manager.PostAsJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "Missing title", element = "Title",
            variants = new object[] { new { key = "A", name = "A", weight = 50, title = "x" }, new { key = "B", name = "B", weight = 50 } },
        })).ShouldFailAsync(400, "experiment.invalid");

        // DataAnnotations: 1 variant, weight 0, key E.
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "One", element = "Title", variants = new object[] { new { key = "A", name = "A", weight = 50, title = "x" } },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "Weights", element = "Title",
            variants = new object[] { new { key = "A", name = "A", weight = 0, title = "x" }, new { key = "E", name = "E", weight = 50, title = "y" } },
        })).StatusCode);
    }

    [Fact]
    public async Task Participants_get_sticky_variant_overlays_for_running_experiments()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        var title = await manager.PostJsonAsync("/api/v1/marketing/experiments", TitleExperiment(campaign.Id));
        var creative = await manager.PostJsonAsync("/api/v1/marketing/experiments", new
        {
            campaignId = campaign.Id, name = "Creative", element = "CreativeAsset",
            variants = new object[]
            {
                new { key = "A", name = "Image", weight = 50, assetId = campaign.Assets[0].Id },
                new { key = "B", name = "Caption", weight = 50, assetId = campaign.Assets[1].Id },
            },
        });
        var (participant, client) = await api.CreateClientAsync();

        Assert.Empty((await (await client.GetAsync($"/api/v1/campaigns/{campaign.Id}/experiment-variants")).ReadJsonAsync()).EnumerateArray());

        (await manager.PostAsync($"/api/v1/marketing/experiments/{title.GetProperty("id").GetGuid()}/start", null)).EnsureSuccessStatusCode();
        (await manager.PostAsync($"/api/v1/marketing/experiments/{creative.GetProperty("id").GetGuid()}/start", null)).EnsureSuccessStatusCode();

        var first = (await (await client.GetAsync($"/api/v1/campaigns/{campaign.Id}/experiment-variants")).ReadJsonAsync()).EnumerateArray().ToList();
        var second = (await (await client.GetAsync($"/api/v1/campaigns/{campaign.Id}/experiment-variants")).ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(v => v.GetProperty("variantId").GetGuid()), second.Select(v => v.GetProperty("variantId").GetGuid()));

        var titleOverlay = first.Single(v => v.GetProperty("element").GetString() == "Title");
        Assert.StartsWith(titleOverlay.GetProperty("key").GetString() == "A" ? "Share" : "Earn", titleOverlay.GetProperty("title").GetString());
        var creativeOverlay = first.Single(v => v.GetProperty("element").GetString() == "CreativeAsset");
        var asset = creativeOverlay.GetProperty("asset");
        Assert.Contains(asset.GetProperty("id").GetGuid(), campaign.Assets.Select(a => a.Id));
        Assert.Equal(JsonValueKind.Null, creativeOverlay.GetProperty("title").ValueKind);

        var subject = $"user:{participant.Id:D}";
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<ExperimentAssignment>().CountAsync(a => a.SubjectKey == subject)));
        await (await client.GetAsync($"/api/v1/campaigns/{Guid.NewGuid()}/experiment-variants")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Results_report_measured_rates_and_significance_only_with_enough_data()
    {
        var (managerUser, manager) = await api.CreateClientAsync(Role.CampaignManager);
        var campaign = await api.CreateCampaignAsync(managerUser.Id);
        var experiment = await manager.PostJsonAsync("/api/v1/marketing/experiments", TitleExperiment(campaign.Id));
        var id = experiment.GetProperty("id").GetGuid();
        (await manager.PostAsync($"/api/v1/marketing/experiments/{id}/start", null)).EnsureSuccessStatusCode();
        var variantA = experiment.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var variantB = experiment.GetProperty("variants")[1].GetProperty("id").GetGuid();

        // Small sample: 3 per arm (1 vs 2 submitters) — never called significant.
        await ArrangeArmAsync(campaign, id, variantA, assigned: 3, submitters: 1, approved: 1);
        await ArrangeArmAsync(campaign, id, variantB, assigned: 3, submitters: 2, approved: 1);
        var small = await (await manager.GetAsync($"/api/v1/marketing/experiments/{id}/results")).ReadJsonAsync();
        var smallComparison = small.GetProperty("comparisons")[0];
        Assert.False(smallComparison.GetProperty("significant").GetBoolean());
        Assert.Equal("Not enough data for a reliable conclusion", smallComparison.GetProperty("note").GetString());
        Assert.Equal("measured", small.GetProperty("measurement").GetString());

        // Grow to 120 per arm: A 12 submitters (6 approved), B 30 submitters (20 approved).
        await ArrangeArmAsync(campaign, id, variantA, assigned: 117, submitters: 11, approved: 5);
        await ArrangeArmAsync(campaign, id, variantB, assigned: 117, submitters: 28, approved: 19, extraSubmissionForFirst: true);

        var results = await (await manager.GetAsync($"/api/v1/marketing/experiments/{id}/results")).ReadJsonAsync();
        var a = results.GetProperty("variants")[0];
        var b = results.GetProperty("variants")[1];
        Assert.Equal("A", a.GetProperty("key").GetString());
        Assert.Equal(120, a.GetProperty("assigned").GetInt32());
        Assert.Equal(12, a.GetProperty("submissions").GetInt32());
        Assert.Equal(6, a.GetProperty("approved").GetInt32());
        Assert.Equal(0.1, a.GetProperty("submissionRate").GetDouble(), 9);
        Assert.Equal(0.5, a.GetProperty("approvalRate").GetDouble(), 9);
        Assert.Equal(120, b.GetProperty("assigned").GetInt32());
        Assert.Equal(30, b.GetProperty("submissions").GetInt32());
        Assert.Equal(31, b.GetProperty("totalSubmissions").GetInt32()); // one participant submitted twice
        Assert.Equal(20, b.GetProperty("approved").GetInt32());
        Assert.Equal(0.25, b.GetProperty("submissionRate").GetDouble(), 9);

        var comparison = Assert.Single(results.GetProperty("comparisons").EnumerateArray().ToList());
        Assert.Equal("B", comparison.GetProperty("variantKey").GetString());
        Assert.Equal(0.15, comparison.GetProperty("absoluteLift").GetDouble(), 9);
        Assert.Equal(1.5, comparison.GetProperty("relativeLift").GetDouble(), 9);
        Assert.Equal(3.0578831486257534, comparison.GetProperty("zScore").GetDouble(), 6);
        Assert.Equal(0.0022290647783154535, comparison.GetProperty("pValue").GetDouble(), 6);
        Assert.True(comparison.GetProperty("significant").GetBoolean());
    }

    /// <summary>Stores assignments and submissions for one variant arm (real users, accounts and submissions).</summary>
    private async Task ArrangeArmAsync(Campaign campaign, Guid experimentId, Guid variantId, int assigned, int submitters, int approved,
        bool extraSubmissionForFirst = false)
    {
        var now = api.Now();
        var ruleSetId = await api.WithDbAsync(db => db.Set<OptimizeAll.Domain.Rewards.RewardRuleSet>()
            .Where(r => r.CampaignId == campaign.Id).Select(r => r.Id).FirstAsync());
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < assigned; i++)
            {
                var subject = $"user:{Guid.NewGuid():D}";
                if (i < submitters)
                {
                    var tag = Guid.NewGuid().ToString("N");
                    var user = new User
                    {
                        Email = $"x{tag}@example.test", NormalizedEmail = $"X{tag}@EXAMPLE.TEST", DisplayName = "Arm user",
                        CountryCode = "PK", EmailVerifiedAt = now, ReferralCode = tag[..12].ToUpperInvariant(), PasswordHash = "unused",
                    };
                    var account = new SocialAccount
                    {
                        UserId = user.Id, Platform = SocialPlatform.Instagram, Handle = "a" + tag[..20], NormalizedHandle = "a" + tag[..20],
                        ProfileUrl = "https://social.example.com/" + tag, AccountCreatedAt = now.AddDays(-500), FollowerCount = 100,
                    };
                    db.Set<User>().Add(user);
                    db.Set<SocialAccount>().Add(account);
                    var count = extraSubmissionForFirst && i == 0 ? 2 : 1;
                    for (var s = 0; s < count; s++)
                    {
                        var url = $"https://social.example.com/p/{Guid.NewGuid():N}";
                        db.Set<Submission>().Add(new Submission
                        {
                            CampaignId = campaign.Id, UserId = user.Id, SocialAccountId = account.Id, Platform = SocialPlatform.Instagram,
                            PostUrl = url, NormalizedPostUrl = url, PostedAt = now, SubmittedAt = now,
                            Status = i < approved && s == 0 ? SubmissionStatus.Approved : SubmissionStatus.Pending,
                            RewardRuleSetId = ruleSetId, RewardRuleSetVersion = 1, RewardCurrency = "USD", ExperimentVariantId = variantId,
                        });
                    }
                    subject = $"user:{user.Id:D}";
                }
                db.Set<ExperimentAssignment>().Add(new ExperimentAssignment
                {
                    ExperimentId = experimentId, VariantId = variantId, SubjectKey = subject, AssignedAt = now,
                });
            }
            await db.SaveChangesAsync();
        });
    }
}
