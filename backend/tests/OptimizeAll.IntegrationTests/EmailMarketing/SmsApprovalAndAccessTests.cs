using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Seed;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[Collection(EmailCollection.Name)]
public sealed class SmsApprovalAndAccessTests(EmailFixture fx)
{
    private const string SmsPath = "/api/v1/agency/email/sms/campaigns";

    [Fact]
    public async Task Sms_campaigns_need_consent_respect_quiet_hours_and_count_segments()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var hour = fx.Now.Hour;
        await fx.Db(async db =>
        {
            var settings = await db.Set<EmailWorkspaceSettings>().FirstAsync(s => s.ClientAccountId == ws.ClientId);
            // Quiet for recipients whose local hour is the current UTC hour.
            settings.QuietHoursStart = hour;
            settings.QuietHoursEnd = (hour + 1) % 24;
            await db.SaveChangesAsync();
        });
        var quiet = await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Phone = "+14155550001"; s.SmsConsent = ConsentStatus.Granted; s.TimeZone = "UTC"; });
        var awake = await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Phone = "+14155550002"; s.SmsConsent = ConsentStatus.Granted; s.TimeZone = "Etc/GMT-6"; });
        await fx.AddSubscribersAsync(ws, 1, (s, _) => { s.Phone = "+14155550003"; s.SmsConsent = ConsentStatus.Unknown; });

        // Content creators cannot manage SMS.
        var creator = await fx.StaffAsync(Role.ContentCreator);
        await (await creator.GetAsync($"{SmsPath}?clientAccountId={ws.ClientId}")).ShouldFailAsync(403);

        var campaign = await fx.CreateCampaignAsync(staff, ws, path: SmsPath, channel: "Sms",
            extra: new { smsBody = "Hi {{first_name|there}}, 2-for-1 pizzas tonight 🍕 Reply STOP to opt out", throttlePerMinute = 100 });
        var id = campaign.GetProperty("id").GetString();
        var checklist = await (await staff.GetAsync($"{SmsPath}/{id}/checklist")).ReadJsonAsync();
        Assert.False(checklist.GetProperty("canSend").GetBoolean()); // Twilio not connected
        Assert.Equal("Ucs2", checklist.GetProperty("smsEncoding").GetString());
        await fx.AddIntegrationAsync(ws.ClientId, "twilio", new() { ["accountSid"] = "AC1", ["fromNumber"] = "+15005550006" }, new() { ["authToken"] = "t" });
        checklist = await (await staff.GetAsync($"{SmsPath}/{id}/checklist")).ReadJsonAsync();
        Assert.True(checklist.GetProperty("canSend").GetBoolean());
        Assert.Equal(2, checklist.GetProperty("audienceCount").GetInt32());
        Assert.True(checklist.GetProperty("estimatedCost").GetDecimal() > 0);

        // Email campaign endpoints do not expose SMS campaigns.
        await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{id}")).ShouldFailAsync(404);

        await EmailFixture.ConfirmSendAsync(staff, campaign, SmsPath);
        await fx.RunJobAsync<CampaignSendJob>();
        var recipients = await fx.RecipientsAsync(Guid.Parse(id!));
        var sentNow = recipients.Single(r => r.SubscriberId == awake[0]);
        Assert.Equal(RecipientStatus.Sent, sentNow.Status);
        Assert.True(sentNow.Segments >= 1);
        Assert.True(sentNow.Cost > 0);
        var held = recipients.Single(r => r.SubscriberId == quiet[0]);
        Assert.Equal(RecipientStatus.Pending, held.Status);
        Assert.True(held.DueAt > fx.Now);
        Assert.DoesNotContain(fx.Sms.Sent, m => m.To == "+14155550003");

        fx.Api.Clock.Advance(held.DueAt!.Value - fx.Now + TimeSpan.FromMinutes(1));
        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Contains(fx.Sms.Sent, m => m.To == "+14155550001");
        Assert.Equal(CampaignStatus.Sent, (await fx.CampaignAsync(Guid.Parse(id!))).Status);

        var segments = await (await staff.PostAsJsonAsync($"{SmsPath}/segments", new { text = new string('a', 161), recipients = 1000, costPerSegment = 0.01m })).ReadJsonAsync();
        Assert.Equal(2, segments.GetProperty("segments").GetInt32());
        Assert.Equal(20m, segments.GetProperty("estimatedCost").GetDecimal());
    }

    [Fact]
    public async Task Client_approval_gates_the_send_job_and_only_approvers_can_decide()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync(requireApproval: true);
        await fx.AddSubscribersAsync(ws, 2);
        var campaign = await fx.CreateCampaignAsync(staff, ws, name: "Flash sale");
        var id = Guid.Parse(campaign.GetProperty("id").GetString()!);
        var confirmed = await EmailFixture.ConfirmSendAsync(staff, campaign);
        Assert.Equal("Pending", confirmed.GetProperty("approvalStatus").GetString());

        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(CampaignStatus.Scheduled, (await fx.CampaignAsync(id)).Status);
        Assert.Empty(await fx.RecipientsAsync(id));

        var (_, viewer) = await fx.ClientUserAsync(ws.ClientId, ClientMemberRole.Viewer);
        var (_, approver) = await fx.ClientUserAsync(ws.ClientId, ClientMemberRole.Approver);
        var other = await fx.CreateWorkspaceAsync();
        var (_, stranger) = await fx.ClientUserAsync(other.ClientId, ClientMemberRole.Owner);

        var approvals = await (await approver.GetAsync("/api/v1/client/email/approvals")).ReadJsonAsync();
        var item = Assert.Single(approvals.EnumerateArray());
        Assert.True(item.GetProperty("canApprove").GetBoolean());
        Assert.False((await (await viewer.GetAsync("/api/v1/client/email/approvals")).ReadJsonAsync())[0].GetProperty("canApprove").GetBoolean());
        var preview = await (await viewer.GetAsync($"/api/v1/client/email/campaigns/{id}/preview")).ReadJsonAsync();
        Assert.Contains("Hello Alex", preview.GetProperty("html").GetString());

        await (await viewer.PostAsJsonAsync($"/api/v1/client/email/campaigns/{id}/approval", new { approve = true })).ShouldFailAsync(403, "client.insufficient_role");
        await (await stranger.PostAsJsonAsync($"/api/v1/client/email/campaigns/{id}/approval", new { approve = true })).ShouldFailAsync(404);
        await (await stranger.GetAsync($"/api/v1/client/email/campaigns/{id}/report")).ShouldFailAsync(404);
        await (await approver.PostAsJsonAsync($"/api/v1/client/email/campaigns/{id}/approval", new { approve = false })).ShouldFailAsync(400, "email.approval_note_required");
        var approved = await (await approver.PostAsJsonAsync($"/api/v1/client/email/campaigns/{id}/approval", new { approve = true, note = "Looks great" })).ReadJsonAsync();
        Assert.Equal("Approved", approved.GetProperty("approvalStatus").GetString());

        await fx.RunJobAsync<CampaignSendJob>();
        Assert.Equal(CampaignStatus.Sent, (await fx.CampaignAsync(id)).Status);
        Assert.Equal(2, (await fx.RecipientsAsync(id)).Count(r => r.Status == RecipientStatus.Sent));

        var list = await (await viewer.GetAsync("/api/v1/client/email/campaigns")).ReadJsonAsync();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id && c.GetProperty("sent").GetInt32() == 2);
        var report = await (await viewer.GetAsync($"/api/v1/client/email/campaigns/{id}/report")).ReadJsonAsync();
        Assert.Equal(2, report.GetProperty("sent").GetInt32());
        var orgs = await (await viewer.GetAsync("/api/v1/client/email/clients")).ReadJsonAsync();
        Assert.Equal(ws.ClientId, Assert.Single(orgs.EnumerateArray()).GetProperty("id").GetGuid());

        // Rejection sends it back to draft.
        var second = await fx.CreateCampaignAsync(staff, ws, name: "Second");
        var secondId = second.GetProperty("id").GetString();
        await EmailFixture.ConfirmSendAsync(staff, second);
        var rejected = await (await approver.PostAsJsonAsync($"/api/v1/client/email/campaigns/{secondId}/approval", new { approve = false, note = "Wrong price" })).ReadJsonAsync();
        Assert.Equal("Draft", rejected.GetProperty("status").GetString());
        Assert.Equal("Rejected", rejected.GetProperty("approvalStatus").GetString());
    }

    [Fact]
    public async Task Permission_matrix_and_tenant_isolation()
    {
        var ws = await fx.CreateWorkspaceAsync();
        var campaign = await fx.CreateCampaignAsync(await fx.StaffAsync(), ws);
        var id = campaign.GetProperty("id").GetString();
        var participant = await fx.LoginAsync(await fx.Api.CreateUserAsync());
        var (_, clientUser) = await fx.ClientUserAsync(ws.ClientId, ClientMemberRole.Owner);
        var creator = await fx.StaffAsync(Role.ContentCreator);
        var manager = await fx.StaffAsync(Role.AccountManager);
        var finance = await fx.StaffAsync(Role.Finance);
        var admin = await fx.StaffAsync();
        var anonymous = fx.Anonymous();

        var staffOnly = new[]
        {
            "/api/v1/agency/email/lists", "/api/v1/agency/email/subscribers", "/api/v1/agency/email/segments", "/api/v1/agency/email/templates",
            "/api/v1/agency/email/campaigns", "/api/v1/agency/email/automations", "/api/v1/agency/email/settings", "/api/v1/agency/email/suppressions",
        };
        foreach (var path in staffOnly)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await participant.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clientUser.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(path)).StatusCode);
        }
        // Content creators author but cannot send or run SMS; account managers can send and run SMS; choosing the provider
        // (integrations.manage) stays admin-only.
        await (await creator.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/pause", new { })).ShouldFailAsync(403);
        await (await creator.GetAsync("/api/v1/agency/email/sms/campaigns")).ShouldFailAsync(403);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/v1/agency/email/campaigns/{id}/pause", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/v1/agency/email/sms/campaigns")).StatusCode);
        foreach (var client in new[] { creator, manager })
            await (await client.PutAsJsonAsync("/api/v1/agency/email/settings/provider", new { emailProvider = "sendgrid", confirm = true })).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/agency/email/sms/campaigns")).StatusCode);
        var provider = await (await admin.PutAsJsonAsync("/api/v1/agency/email/settings/provider", new { clientAccountId = ws.ClientId, emailProvider = "sendgrid", confirm = true })).ReadJsonAsync();
        Assert.Contains(provider.GetProperty("providers").EnumerateArray(), p => p.GetProperty("channel").GetString() == "Email" && !p.GetProperty("ready").GetBoolean());

        // Client portal: staff have no client.portal; members only see their own organizations; drafts are hidden.
        await (await admin.GetAsync("/api/v1/client/email/campaigns")).ShouldFailAsync(403);
        var visible = await (await clientUser.GetAsync("/api/v1/client/email/campaigns")).ReadJsonAsync();
        Assert.DoesNotContain(visible.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetString() == id);
        await (await clientUser.GetAsync($"/api/v1/client/email/campaigns/{id}/report")).ShouldFailAsync(404);
        var other = await fx.CreateWorkspaceAsync();
        await (await clientUser.GetAsync($"/api/v1/client/email/campaigns?clientId={other.ClientId}")).ShouldFailAsync(404);
        await (await clientUser.GetAsync($"/api/v1/client/email/kpis?clientId={other.ClientId}")).ShouldFailAsync(404);
        Assert.Equal(HttpStatusCode.OK, (await clientUser.GetAsync($"/api/v1/client/email/kpis?clientId={ws.ClientId}")).StatusCode);

        // Staff endpoints validate the workspace.
        await (await admin.GetAsync($"/api/v1/agency/email/lists?clientId={Guid.NewGuid()}")).ShouldFailAsync(404);
        // Cross-workspace references are rejected.
        await (await admin.PostAsJsonAsync("/api/v1/agency/email/campaigns", new
        {
            clientAccountId = other.ClientId, name = "x", channel = "Email", listId = ws.ListId, senderProfileId = ws.SenderId, subject = "s", design = EmailFixture.Design(),
        })).ShouldFailAsync(400, "email.campaign_invalid");
    }

    [Fact]
    public async Task WhatsApp_campaigns_with_the_largest_allowed_parameters_are_stored_intact()
    {
        // Validation allows 10 parameters of up to 1024 characters; the columns must hold that on every provider
        // (MySQL rejected it as "Data too long" when the JSON column was varchar(4000) and the summary varchar(1600)).
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var parameters = Enumerable.Range(0, 10).Select(i => $"P{i}-" + new string((char)('a' + i), 1000)).ToList();
        var campaign = await fx.CreateCampaignAsync(staff, ws, path: SmsPath, channel: "WhatsApp",
            extra: new { whatsAppTemplateName = "spring_offer", whatsAppTemplateLanguage = "en", whatsAppParameters = parameters });
        var reloaded = await (await staff.GetAsync($"{SmsPath}/{campaign.GetProperty("id").GetString()}")).ReadJsonAsync();
        Assert.Equal(parameters, reloaded.GetProperty("whatsAppParameters").EnumerateArray().Select(p => p.GetString()!).ToList());
        Assert.True(reloaded.GetProperty("smsBody").GetString()!.Length <= 1600);
    }

    [Fact]
    public async Task Long_json_columns_are_unbounded_text_on_every_provider()
    {
        // Convention: long text is unbounded (varchar(N >= 4000) risks "Data too long" and MySQL's row-size limit).
        await fx.Db(db =>
        {
            var model = db.Model;
            foreach (var (entity, property) in new[]
            {
                (typeof(EmailCampaign), nameof(EmailCampaign.WhatsAppParametersJson)), (typeof(EmailCampaign), nameof(EmailCampaign.DesignJson)),
                (typeof(SubscriberImport), nameof(SubscriberImport.MappingJson)), (typeof(SubscriberImport), nameof(SubscriberImport.ErrorsJson)),
                (typeof(AutomationStep), nameof(AutomationStep.ConfigJson)), (typeof(AutomationEnrollment), nameof(AutomationEnrollment.TriggerDataJson)),
                (typeof(EmailTemplate), nameof(EmailTemplate.DesignJson)), (typeof(CampaignVariant), nameof(CampaignVariant.DesignJson)),
                (typeof(Segment), nameof(Segment.DefinitionJson)),
            })
                Assert.True(model.FindEntityType(entity)!.FindProperty(property)!.GetMaxLength() is null, $"{entity.Name}.{property} must be unbounded");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Demo_seed_creates_the_canonical_clients_campaigns_in_every_status_and_is_idempotent()
    {
        async Task RunSeed()
        {
            using var scope = fx.App.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetServices<OptimizeAll.Api.Common.Persistence.ISeeder>().OfType<EmailDemoSeeder>().Single();
            await seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), default);
        }

        await RunSeed();
        var counts = await fx.Db(async db => new
        {
            Campaigns = await db.Set<EmailCampaign>().CountAsync(),
            Subscribers = await db.Set<Subscriber>().CountAsync(),
            Users = await db.Set<User>().CountAsync(),
        });
        await RunSeed();
        Assert.Equal(counts, await fx.Db(async db => new
        {
            Campaigns = await db.Set<EmailCampaign>().CountAsync(),
            Subscribers = await db.Set<Subscriber>().CountAsync(),
            Users = await db.Set<User>().CountAsync(),
        }));

        var clients = await fx.Db(db => db.Set<ClientAccount>().AsNoTracking().Where(c => EmailDemoSeeder.Clients.Select(x => x.Slug).Contains(c.Slug)).ToListAsync());
        Assert.Equal(4, clients.Count);
        var aurora = clients.Single(c => c.Slug == "aurora-skincare");
        Assert.Equal(("Aurora Skincare", "AE", "AED"), (aurora.Name, aurora.CountryCode, aurora.Currency));
        var ids = clients.Select(c => (Guid?)c.Id).ToList();
        var statuses = await fx.Db(db => db.Set<EmailCampaign>().AsNoTracking().Where(c => ids.Contains(c.ClientAccountId)).Select(c => c.Status).Distinct().ToListAsync());
        Assert.Equal(Enum.GetValues<CampaignStatus>().OrderBy(s => s), statuses.OrderBy(s => s));
        Assert.True(await fx.Db(db => db.Set<Subscriber>().CountAsync(s => ids.Contains(s.ClientAccountId))) >= 800);
        Assert.True(await fx.Db(db => db.Set<Automation>().AnyAsync(a => ids.Contains(a.ClientAccountId) && a.Status == AutomationStatus.Active)));
        Assert.True(await fx.Db(db => db.Set<User>().AnyAsync(u => u.NormalizedEmail == "CONTENT@DEMO.OPTIMIZEALL.APP")));
        Assert.True(await fx.Db(db => db.Set<User>().AnyAsync(u => u.NormalizedEmail == "AM@DEMO.OPTIMIZEALL.APP")));

        // Whichever demo seeder runs first, the shared demo people and clients carry the canonical values.
        foreach (var canonical in new[] { OptimizeAll.Api.Modules.Clients.DeliveryDemoData.AccountManager, OptimizeAll.Api.Modules.Clients.DeliveryDemoData.Content })
        {
            var normalized = canonical.Email.ToUpperInvariant();
            var user = await fx.Db(db => db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstAsync(u => u.NormalizedEmail == normalized));
            Assert.Equal(canonical.DisplayName, user.DisplayName);
            Assert.Contains(user.Roles, r => r.Role == canonical.Role);
        }
        foreach (var canonical in OptimizeAll.Api.Modules.Clients.DeliveryDemoData.Clients)
        {
            var client = clients.Single(c => c.Slug == canonical.Slug);
            Assert.Equal((canonical.Name, canonical.Industry, canonical.CountryCode, canonical.Currency, canonical.TimeZone, canonical.Status, canonical.Website),
                (client.Name, client.Industry, client.CountryCode, client.Currency, client.TimeZone, client.Status, client.Website));
        }
        var am = await fx.Db(db => db.Set<User>().AsNoTracking().FirstAsync(u => u.NormalizedEmail == "AM@DEMO.OPTIMIZEALL.APP"));
        Assert.All(clients, c => Assert.Equal(am.Id, c.AccountManagerUserId));

        // Sent demo campaigns have report data.
        var staff = await fx.StaffAsync();
        var sent = await fx.Db(db => db.Set<EmailCampaign>().AsNoTracking().FirstAsync(c => c.ClientAccountId == aurora.Id && c.Status == CampaignStatus.Sent));
        var report = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{sent.Id}/report")).ReadJsonAsync();
        Assert.True(report.GetProperty("uniqueOpens").GetInt32() > 0);
        Assert.NotEmpty(report.GetProperty("revenue").EnumerateArray());
    }
}
