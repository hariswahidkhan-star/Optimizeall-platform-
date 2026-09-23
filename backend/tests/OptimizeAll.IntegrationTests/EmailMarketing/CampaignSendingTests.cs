using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[Collection(EmailCollection.Name)]
public sealed class CampaignSendingTests(EmailFixture fx)
{
    [Fact]
    public async Task Sends_once_to_each_consented_subscriber_and_never_to_suppressed_or_unconsented_contacts()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var consented = await fx.AddSubscribersAsync(ws, 4);
        var noConsent = await fx.AddSubscribersAsync(ws, 1, (s, _) => s.EmailConsent = ConsentStatus.Unknown);
        var unsubscribed = await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Status = SubscriberStatus.Unsubscribed; s.EmailConsent = ConsentStatus.Withdrawn; });
        var suppressedEarly = await fx.AddSubscribersAsync(ws, 1);
        await fx.Db(async db =>
        {
            var s = await db.Set<Subscriber>().FirstAsync(x => x.Id == suppressedEarly[0]);
            db.Add(new Suppression { ClientAccountId = ws.ClientId, ScopeKey = s.ScopeKey, Channel = MessageChannel.Email, Value = s.NormalizedEmail!, Reason = SuppressionReason.HardBounce, Source = "test", CreatedAt = fx.Now });
            await db.SaveChangesAsync();
        });

        var campaign = await fx.CreateCampaignAsync(staff, ws, extra: new { throttlePerMinute = 1000 });
        var checklist = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{campaign.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.Equal(4, checklist.GetProperty("audienceCount").GetInt32());
        Assert.True(checklist.GetProperty("canSend").GetBoolean());
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);

        // Suppression added after the audience was expanded still wins at send time.
        await fx.RunJobAsync<CampaignSendJob>();
        var recipients = await fx.RecipientsAsync(id);
        Assert.Equal(4, recipients.Count);
        Assert.DoesNotContain(recipients, r => noConsent.Contains(r.SubscriberId) || unsubscribed.Contains(r.SubscriberId) || suppressedEarly.Contains(r.SubscriberId));
        Assert.All(recipients, r => Assert.Equal(RecipientStatus.Sent, r.Status));
        Assert.Equal(4, fx.Email.For(ws.ClientId).Count);
        Assert.Equal(CampaignStatus.Sent, (await fx.CampaignAsync(id)).Status);

        // A second campaign: one contact is suppressed between expansion and sending (paused in between).
        var second = await fx.CreateCampaignAsync(staff, ws, extra: new { throttlePerMinute = 1000 });
        var secondId = Guid.Parse(second.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, second);
        await fx.Db(async db =>
        {
            var c = await db.Set<EmailCampaign>().FirstAsync(x => x.Id == secondId);
            c.Status = CampaignStatus.Sending;
            c.SendStartedAt = fx.Now;
            await db.SaveChangesAsync();
        });
        using (var scope = fx.App.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CampaignSendJob>().ExpandAsync(await fx.CampaignAsync(secondId), default);
        await fx.Db(async db =>
        {
            var s = await db.Set<Subscriber>().FirstAsync(x => x.Id == consented[0]);
            db.Add(new Suppression { ClientAccountId = ws.ClientId, ScopeKey = s.ScopeKey, Channel = MessageChannel.Email, Value = s.NormalizedEmail!, Reason = SuppressionReason.Complaint, Source = "test", CreatedAt = fx.Now });
            await db.SaveChangesAsync();
        });
        await fx.RunJobAsync<CampaignSendJob>();
        var secondRecipients = await fx.RecipientsAsync(secondId);
        var blocked = Assert.Single(secondRecipients, r => r.SubscriberId == consented[0]);
        Assert.Equal(RecipientStatus.Skipped, blocked.Status);
        Assert.Contains("Suppressed", blocked.SkipReason);
        Assert.Equal(3, secondRecipients.Count(r => r.Status == RecipientStatus.Sent));
    }

    [Fact]
    public async Task Expansion_and_sending_are_idempotent_under_reruns_and_concurrent_workers()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var subscribers = await fx.AddSubscribersAsync(ws, 30);
        var campaign = await fx.CreateCampaignAsync(staff, ws, extra: new { throttlePerMinute = 10_000 });
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, campaign);

        fx.Email.Delay = TimeSpan.FromMilliseconds(5);
        try
        {
            // Two workers at once (bypassing the job lease, as two instances would), then a plain re-run.
            await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                using var scope = fx.App.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CampaignSendJob>().ExecuteAsync(default);
            }));
            await fx.RunJobAsync<CampaignSendJob>();
            await fx.RunJobAsync<CampaignSendJob>();
        }
        finally { fx.Email.Delay = TimeSpan.Zero; }

        var recipients = await fx.RecipientsAsync(id);
        Assert.Equal(30, recipients.Count);
        Assert.Equal(30, recipients.Select(r => r.SubscriberId).Distinct().Count());
        Assert.All(recipients, r => Assert.Equal(RecipientStatus.Sent, r.Status));
        var sends = fx.Email.For(ws.ClientId).Where(e => e.Metadata["oa_ref"] is var reference && recipients.Any(r => reference == "c:" + r.Id.ToString("N"))).ToList();
        Assert.Equal(30, sends.Count);
        Assert.Equal(30, sends.Select(e => e.To).Distinct().Count());
        Assert.Equal(30, (await fx.CampaignAsync(id)).RecipientCount);
    }

    [Fact]
    public async Task Throttling_limits_messages_per_minute()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 12);
        var campaign = await fx.CreateCampaignAsync(staff, ws, extra: new { throttlePerMinute = 5 });
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, campaign);

        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(5, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(5, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(61));
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(10, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));
        fx.Api.Clock.Advance(TimeSpan.FromSeconds(61));
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(12, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));
        Assert.Equal(CampaignStatus.Sent, (await fx.CampaignAsync(id)).Status);
    }

    [Fact]
    public async Task Pause_and_cancel_stop_sending_mid_campaign()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 9);
        var campaign = await fx.CreateCampaignAsync(staff, ws, extra: new { throttlePerMinute = 3 });
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(3, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));

        var paused = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/pause", new { reason = "Typo in hero" })).ReadJsonAsync();
        Assert.Equal("Paused", paused.GetProperty("status").GetString());
        fx.Api.Clock.Advance(TimeSpan.FromMinutes(2));
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(3, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));

        var resumed = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/resume", new { })).ReadJsonAsync();
        Assert.Equal("Sending", resumed.GetProperty("status").GetString());
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(6, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));

        var cancelled = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/cancel", new { reason = "Wrong audience" })).ReadJsonAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        fx.Api.Clock.Advance(TimeSpan.FromMinutes(2));
        await fx.RunJobAsync<CampaignSendJob>();
        var recipients = await fx.RecipientsAsync(id);
        Assert.Equal(6, recipients.Count(r => r.Status == RecipientStatus.Sent));
        Assert.Equal(3, recipients.Count(r => r.Status == RecipientStatus.Cancelled));
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/resume", new { })).ShouldFailAsync(409);
    }

    [Fact]
    public async Task Provider_not_configured_pauses_the_campaign_without_marking_anything_sent()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 2);
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        fx.Email.NextOutcome = ProviderOutcome.NotConfigured;
        try { await fx.RunJobAsync<CampaignSendJob>(); }
        finally { fx.Email.NextOutcome = ProviderOutcome.Accepted; }
        var c = await fx.CampaignAsync(id);
        Assert.Equal(CampaignStatus.Paused, c.Status);
        Assert.Contains("not configured", c.PauseReason);
        Assert.DoesNotContain(await fx.RecipientsAsync(id), r => r.Status == RecipientStatus.Sent);
    }

    [Fact]
    public async Task Ab_test_sends_to_a_cohort_then_the_winner_by_open_rate_to_the_rest()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 60);
        var campaign = await fx.CreateCampaignAsync(staff, ws, extra: new
        {
            type = "AbTest", abTestPercent = 50, abWinnerMetric = "OpenRate", abWaitHours = 2, throttlePerMinute = 10_000,
            variants = new object[] { new { key = "A" }, new { key = "B", subject = "Variant B subject" } },
        });
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        await fx.RunJobAsync<CampaignSendJob>();

        var recipients = await fx.RecipientsAsync(id);
        var cohort = recipients.Where(r => r.IsTestCohort).ToList();
        Assert.All(cohort, r => Assert.Equal(RecipientStatus.Sent, r.Status));
        Assert.All(recipients.Where(r => !r.IsTestCohort), r => Assert.Equal(RecipientStatus.Held, r.Status));
        Assert.Contains(cohort, r => r.Variant == "A");
        Assert.Contains(cohort, r => r.Variant == "B");
        Assert.Contains(fx.Email.For(ws.ClientId), e => e.Subject == "Variant B subject");

        // Variant B gets opened (human), A does not.
        await fx.Db(async db =>
        {
            foreach (var r in await db.Set<CampaignRecipient>().Where(x => x.CampaignId == id && x.Variant == "B").ToListAsync())
            {
                r.OpenedAt = fx.Now;
                r.OpenCount = 1;
            }
            foreach (var r in await db.Set<CampaignRecipient>().Where(x => x.CampaignId == id && x.Variant == "A").ToListAsync()) r.MachineOpenCount = 3;
            await db.SaveChangesAsync();
        });
        await fx.RunJobAsync<CampaignSendJob>(); // marks the test phase complete; waiting starts
        Assert.Null((await fx.CampaignAsync(id)).AbWinnerVariant);
        fx.Api.Clock.Advance(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(1)));
        await fx.RunJobAsync<CampaignSendJob>();

        var c = await fx.CampaignAsync(id);
        Assert.Equal("B", c.AbWinnerVariant);
        var after = await fx.RecipientsAsync(id);
        Assert.All(after.Where(r => !r.IsTestCohort), r =>
        {
            Assert.Equal(RecipientStatus.Sent, r.Status);
            Assert.Equal("B", r.Variant);
        });
        Assert.Equal(CampaignStatus.Sent, c.Status);

        var report = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{id}/report")).ReadJsonAsync();
        var variants = report.GetProperty("variants").EnumerateArray().ToList();
        Assert.True(variants.Single(v => v.GetProperty("key").GetString() == "B").GetProperty("winner").GetBoolean());
        Assert.True(report.GetProperty("machineOpens").GetInt32() > 0);
    }

    [Fact]
    public async Task Messages_carry_one_click_unsubscribe_headers_and_the_one_click_post_unsubscribes()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var ids = await fx.AddSubscribersAsync(ws, 1);
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        await fx.RunJobAsync<CampaignSendJob>();

        var message = fx.Email.For(ws.ClientId).Last();
        Assert.Equal("List-Unsubscribe=One-Click", message.Headers["List-Unsubscribe-Post"]);
        var header = message.Headers["List-Unsubscribe"];
        Assert.StartsWith("<http://app.test/e/u/", header);
        Assert.Contains(header.Trim('<', '>'), message.Html); // the footer link is the same signed URL
        Assert.Contains("1 Main Street, Springfield", message.Html);
        Assert.Contains("/e/o/", message.Html); // open pixel
        Assert.Contains("/e/c/", message.Html); // tracked link

        var path = new Uri(header.Trim('<', '>')).AbsolutePath;
        var anonymous = fx.Anonymous();
        // GET only shows the confirmation page (link scanners must not unsubscribe people).
        var get = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.StartsWith("http://app.test/email/unsubscribe/", get.Headers.Location!.ToString());
        var stillSubscribed = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == ids[0]));
        Assert.Equal(SubscriberStatus.Subscribed, stillSubscribed.Status);

        var post = await anonymous.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == ids[0]));
        Assert.Equal(SubscriberStatus.Unsubscribed, s.Status);
        Assert.Equal(ConsentStatus.Withdrawn, s.EmailConsent);
        Assert.True(await fx.Db(db => db.Set<Suppression>().AnyAsync(x => x.Value == s.NormalizedEmail && x.Reason == SuppressionReason.Unsubscribed)));
        // Idempotent.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsync(path + "x", null)).StatusCode);
    }

    [Fact]
    public async Task Sending_requires_email_send_the_typed_name_and_a_clean_checklist()
    {
        var admin = await fx.StaffAsync();
        var manager = await fx.StaffAsync(Role.AccountManager);
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 2);
        var campaign = await fx.CreateCampaignAsync(manager, ws, name: "October update");
        var id = campaign.GetProperty("id").GetString();

        // email.manage can author but not send.
        await (await manager.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/send", new { confirm = true, confirmName = "October update" })).ShouldFailAsync(403);
        await (await admin.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/send", new { confirm = true, confirmName = "october update" }))
            .ShouldFailAsync(400, "email.confirm_name_mismatch");
        await (await admin.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/send", new { confirm = false, confirmName = "October update" }))
            .ShouldFailAsync(400, "email.confirm_required");

        // A design without the footer (address + unsubscribe) is blocked.
        var noFooter = await fx.CreateCampaignAsync(admin, ws, name: "No footer", design: EmailFixture.Design(footer: false));
        var checklist = await (await admin.GetAsync($"/api/v1/agency/email/campaigns/{noFooter.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.False(checklist.GetProperty("canSend").GetBoolean());
        Assert.Contains(checklist.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "footer" && i.GetProperty("status").GetString() == "Fail");
        await (await admin.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{noFooter.GetProperty("id").GetString()}/send",
            new { confirm = true, confirmName = "No footer" })).ShouldFailAsync(400, "email.checklist_failed");

        // Unverified sender and missing address are blockers too.
        await fx.Db(async db =>
        {
            var sender = await db.Set<SenderProfile>().FirstAsync(s => s.Id == ws.SenderId);
            sender.VerifiedAt = null;
            await db.SaveChangesAsync();
        });
        checklist = await (await admin.GetAsync($"/api/v1/agency/email/campaigns/{id}/checklist")).ReadJsonAsync();
        Assert.Contains(checklist.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "sender" && i.GetProperty("status").GetString() == "Fail");
        await fx.Db(async db =>
        {
            var sender = await db.Set<SenderProfile>().FirstAsync(s => s.Id == ws.SenderId);
            sender.VerifiedAt = fx.Now;
            await db.SaveChangesAsync();
        });

        var sent = await (await admin.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/send", new { confirm = true, confirmName = "October update", reason = "Approved by AM" })).ReadJsonAsync();
        Assert.Equal("Scheduled", sent.GetProperty("status").GetString());
        // Cannot be edited or sent twice once confirmed.
        await (await admin.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/send", new { confirm = true, confirmName = "October update" })).ShouldFailAsync(409);
        Assert.True(await fx.Db(db => db.Set<OptimizeAll.Domain.Audit.AuditLog>().AnyAsync(a => a.Action == "email.campaign.send_confirmed" && a.EntityId == id)));
    }
}
