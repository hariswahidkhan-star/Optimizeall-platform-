using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Settings;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Rates.RatesKit;

namespace OptimizeAll.IntegrationTests.Rates;

/// <summary>Rate cards, groups and assignments: CRUD, lifecycle, validation, permissions, concurrency, bulk and CSV.</summary>
public sealed class RateCardsAndGroupsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly RatesKit kit = new(api);

    // ---------------------------------------------------------------- rate cards

    [Fact]
    public async Task Card_lifecycle_create_update_version_duplicate_archive()
    {
        var (_, manager) = await kit.ManagerAsync();
        var name = "Lifecycle " + Guid.NewGuid().ToString("N")[..6];
        var created = await PostCardAsync(manager, name, lines: new[] { Line(10m), Line(12m, "Instagram"), Line(15m, "Instagram", "ShortVideo", label: "Reel fee") },
            activate: false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var card = await created.ReadJsonAsync();
        var id = Id(card);
        Assert.Equal("Draft", card.GetProperty("status").GetString());
        Assert.Equal(1, card.GetProperty("currentVersion").GetInt32());
        Assert.Equal(3, card.GetProperty("versions")[0].GetProperty("lines").GetArrayLength());

        // Drafts are not assignable.
        var participant = await kit.Campaigns.ParticipantAsync();
        await (await PostAssignmentAsync(manager, id, participant.User.Id)).ShouldFailAsync(409, "rate_card.not_active");

        // Duplicate names are refused (case-insensitive).
        await (await PostCardAsync(manager, name.ToUpperInvariant())).ShouldFailAsync(409, "rate_card.name_taken");

        // Metadata edit with the stamp; a stale stamp is a 409.
        var renamed = await (await manager.PutAsJsonAsync($"/api/v1/admin/rate-cards/{id}",
            new { name = name + " (renamed)", description = "d", concurrencyStamp = Stamp(card) })).ReadJsonAsync();
        await (await manager.PutAsJsonAsync($"/api/v1/admin/rate-cards/{id}",
            new { name = name + " x", description = "d", concurrencyStamp = Stamp(card) })).ShouldFailAsync(409, "concurrency.conflict");

        var active = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/activate", new { concurrencyStamp = Stamp(renamed) })).ReadJsonAsync();
        Assert.Equal("Active", active.GetProperty("status").GetString());

        // New version; a stale base version and a past effective date are refused; confirm is required.
        var v2 = await (await PostVersionAsync(manager, id, new[] { Line(11m) }, baseVersion: 1)).ReadJsonAsync();
        Assert.Equal(2, v2.GetProperty("currentVersion").GetInt32());
        await (await PostVersionAsync(manager, id, new[] { Line(12m) }, baseVersion: 1)).ShouldFailAsync(409, "rate_card.version_conflict");
        await (await PostVersionAsync(manager, id, new[] { Line(12m) }, effectiveFrom: kit.Now.AddDays(-2))).ShouldFailAsync(400, "rate_card.effective_in_past");
        await (await PostVersionAsync(manager, id, new[] { Line(12m) }, confirm: false)).ShouldFailAsync(400, "confirmation.required");
        // A future version is saved but not in force yet.
        var future = await (await PostVersionAsync(manager, id, new[] { Line(20m) }, effectiveFrom: kit.Now.AddDays(3))).ReadJsonAsync();
        var versions = future.GetProperty("versions").EnumerateArray().ToList();
        Assert.True(versions.Single(v => v.GetProperty("version").GetInt32() == 2).GetProperty("isCurrent").GetBoolean());
        Assert.False(versions.Single(v => v.GetProperty("version").GetInt32() == 3).GetProperty("isCurrent").GetBoolean());

        // Duplicate copies the latest approved version into a new draft.
        var copy = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/duplicate", new { name = name + " copy" })).ReadJsonAsync();
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal(20m, copy.GetProperty("versions")[0].GetProperty("lines")[0].GetProperty("amount").GetDecimal());

        // Archiving a card in use needs endAssignments.
        var assignment = await AssignAsync(manager, id, participant.User.Id);
        var current = await (await manager.GetAsync($"/api/v1/admin/rate-cards/{id}")).ReadJsonAsync();
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/archive",
            new { reason = "Retired card", concurrencyStamp = Stamp(current) })).ShouldFailAsync(409, "rate_card.in_use");
        var archived = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/archive",
            new { reason = "Retired card", endAssignments = true, concurrencyStamp = Stamp(current) })).ReadJsonAsync();
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        var ended = await (await manager.GetAsync($"/api/v1/admin/rate-assignments/{Id(assignment)}")).ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, ended.GetProperty("endedAt").ValueKind);
        Assert.False(ended.GetProperty("isActive").GetBoolean());
        await (await PostVersionAsync(manager, id, new[] { Line(1m) })).ShouldFailAsync(409, "rate_card.archived");

        // Everything is audited with the reason.
        var actions = await api.WithDbAsync(db => db.Set<Domain.Audit.AuditLog>().Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("rate_card.created", actions);
        Assert.Contains("rate_card.version_created", actions);
        Assert.Contains("rate_card.archived", actions);
        Assert.Contains("rate_card.activated", actions);

        // Listing: archived cards are hidden by default and found with the status filter.
        var list = await (await manager.GetAsync($"/api/v1/admin/rate-cards?search={Uri.EscapeDataString(name)}")).ReadJsonAsync();
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), c => Id(c) == id);
        var archivedList = await (await manager.GetAsync($"/api/v1/admin/rate-cards?status=Archived&search={Uri.EscapeDataString(name)}")).ReadJsonAsync();
        Assert.Contains(archivedList.GetProperty("items").EnumerateArray(), c => Id(c) == id);
    }

    [Fact]
    public async Task Invalid_cards_are_rejected_with_problem_details()
    {
        var (_, manager) = await kit.ManagerAsync();
        var dup = await PostCardAsync(manager, lines: new[] { Line(5m, "TikTok"), Line(6m, "TikTok") });
        await dup.ShouldFailAsync(400, "rate_card.invalid");
        var problem = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal("application/problem+json", dup.Content.Headers.ContentType?.MediaType);

        await (await PostCardAsync(manager, currency: "XYZ")).ShouldFailAsync(400);
        await (await PostCardAsync(manager, lines: new[] { Line(-1m) })).ShouldFailAsync(400);
        await (await PostCardAsync(manager, lines: Array.Empty<object>())).ShouldFailAsync(400);
        // Undefined enum values are a 400, not a silent default.
        await (await PostCardAsync(manager, lines: new[] { Line(5m, "MySpace") })).ShouldFailAsync(400);
        await (await PostCardAsync(manager, lines: new[] { Line(5m, format: "Hologram") })).ShouldFailAsync(400);
        await (await manager.GetAsync($"/api/v1/admin/rate-cards/{Guid.NewGuid()}")).ShouldFailAsync(404);
    }

    [Fact]
    public async Task Four_eyes_rate_increases_above_the_threshold_wait_for_a_second_person()
    {
        await api.SetSettingAsync(SettingKeys.RatesFourEyesIncreasePercent, 50);
        try
        {
            var (_, manager) = await kit.ManagerAsync();
            var (_, second) = await kit.ManagerAsync();
            var card = await CardAsync(manager, lines: new[] { Line(10m), Line(20m, "Instagram") });
            var id = Id(card);

            // +40% is below the threshold: approved at once.
            var small = await (await PostVersionAsync(manager, id, new[] { Line(14m), Line(20m, "Instagram") })).ReadJsonAsync();
            Assert.Equal(2, small.GetProperty("currentVersion").GetInt32());

            // +100% needs approval and does not price anything meanwhile.
            var big = await (await PostVersionAsync(manager, id, new[] { Line(14m), Line(40m, "Instagram") })).ReadJsonAsync();
            Assert.Equal(2, big.GetProperty("currentVersion").GetInt32());
            var pending = big.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetInt32() == 3);
            Assert.Equal("PendingApproval", pending.GetProperty("status").GetString());
            Assert.Equal(100m, pending.GetProperty("maxIncreasePercent").GetDecimal());
            await (await PostVersionAsync(manager, id, new[] { Line(15m) })).ShouldFailAsync(409, "rate_card.pending_approval");

            // The author can't approve it; another manager can.
            await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/versions/3/approve", new { note = "ok" })).ShouldFailAsync(403, "rates.self_approval");
            var approved = await (await second.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/versions/3/approve", new { note = "Checked the contract" })).ReadJsonAsync();
            Assert.Equal(3, approved.GetProperty("currentVersion").GetInt32());
            await (await second.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/versions/3/approve", new { })).ShouldFailAsync(409, "rate_card.not_pending");

            // Rejection path.
            await PostVersionAsync(manager, id, new[] { Line(40m), Line(40m, "Instagram") });
            var rejected = await (await second.PostAsJsonAsync($"/api/v1/admin/rate-cards/{id}/versions/4/reject", new { reason = "Too high for Q4" })).ReadJsonAsync();
            Assert.Equal("Rejected", rejected.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("version").GetInt32() == 4).GetProperty("status").GetString());
            Assert.Equal(3, rejected.GetProperty("currentVersion").GetInt32());
        }
        finally
        {
            await api.SetSettingAsync(SettingKeys.RatesFourEyesIncreasePercent, 0);
        }
    }

    // ---------------------------------------------------------------- permissions, impersonation, tenancy

    [Fact]
    public async Task Permissions_are_enforced_per_action()
    {
        var (_, manager) = await kit.ManagerAsync();
        var card = await CardAsync(manager);
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        var (_, client) = await api.CreateClientAsync(Role.Client);

        foreach (var c in new[] { reviewer, participant, client })
        {
            await (await c.GetAsync("/api/v1/admin/rate-cards")).ShouldFailAsync(403);
            await (await c.GetAsync("/api/v1/admin/rate-groups")).ShouldFailAsync(403);
            await (await c.GetAsync("/api/v1/admin/rate-assignments")).ShouldFailAsync(403);
            await (await PostCardAsync(c)).ShouldFailAsync(403);
        }
        // Finance can read (rate sources on the ledger) but not change rates.
        await (await finance.GetAsync($"/api/v1/admin/rate-cards/{Id(card)}")).ReadJsonAsync();
        await (await PostCardAsync(finance)).ShouldFailAsync(403);
        await (await finance.PostAsJsonAsync("/api/v1/admin/rate-groups", new { name = "nope" })).ShouldFailAsync(403);

        // A custom role with rates.view only reads.
        var (_, adminClient) = await api.CreateClientAsync(Role.Admin);
        var role = (await (await adminClient.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            name = "Rate viewer " + Guid.NewGuid().ToString("N")[..6], description = "Reads rates", permissions = new[] { "rates.view" },
        })).ReadJsonAsync()).GetProperty("role");
        var viewer = await api.CreateUserAsync(Array.Empty<Role>());
        await (await adminClient.PutAsync($"/api/v1/admin/roles/{Id(role)}/users/{viewer.Id}", null)).ReadJsonAsync();
        var viewerClient = await api.LoginAsync(viewer);
        await (await viewerClient.GetAsync("/api/v1/admin/rate-cards")).ReadJsonAsync();
        await (await viewerClient.GetAsync($"/api/v1/admin/rate-cards/{Id(card)}")).ReadJsonAsync();
        await (await PostCardAsync(viewerClient)).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Rate_writes_are_denied_while_impersonating_but_reads_work()
    {
        var (_, admin) = await api.CreateClientAsync(Role.Admin);
        var manager = await api.CreateUserAsync(new[] { Role.CampaignManager });
        var token = await Impersonating.TokenAsync(admin, manager.Id);
        (await Impersonating.SendAsync(admin, HttpMethod.Get, "/api/v1/admin/rate-cards", token)).EnsureSuccessStatusCode();
        var write = await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/admin/rate-cards", token, new
        {
            name = "Impersonated", currency = "USD", lines = new[] { Line(5m) }, reason = "Should be refused",
        });
        await write.ShouldFailAsync(403, "auth.impersonation_forbidden_action");
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, "/api/v1/admin/rate-groups", token, new { name = "x y" }))
            .ShouldFailAsync(403, "auth.impersonation_forbidden_action");
    }

    // ---------------------------------------------------------------- groups & members

    [Fact]
    public async Task Group_members_bulk_add_classifies_every_person_and_keeps_history()
    {
        var (managerUser, manager) = await kit.ManagerAsync();
        var group = await GroupAsync(manager, "Micro " + Guid.NewGuid().ToString("N")[..6], priority: 30);
        var gid = Id(group);
        await (await manager.PostAsJsonAsync("/api/v1/admin/rate-groups", new { name = group.GetProperty("name").GetString()!.ToLowerInvariant() }))
            .ShouldFailAsync(409, "rate_group.name_taken");

        var a = await api.CreateUserAsync();
        var b = await api.CreateUserAsync();
        var suspended = await api.CreateUserAsync();
        await api.WithDbAsync(async db =>
        {
            await db.Set<User>().Where(u => u.Id == suspended.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Suspended));
        });
        var unknown = Guid.NewGuid();
        var result = await AddMembersAsync(manager, gid, a.Id, b.Id, a.Id, suspended.Id, unknown, managerUser.Id);
        Assert.Equal(3, result.GetProperty("added").GetInt32());
        var rejected = result.GetProperty("rejected").EnumerateArray().ToList();
        Assert.Contains(rejected, r => r.GetProperty("code").GetString() == "user.not_found");
        Assert.Contains(rejected, r => r.GetProperty("code").GetString() == "rates.not_participant");
        var warnings = result.GetProperty("warnings").EnumerateArray().Select(w => w.GetProperty("code").GetString()).ToList();
        Assert.Contains("duplicate", warnings);
        Assert.Contains("user.suspended", warnings);

        // Adding again changes nothing (idempotent).
        var again = await AddMembersAsync(manager, gid, a.Id);
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(1, again.GetProperty("unchanged").GetInt32());

        var members = await (await manager.GetAsync($"/api/v1/admin/rate-groups/{gid}/members?pageSize=50")).ReadJsonAsync();
        Assert.Equal(3, members.GetProperty("total").GetInt32());

        var removed = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{gid}/members/remove",
            new { userIds = new[] { b.Id, unknown }, reason = "Moved to Macro" })).ReadJsonAsync();
        Assert.Equal(1, removed.GetProperty("removed").GetInt32());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{gid}/members/remove",
            new { userIds = new[] { a.Id }, reason = "" })).ShouldFailAsync(400);

        var history = await (await manager.GetAsync($"/api/v1/admin/rate-groups/{gid}/history")).ReadJsonAsync();
        var events = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("action").GetString() == "Removed" && e.GetProperty("reason").GetString() == "Moved to Macro");
        Assert.Equal(4, events.Count);

        // Export.
        var csv = await (await manager.GetAsync($"/api/v1/admin/rate-groups/{gid}/members/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains("userId,email", csv);
        Assert.Contains(a.Email, csv);
        Assert.DoesNotContain(b.Email, csv);
    }

    [Fact]
    public async Task Csv_import_validates_first_then_adds_and_reports_every_row()
    {
        var (_, manager) = await kit.ManagerAsync();
        var gid = Id(await GroupAsync(manager));
        var a = await api.CreateUserAsync();
        var b = await api.CreateUserAsync();
        var staff = await api.CreateUserAsync(new[] { Role.Finance });
        var csv = $"email,name\n{a.Email},A\n{b.Email.ToUpperInvariant()},B\nnobody@example.test,X\nnot-an-email,Y\n{staff.Email},Staff\n{a.Email},dup\n";

        async Task<JsonElement> Upload(bool dryRun)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "members.csv");
            return await (await manager.PostAsync($"/api/v1/admin/rate-groups/{gid}/members/import?dryRun={dryRun}", form)).ReadJsonAsync();
        }

        var dry = await Upload(true);
        Assert.True(dry.GetProperty("dryRun").GetBoolean());
        Assert.Equal(6, dry.GetProperty("rows").GetInt32());
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        var rejectedRows = dry.GetProperty("rejected").EnumerateArray().Select(r => (r.GetProperty("row").GetInt32(), r.GetProperty("code").GetString())).ToList();
        Assert.Contains((4, "user.not_found"), rejectedRows);
        Assert.Contains((5, "csv.invalid_value"), rejectedRows);
        Assert.Contains((6, "rates.not_participant"), rejectedRows);
        Assert.Equal(0, await api.WithDbAsync(db => db.Set<RateGroupMember>().CountAsync(m => m.GroupId == gid)));

        var real = await Upload(false);
        Assert.Equal(2, real.GetProperty("added").GetInt32());
        Assert.Equal(2, await api.WithDbAsync(db => db.Set<RateGroupMember>().CountAsync(m => m.GroupId == gid)));
        Assert.Equal("csv", await api.WithDbAsync(db => db.Set<RateGroupMemberEvent>().Where(e => e.GroupId == gid).Select(e => e.Source).FirstAsync()));

        var empty = new MultipartFormDataContent { { new ByteArrayContent(Array.Empty<byte>()), "file", "empty.csv" } };
        await (await manager.PostAsync($"/api/v1/admin/rate-groups/{gid}/members/import", empty)).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Bulk_add_of_ten_thousand_people_is_batched()
    {
        var (_, manager) = await kit.ManagerAsync();
        var gid = Id(await GroupAsync(manager, "Bulk " + Guid.NewGuid().ToString("N")[..6]));
        var hash = new PasswordHasher<User>().HashPassword(new User(), "unused-password-1");
        var ids = new List<Guid>();
        await api.WithDbAsync(async db =>
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < 10_000; i++)
            {
                var email = $"bulk-{Guid.NewGuid():N}@example.test";
                var user = new User
                {
                    Email = email, NormalizedEmail = email, PasswordHash = hash, DisplayName = $"Bulk {i}", CountryCode = "PK",
                    ReferralCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                };
                user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Participant, GrantedAt = kit.Now });
                db.Add(user);
                ids.Add(user.Id);
                if (i % 1000 == 999)
                {
                    db.ChangeTracker.DetectChanges();
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
            }
        });

        var watch = Stopwatch.StartNew();
        var result = await AddMembersAsync(manager, gid, ids.ToArray());
        watch.Stop();
        Assert.Equal(10_000, result.GetProperty("added").GetInt32());
        Assert.Equal(10_000, await api.WithDbAsync(db => db.Set<RateGroupMember>().CountAsync(m => m.GroupId == gid)));
        Assert.Equal(10_000, await api.WithDbAsync(db => db.Set<RateGroupMemberEvent>().CountAsync(m => m.GroupId == gid)));
        // One audit row for the whole operation, not 10,000.
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<Domain.Audit.AuditLog>().CountAsync(a => a.EntityId == gid.ToString() && a.Action == "rate_group.members_added")));
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(4), $"Bulk add took {watch.Elapsed}.");

        // More than 10,000 in one request is a validation error.
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{gid}/members", new { userIds = Enumerable.Range(0, 10_001).Select(_ => Guid.NewGuid()) }))
            .ShouldFailAsync(400);
    }

    [Fact]
    public async Task Archiving_a_group_in_use_is_refused_unless_forced_and_then_unassigns_with_audit()
    {
        var (_, manager) = await kit.ManagerAsync();
        var group = await GroupAsync(manager);
        var gid = Id(group);
        var card = await CardAsync(manager);
        var member = await api.CreateUserAsync();
        await AddMembersAsync(manager, gid, member.Id);
        var assignment = await AssignAsync(manager, Id(card), groupId: gid);

        group = await (await manager.GetAsync($"/api/v1/admin/rate-groups/{gid}")).ReadJsonAsync();
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{gid}/archive", new { reason = "Merged", concurrencyStamp = Stamp(group) }))
            .ShouldFailAsync(409, "rate_group.in_use");
        var archived = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-groups/{gid}/archive",
            new { reason = "Merged into Micro", force = true, concurrencyStamp = Stamp(group) })).ReadJsonAsync();
        Assert.NotEqual(JsonValueKind.Null, archived.GetProperty("archivedAt").ValueKind);
        Assert.Equal(0, archived.GetProperty("memberCount").GetInt32());
        var ended = await (await manager.GetAsync($"/api/v1/admin/rate-assignments/{Id(assignment)}")).ReadJsonAsync();
        Assert.Contains("Group archived", ended.GetProperty("endReason").GetString());
        Assert.True(await api.WithDbAsync(db => db.Set<RateGroupMemberEvent>().AnyAsync(e => e.GroupId == gid && e.Source == "group_archived")));
        await (await AddMembersAsync2(manager, gid, member.Id)).ShouldFailAsync(409, "rate_group.archived");
    }

    private static Task<HttpResponseMessage> AddMembersAsync2(HttpClient client, Guid groupId, params Guid[] ids) =>
        client.PostAsJsonAsync($"/api/v1/admin/rate-groups/{groupId}/members", new { userIds = ids });

    [Fact]
    public async Task Automatic_groups_list_the_people_matching_their_rule()
    {
        var (_, manager) = await kit.ManagerAsync();
        var group = await GroupAsync(manager, mode: "Automatic", tiers: new[] { "Platinum" }, minFollowers: 4000);
        var gid = Id(group);
        Assert.Contains("tier Platinum", group.GetProperty("autoRule").GetString());
        var match = await kit.Campaigns.ParticipantAsync(tier: ParticipantTier.Platinum); // 5,000 verified followers
        var lowTier = await kit.Campaigns.ParticipantAsync(tier: ParticipantTier.Gold);
        var members = await (await manager.GetAsync($"/api/v1/admin/rate-groups/{gid}/members?pageSize=200")).ReadJsonAsync();
        var ids = members.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("userId").GetGuid()).ToList();
        Assert.Contains(match.User.Id, ids);
        Assert.DoesNotContain(lowTier.User.Id, ids);
        // Members of automatic groups can't be edited by hand.
        await (await AddMembersAsync2(manager, gid, match.User.Id)).ShouldFailAsync(409, "rate_group.automatic");
        // Rule validation.
        await (await manager.PostAsJsonAsync("/api/v1/admin/rate-groups", new { name = "Bad rule", membershipMode = "Automatic" })).ShouldFailAsync(400, "rate_group.invalid_rule");
        await (await manager.PostAsJsonAsync("/api/v1/admin/rate-groups", new { name = "Bad bounds", membershipMode = "Automatic", autoMinFollowers = 10, autoMaxFollowers = 5 }))
            .ShouldFailAsync(400, "rate_group.invalid_rule");
    }

    // ---------------------------------------------------------------- assignments

    [Fact]
    public async Task Assignments_refuse_overlaps_invalid_windows_and_custom_cards()
    {
        var (_, manager) = await kit.ManagerAsync();
        var card = Id(await CardAsync(manager));
        var other = Id(await CardAsync(manager));
        var p = await kit.Campaigns.ParticipantAsync();
        var now = kit.Now;

        var first = await AssignAsync(manager, card, p.User.Id, to: now.AddDays(10));
        Assert.Equal("GlobalPersonalCard", first.GetProperty("level").GetString());
        await (await PostAssignmentAsync(manager, other, p.User.Id, from: now.AddDays(5))).ShouldFailAsync(409, "rates.duplicate_assignment");
        // A window that starts when the first ends is fine.
        var next = await AssignAsync(manager, other, p.User.Id, from: now.AddDays(10), to: now.AddDays(20));
        Assert.True(next.GetProperty("isActive").GetBoolean());

        await (await PostAssignmentAsync(manager, card, p.User.Id, from: now.AddDays(30), to: now.AddDays(29))).ShouldFailAsync(400, "rates.invalid_window");
        await (await PostAssignmentAsync(manager, card, p.User.Id, to: now.AddDays(-1))).ShouldFailAsync(400, "rates.invalid_window");
        await (await manager.PostAsJsonAsync("/api/v1/admin/rate-assignments", new { rateCardId = card, target = "Person", reason = "no target" }))
            .ShouldFailAsync(400, "rates.target_required");
        await (await manager.PostAsJsonAsync("/api/v1/admin/rate-assignments", new { rateCardId = card, target = "Everyone", userId = p.User.Id, reason = "bad enum" }))
            .ShouldFailAsync(400);

        var staff = await api.CreateUserAsync(new[] { Role.Finance });
        await (await PostAssignmentAsync(manager, card, staff.Id)).ShouldFailAsync(400, "rates.not_participant");

        var custom = await CustomRateAsync(manager, p.User.Id, new[] { Line(30m) }, validTo: now.AddDays(3));
        Assert.Equal("GlobalPersonalCustom", custom.GetProperty("level").GetString());
        var customCard = custom.GetProperty("card").GetProperty("id").GetGuid();
        var q = await kit.Campaigns.ParticipantAsync();
        await (await PostAssignmentAsync(manager, customCard, q.User.Id)).ShouldFailAsync(400, "rates.custom_card_not_assignable");
        // A second overlapping custom rate for the same person and scope is refused.
        await (await manager.PostAsJsonAsync($"/api/v1/admin/users/{p.User.Id}/custom-rates",
            new { currency = "USD", lines = new[] { Line(1m) }, reason = "Another deal" })).ShouldFailAsync(409, "rates.duplicate_assignment");

        // Extend the deal (stamp), stale stamp → 409, end it.
        var extended = await (await manager.PutAsJsonAsync($"/api/v1/admin/rate-assignments/{Id(first)}",
            new { validTo = now.AddDays(9), reason = "Shortened", concurrencyStamp = Stamp(first) })).ReadJsonAsync();
        await (await manager.PutAsJsonAsync($"/api/v1/admin/rate-assignments/{Id(first)}",
            new { validTo = now.AddDays(8), reason = "Again", concurrencyStamp = Stamp(first) })).ShouldFailAsync(409, "concurrency.conflict");
        var ended = await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-assignments/{Id(first)}/end",
            new { reason = "Deal cancelled", concurrencyStamp = Stamp(extended) })).ReadJsonAsync();
        Assert.False(ended.GetProperty("isActive").GetBoolean());
        await (await manager.PostAsJsonAsync($"/api/v1/admin/rate-assignments/{Id(first)}/end",
            new { reason = "Deal cancelled", concurrencyStamp = Stamp(ended) })).ShouldFailAsync(409, "rates.assignment_ended");

        var list = await (await manager.GetAsync($"/api/v1/admin/rate-assignments?userId={p.User.Id}&activeOnly=true")).ReadJsonAsync();
        Assert.Equal(2, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Concurrent_overlapping_assignments_produce_exactly_one()
    {
        var (_, manager) = await kit.ManagerAsync();
        var card = Id(await CardAsync(manager));
        var p = await kit.Campaigns.ParticipantAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAssignmentAsync(manager, card, p.User.Id)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Missing_exchange_rates_are_refused_when_assigning_and_when_publishing()
    {
        var (_, manager) = await kit.ManagerAsync();
        var campaign = await kit.CampaignAsync(manager, currency: "USD");
        var bhd = Id(await CardAsync(manager, currency: "BHD", lines: new[] { Line(4.5m) }));
        var p = await kit.Campaigns.ParticipantAsync();
        var fail = await PostAssignmentAsync(manager, bhd, p.User.Id, campaignId: campaign.Id);
        await fail.ShouldFailAsync(409, "rates.fx_missing");
        Assert.Contains("BHD→USD", await fail.Content.ReadAsStringAsync());
        // A global assignment is checked against every live campaign's currency.
        await (await PostAssignmentAsync(manager, bhd, p.User.Id)).ShouldFailAsync(409, "rates.fx_missing");

        await kit.AddExchangeRateAsync("BHD", "USD", 2.65m);
        await AssignAsync(manager, bhd, p.User.Id, campaignId: campaign.Id);

        // Publishing a draft campaign whose applicable rates can't be converted is refused.
        var omr = Id(await CardAsync(manager, currency: "OMR", lines: new[] { Line(3m) }));
        var draft = await kit.CampaignAsync(manager, currency: "USD", publish: false);
        var q = await kit.Campaigns.ParticipantAsync();
        await AssignAsync(manager, omr, q.User.Id, campaignId: draft.Id);
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/publish", null)).ShouldFailAsync(409, "rates.fx_missing");
        var panel = await (await manager.GetAsync($"/api/v1/admin/campaigns/{draft.Id}/rates")).ReadJsonAsync();
        Assert.Contains("OMR→USD", panel.GetProperty("fxProblems").EnumerateArray().Select(x => x.GetString()));
        await kit.AddExchangeRateAsync("OMR", "USD", 2.6m);
        await (await manager.PostAsync($"/api/v1/admin/campaigns/{draft.Id}/publish", null)).ReadJsonAsync();
    }
}
