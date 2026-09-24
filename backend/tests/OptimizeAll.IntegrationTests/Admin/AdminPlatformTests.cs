using System.Net.Http.Json;
using System.Text.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Accounts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Admin;

public sealed class AdminPlatformTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Settings_list_defaults_and_validate_updates()
    {
        var (adminUser, admin) = await api.AdminAsync();
        var list = await (await admin.GetAsync("/api/v1/admin/settings")).ReadJsonAsync();
        var keys = list.EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        Assert.Equal(10, keys.Count);
        var fourEyes = list.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "rates.fourEyesIncreasePercent");
        Assert.Equal(0, fourEyes.GetProperty("defaultValue").GetInt32());
        var inactivity = list.EnumerateArray().Single(s => s.GetProperty("key").GetString() == "retention.inactivityDays");
        Assert.Equal(30, inactivity.GetProperty("value").GetInt32());
        Assert.Equal(30, inactivity.GetProperty("defaultValue").GetInt32());
        Assert.True(inactivity.GetProperty("isDefault").GetBoolean());
        Assert.False(string.IsNullOrEmpty(inactivity.GetProperty("description").GetString()));

        async Task Bad(string key, object value, int status = 400, string? code = "settings.invalid_value") =>
            await (await admin.PutAsJsonAsync($"/api/v1/admin/settings/{key}", new { value, reason = "test", confirm = true })).ShouldFailAsync(status, code);

        await Bad("eligibility.minAccountAgeDays", 4000);
        await Bad("eligibility.minAccountAgeDays", -1);
        await Bad("eligibility.minAccountAgeDays", 12.5);
        await Bad("eligibility.minAccountAgeDays", "90");
        await Bad("eligibility.minFollowers", 10_000_001);
        await Bad("review.claimMinutes", 0);
        await Bad("retention.inactivityDays", 6);
        await Bad("retention.enabled", "yes");
        await Bad("rates.fourEyesIncreasePercent", -1);
        await Bad("rates.fourEyesIncreasePercent", 1001);
        await Bad("referral.program", new { enabled = true, referrerRewardAmount = -1, currency = "USD", qualifyingAction = "FirstApprovedSubmission", qualifyWithinDays = 30 });
        await Bad("referral.program", new { referrerRewardAmount = 5, currency = "XXX", qualifyingAction = "FirstApprovedSubmission", qualifyWithinDays = 30 });
        await Bad("referral.program", new { referrerRewardAmount = 5, currency = "USD", qualifyingAction = "SignedUp", qualifyWithinDays = 30 });
        await Bad("referral.program", new { referrerRewardAmount = 5, currency = "USD", qualifyingAction = "EmailVerified", qualifyWithinDays = 366 });
        await Bad("referral.program", new { referrerRewardAmount = 5, currency = "USD", qualifyingAction = "EmailVerified", qualifyWithinDays = 30, typo = 1 });
        await Bad("no.such.key", 1, 404, "setting.not_found");
        await (await admin.PutAsJsonAsync("/api/v1/admin/settings/review.claimMinutes", new { value = 20, reason = "test" }))
            .ShouldFailAsync(400, "admin.confirmation_required");

        var updated = await (await admin.PutAsJsonAsync("/api/v1/admin/settings/referral.program", new
        {
            value = new { enabled = true, referrerRewardAmount = 7.5, currency = "eur", qualifyingAction = "firstpaidpayout", qualifyWithinDays = 90, requireManualApproval = false, maxRewardedReferralsPerUser = 20 },
            reason = "Q4 referral push", confirm = true,
        })).ReadJsonAsync();
        Assert.False(updated.GetProperty("isDefault").GetBoolean());
        Assert.Equal("EUR", updated.GetProperty("value").GetProperty("currency").GetString());
        Assert.Equal("FirstPaidPayout", updated.GetProperty("value").GetProperty("qualifyingAction").GetString());
        Assert.Equal(adminUser.Id, updated.GetProperty("updatedBy").GetProperty("id").GetGuid());

        var claim = await (await admin.PutAsJsonAsync("/api/v1/admin/settings/review.claimMinutes", new { value = 20, reason = "Longer reviews", confirm = true })).ReadJsonAsync();
        Assert.Equal(20, claim.GetProperty("value").GetInt32());
        var retention = await (await admin.PutAsJsonAsync("/api/v1/admin/settings/retention.enabled", new { value = false, reason = "Pause automations", confirm = true })).ReadJsonAsync();
        Assert.False(retention.GetProperty("value").GetBoolean());

        // Audit trail with before/after.
        var audit = await (await admin.GetAsync("/api/v1/admin/audit-logs?action=admin.setting_changed&entityId=review.claimMinutes")).ReadJsonAsync();
        var entry = Assert.Single(audit.GetProperty("items").EnumerateArray());
        Assert.Equal(15, entry.GetProperty("before").GetProperty("value").GetInt32());
        Assert.Equal(20, entry.GetProperty("after").GetProperty("value").GetInt32());
        Assert.Equal("Longer reviews", entry.GetProperty("reason").GetString());
        Assert.Equal(adminUser.Email, entry.GetProperty("actorEmail").GetString());
    }

    [Fact]
    public async Task Audit_log_filters_and_csv_export()
    {
        var (adminUser, admin) = await api.AdminAsync();
        var target = await api.CreateUserAsync();
        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/tier", new { tier = "Silver", reason = "Audit test" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/tier", new { tier = "Gold", reason = "Audit test 2" })).EnsureSuccessStatusCode();

        var byEntity = await (await admin.GetAsync($"/api/v1/admin/audit-logs?entityType=User&entityId={target.Id}")).ReadJsonAsync();
        Assert.Equal(2, byEntity.GetProperty("total").GetInt32());
        var items = byEntity.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("Audit test 2", items[0].GetProperty("reason").GetString()); // newest first
        Assert.Equal("Gold", items[0].GetProperty("after").GetProperty("tier").GetString());

        var byActor = await (await admin.GetAsync($"/api/v1/admin/audit-logs?actorUserId={adminUser.Id}&action=admin.")).ReadJsonAsync();
        Assert.Equal(2, byActor.GetProperty("total").GetInt32());

        var now = api.Clock.GetUtcNow().UtcDateTime;
        var future = await (await admin.GetAsync($"/api/v1/admin/audit-logs?actorUserId={adminUser.Id}&from={Uri.EscapeDataString(now.AddMinutes(5).ToString("O"))}")).ReadJsonAsync();
        Assert.Equal(0, future.GetProperty("total").GetInt32());
        var window = await (await admin.GetAsync($"/api/v1/admin/audit-logs?actorUserId={adminUser.Id}&from={Uri.EscapeDataString(now.AddMinutes(-5).ToString("O"))}&to={Uri.EscapeDataString(now.AddMinutes(5).ToString("O"))}")).ReadJsonAsync();
        Assert.Equal(2, window.GetProperty("total").GetInt32());

        var csv = await admin.GetAsync($"/api/v1/admin/audit-logs/export.csv?entityId={target.Id}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 BOM for spreadsheet apps
        var lines = System.Text.Encoding.UTF8.GetString(bytes[3..]).Trim().Split('\n');
        Assert.StartsWith("id,createdAt,actorUserId", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("admin.user_tier_changed", lines[1]);

        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        (await finance.GetAsync("/api/v1/admin/audit-logs")).EnsureSuccessStatusCode(); // Finance holds audit.view
    }

    [Fact]
    public async Task Jobs_can_be_listed_and_run_now()
    {
        var (_, admin) = await api.AdminAsync();
        var jobs = await (await admin.GetAsync("/api/v1/admin/jobs")).ReadJsonAsync();
        var dispatch = jobs.EnumerateArray().Single(j => j.GetProperty("name").GetString() == "NotificationDispatchJob");
        Assert.Equal(30, dispatch.GetProperty("intervalSeconds").GetInt32());

        var run = await (await admin.PostAsync("/api/v1/admin/jobs/NotificationDispatchJob/run", null)).ReadJsonAsync();
        Assert.Equal("Succeeded", run.GetProperty("status").GetString());
        Assert.StartsWith("Claimed", run.GetProperty("summary").GetString());

        jobs = await (await admin.GetAsync("/api/v1/admin/jobs")).ReadJsonAsync();
        dispatch = jobs.EnumerateArray().Single(j => j.GetProperty("name").GetString() == "NotificationDispatchJob");
        Assert.Equal(run.GetProperty("id").GetGuid(), dispatch.GetProperty("lastRun").GetProperty("id").GetGuid());

        var runs = await (await admin.GetAsync("/api/v1/admin/jobs/runs?jobName=NotificationDispatchJob&status=Succeeded")).ReadJsonAsync();
        Assert.True(runs.GetProperty("total").GetInt32() >= 1);

        await (await admin.PostAsync("/api/v1/admin/jobs/NoSuchJob/run", null)).ShouldFailAsync(404);

        // jobs.view is Admin-only.
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.GetAsync("/api/v1/admin/jobs")).ShouldFailAsync(403);
    }
}
