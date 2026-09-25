using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

/// <summary>
/// An admin "viewing as" a user must not message a client's audience on their behalf, like payouts: confirming or
/// scheduling an email/SMS campaign, resuming a paused one, activating a journey or enrolling contacts in it, and a
/// client's approval of a send all answer 403 <c>auth.impersonation_forbidden_action</c> and change nothing. Test sends
/// (verified staff addresses only), pausing, unscheduling and cancelling stay available.
/// </summary>
[Collection(EmailCollection.Name)]
public sealed class ImpersonatedSendTests(EmailFixture fx)
{
    private const string Forbidden = "auth.impersonation_forbidden_action";
    private const string Campaigns = "/api/v1/agency/email/campaigns";
    private const string SmsCampaigns = "/api/v1/agency/email/sms/campaigns";

    private async Task<(HttpClient Admin, string Token)> ViewAsAsync(Guid userId)
    {
        var admin = await fx.StaffAsync(Role.Admin);
        return (admin, await Impersonating.TokenAsync(admin, userId));
    }

    private static object ConfirmBody(JsonElement c) => new
    {
        confirm = true, confirmName = c.GetProperty("name").GetString(), concurrencyStamp = c.GetProperty("concurrencyStamp").GetString(),
    };

    private static object StampBody(JsonElement c) => new { concurrencyStamp = c.GetProperty("concurrencyStamp").GetString(), reason = "Impersonation test" };

    private Task<bool> AuditedAsync(string action, Guid entityId) =>
        fx.Db(db => db.Set<AuditLog>().AnyAsync(a => a.Action == action && a.EntityId == entityId.ToString()));

    [Fact]
    public async Task Viewing_as_an_account_manager_cannot_send_schedule_or_resume_campaigns_but_can_test_pause_and_cancel()
    {
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 3);
        var am = await fx.Api.CreateUserAsync(new[] { Role.AccountManager });
        var amClient = await fx.LoginAsync(am);
        var (admin, token) = await ViewAsAsync(am.Id);
        var adminUser = await fx.StaffUserAsync(Role.Admin);

        // Send now and scheduled sends are both refused; nothing changes and nothing is audited as confirmed.
        var campaign = await fx.CreateCampaignAsync(amClient, ws);
        var id = campaign.GetProperty("id").GetGuid();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{id}/send", token, ConfirmBody(campaign))).ShouldFailAsync(403, Forbidden);
        var scheduled = await fx.CreateCampaignAsync(amClient, ws, extra: new { scheduleMode = "FixedTime", scheduledAt = fx.Now.AddDays(2) });
        var scheduledId = scheduled.GetProperty("id").GetGuid();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{scheduledId}/send", token, ConfirmBody(scheduled)))
            .ShouldFailAsync(403, Forbidden);
        Assert.Equal(CampaignStatus.Draft, (await fx.CampaignAsync(id)).Status);
        Assert.Equal(CampaignStatus.Draft, (await fx.CampaignAsync(scheduledId)).Status);
        Assert.False(await AuditedAsync("email.campaign.send_confirmed", id));
        Assert.False(await AuditedAsync("email.campaign.send_confirmed", scheduledId));
        // The refused attempt is on record as an impersonation request.
        // (AfterJson is a JSON column on MySQL: compare in memory.)
        var requests = await fx.Db(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "impersonation.request" && a.ActorUserId == am.Id).Select(a => a.AfterJson).ToListAsync());
        Assert.Contains(requests, after => after!.Contains($"/{id}/send") && after.Contains("403"));

        // A test send only reaches a verified staff address: allowed.
        var test = await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{id}/test", token, new { to = adminUser.Email });
        Assert.True((await test.ReadJsonAsync()).GetProperty("sent").GetBoolean());

        // The account manager sends it themselves; viewed-as, pausing is allowed, resuming is not, cancelling is.
        var sent = await EmailFixture.ConfirmSendAsync(amClient, campaign);
        Assert.Equal("Scheduled", sent.GetProperty("status").GetString());
        var paused = await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{id}/pause", token, StampBody(sent))).ReadJsonAsync();
        Assert.Equal("Paused", paused.GetProperty("status").GetString());
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{id}/resume", token, StampBody(paused))).ShouldFailAsync(403, Forbidden);
        Assert.Equal(CampaignStatus.Paused, (await fx.CampaignAsync(id)).Status);
        var cancelled = await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{Campaigns}/{id}/cancel", token, StampBody(paused))).ReadJsonAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        // Drafting still works while viewing as the account manager.
        var draft = await Impersonating.SendAsync(admin, HttpMethod.Post, Campaigns, token, new
        {
            clientAccountId = ws.ClientId, name = "Viewed-as draft", channel = "Email", listId = ws.ListId, senderProfileId = ws.SenderId,
            subject = "Hi", design = EmailFixture.Design(),
        });
        Assert.Equal("Draft", (await draft.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Viewing_as_an_account_manager_cannot_send_an_sms_campaign()
    {
        var ws = await fx.CreateWorkspaceAsync();
        await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Phone = "+14155550101"; s.SmsConsent = ConsentStatus.Granted; });
        await fx.AddIntegrationAsync(ws.ClientId, "twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "t" });
        var am = await fx.Api.CreateUserAsync(new[] { Role.AccountManager });
        var amClient = await fx.LoginAsync(am);
        var (admin, token) = await ViewAsAsync(am.Id);
        var sms = await fx.CreateCampaignAsync(amClient, ws, path: SmsCampaigns, channel: "Sms", extra: new { smsBody = "Sale tonight. Reply STOP to opt out" });
        var id = sms.GetProperty("id").GetGuid();
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"{SmsCampaigns}/{id}/send", token, ConfirmBody(sms))).ShouldFailAsync(403, Forbidden);
        Assert.Equal(CampaignStatus.Draft, (await fx.CampaignAsync(id)).Status);
        // Not a permission problem: the account manager can send it.
        Assert.Equal("Scheduled", (await EmailFixture.ConfirmSendAsync(amClient, sms, SmsCampaigns)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Viewing_as_an_account_manager_cannot_activate_a_journey_or_enroll_contacts()
    {
        var ws = await fx.CreateWorkspaceAsync();
        var subscriber = (await fx.AddSubscribersAsync(ws, 1))[0];
        var am = await fx.Api.CreateUserAsync(new[] { Role.AccountManager });
        var amClient = await fx.LoginAsync(am);
        var (admin, token) = await ViewAsAsync(am.Id);
        var template = (await (await amClient.PostAsJsonAsync("/api/v1/agency/email/templates",
            new { clientAccountId = ws.ClientId, name = "Welcome", subject = "Welcome", design = EmailFixture.Design() })).ReadJsonAsync()).GetProperty("id").GetGuid();
        var journey = await (await amClient.PostAsJsonAsync("/api/v1/agency/email/automations", new
        {
            clientAccountId = ws.ClientId, name = "Welcome", trigger = "ListSubscribed", triggerConfig = new { listId = ws.ListId },
            senderProfileId = ws.SenderId, reentry = "Never",
            steps = new object[] { new { key = "e1", type = "SendEmail", config = new { templateId = template } } },
        })).ReadJsonAsync();
        var id = journey.GetProperty("id").GetGuid();

        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/agency/email/automations/{id}/activate", token)).ShouldFailAsync(403, Forbidden);
        Assert.Equal(AutomationStatus.Draft, await fx.Db(db => db.Set<Automation>().Where(a => a.Id == id).Select(a => a.Status).FirstAsync()));
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/agency/email/automations/{id}/enroll", token, new { subscriberId = subscriber }))
            .ShouldFailAsync(403, Forbidden);
        Assert.False(await fx.Db(db => db.Set<AutomationEnrollment>().AnyAsync(e => e.AutomationId == id)));

        // The account manager activates it; viewed-as, pausing it (stopping messages) is allowed.
        Assert.Equal("Active", (await (await amClient.PostAsync($"/api/v1/agency/email/automations/{id}/activate", null)).ReadJsonAsync()).GetProperty("status").GetString());
        var paused = await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/agency/email/automations/{id}/pause", token)).ReadJsonAsync();
        Assert.Equal("Paused", paused.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Viewing_as_a_client_owner_cannot_approve_a_campaign_send()
    {
        var ws = await fx.CreateWorkspaceAsync(requireApproval: true);
        await fx.AddSubscribersAsync(ws, 2);
        var staff = await fx.StaffAsync();
        var (owner, ownerClient) = await fx.ClientUserAsync(ws.ClientId, ClientMemberRole.Owner);
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        var id = campaign.GetProperty("id").GetGuid();
        Assert.Equal("Pending", (await EmailFixture.ConfirmSendAsync(staff, campaign)).GetProperty("approvalStatus").GetString());

        var (admin, token) = await ViewAsAsync(owner.Id);
        await (await Impersonating.SendAsync(admin, HttpMethod.Post, $"/api/v1/client/email/campaigns/{id}/approval", token, new { approve = true }))
            .ShouldFailAsync(403, Forbidden);
        Assert.Equal(ApprovalStatus.Pending, (await fx.CampaignAsync(id)).ApprovalStatus);
        // The owner can still decide themselves.
        var approved = await (await ownerClient.PostAsJsonAsync($"/api/v1/client/email/campaigns/{id}/approval", new { approve = true })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("approvalStatus").GetString());
    }
}
