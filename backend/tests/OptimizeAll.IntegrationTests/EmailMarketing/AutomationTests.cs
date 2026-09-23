using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Events;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[Collection(EmailCollection.Name)]
public sealed partial class AutomationTests(EmailFixture fx)
{
    [GeneratedRegex("http://app\\.test(/e/o/[A-Za-z0-9_.\\-]+\\.gif)")]
    private static partial Regex OpenPixel();

    private async Task<Guid> TemplateAsync(HttpClient staff, EmailFixture.Workspace ws, string subject)
    {
        var t = await (await staff.PostAsJsonAsync("/api/v1/agency/email/templates", new { clientAccountId = ws.ClientId, name = subject, subject, design = EmailFixture.Design() })).ReadJsonAsync();
        return t.GetProperty("id").GetGuid();
    }

    private Task<List<AutomationEnrollment>> Enrollments(Guid automationId) =>
        fx.Db(db => db.Set<AutomationEnrollment>().AsNoTracking().Where(e => e.AutomationId == automationId).ToListAsync());

    [Fact]
    public async Task Welcome_journey_enrolls_sends_waits_branches_and_is_idempotent()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var welcome = await TemplateAsync(staff, ws, "Welcome!");
        var nudge = await TemplateAsync(staff, ws, "Did you see this?");
        var created = await (await staff.PostAsJsonAsync("/api/v1/agency/email/automations", new
        {
            clientAccountId = ws.ClientId, name = "Welcome", trigger = "ListSubscribed", triggerConfig = new { listId = ws.ListId },
            senderProfileId = ws.SenderId, reentry = "Never",
            steps = new object[]
            {
                new { key = "e1", type = "SendEmail", config = new { templateId = welcome }, next = "w1" },
                new { key = "w1", type = "Wait", config = new { days = 1 }, next = "c1" },
                new { key = "c1", type = "Condition", config = new { check = "opened", stepKey = "e1" }, next = "t1", altNext = "e2" },
                new { key = "t1", type = "AddTag", config = new { tag = "engaged" } },
                new { key = "e2", type = "SendEmail", config = new { templateId = nudge } },
            },
        })).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal("Active", (await (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/activate", null)).ReadJsonAsync()).GetProperty("status").GetString());

        async Task<Guid> Subscribe(string email, bool consent)
        {
            var s = await (await staff.PostAsJsonAsync("/api/v1/agency/email/subscribers", new
            {
                clientAccountId = ws.ClientId, email, attestEmailConsent = consent, consentSource = consent ? "Checkout" : null, listIds = new[] { ws.ListId },
            })).ReadJsonAsync();
            return s.GetProperty("id").GetGuid();
        }
        var opener = await Subscribe("opener@example.com", true);
        var ignorer = await Subscribe("ignorer@example.com", true);
        var noConsent = await Subscribe("noconsent@example.com", false);
        Assert.Equal(3, (await Enrollments(id)).Count);

        await fx.RunJobAsync<AutomationJob>();
        await fx.RunJobAsync<AutomationJob>();
        var sent = fx.Email.For(ws.ClientId).Where(e => e.Subject == "Welcome!").ToList();
        Assert.Equal(new[] { "ignorer@example.com", "opener@example.com" }, sent.Select(e => e.To).OrderBy(x => x).ToArray());
        var skipped = await fx.Db(db => db.Set<AutomationStepRun>().AsNoTracking().FirstAsync(r => r.AutomationId == id && r.SubscriberId == noConsent && r.StepKey == "e1"));
        Assert.Equal(StepRunStatus.Skipped, skipped.Status);
        Assert.Contains("consent", skipped.Detail);

        // The opener opens (human user agent).
        var pixel = OpenPixel().Match(sent.Single(e => e.To == "opener@example.com").Html).Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Get, pixel);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148");
        await fx.Anonymous().SendAsync(request);

        await fx.RunJobAsync<AutomationJob>(); // still waiting
        Assert.DoesNotContain(fx.Email.For(ws.ClientId), e => e.Subject == "Did you see this?");
        fx.Api.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        await fx.RunJobAsync<AutomationJob>();
        await fx.RunJobAsync<AutomationJob>();

        var nudges = fx.Email.For(ws.ClientId).Where(e => e.Subject == "Did you see this?").Select(e => e.To).ToList();
        Assert.Equal(new[] { "ignorer@example.com" }, nudges);
        Assert.True(await fx.Db(db => db.Set<SubscriberTag>().AnyAsync(t => t.SubscriberId == opener && t.Tag == "engaged")));
        Assert.False(await fx.Db(db => db.Set<SubscriberTag>().AnyAsync(t => t.SubscriberId == ignorer && t.Tag == "engaged")));
        var enrollments = await Enrollments(id);
        Assert.All(enrollments, e => Assert.Equal(EnrollmentStatus.Completed, e.Status));

        // Re-entry policy "Never": re-subscribing does not enroll again.
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/subscribers/{opener}/lists", new { listId = ws.ListId, action = "unsubscribe" })).ReadJsonAsync();
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/subscribers/{opener}/lists", new { listId = ws.ListId, action = "subscribe" })).ReadJsonAsync();
        Assert.Equal(3, (await Enrollments(id)).Count);

        var detail = await (await staff.GetAsync($"/api/v1/agency/email/automations/{id}")).ReadJsonAsync();
        var e1 = detail.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "e1").GetProperty("stats");
        Assert.Equal(2, e1.GetProperty("sent").GetInt32());
        Assert.Equal(1, e1.GetProperty("opened").GetInt32());
    }

    [Fact]
    public async Task Event_triggered_journey_exits_when_the_goal_is_met_and_cannot_be_activated_when_invalid()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var template = await TemplateAsync(staff, ws, "Your cart misses you");
        var ids = await fx.AddSubscribersAsync(ws, 2);
        var emails = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().Where(s => ids.Contains(s.Id)).OrderBy(s => s.Id).Select(s => s.NormalizedEmail!).ToListAsync());

        await (await staff.PostAsJsonAsync("/api/v1/agency/email/automations", new
        {
            clientAccountId = ws.ClientId, name = "Loop", trigger = "CustomEvent", triggerConfig = new { eventName = "cart_abandoned" }, senderProfileId = ws.SenderId,
            steps = new object[]
            {
                new { key = "a", type = "Wait", config = new { hours = 1 }, next = "b" },
                new { key = "b", type = "Condition", config = new { check = "opened" }, next = "a", altNext = "a" },
            },
        })).ShouldFailAsync(400, "email.automation_invalid");

        var created = await (await staff.PostAsJsonAsync("/api/v1/agency/email/automations", new
        {
            clientAccountId = ws.ClientId, name = "Cart", trigger = "CustomEvent", triggerConfig = new { eventName = "cart_abandoned" }, senderProfileId = ws.SenderId,
            reentry = "AfterExit", goal = new { kind = "purchased" },
            steps = new object[]
            {
                new { key = "w", type = "Wait", config = new { hours = 1 }, next = "mail" },
                new { key = "mail", type = "SendEmail", config = new { templateId = template } },
            },
        })).ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        await (await staff.PostAsync($"/api/v1/agency/email/automations/{id}/activate", null)).ReadJsonAsync();

        foreach (var email in emails)
        {
            var result = await (await staff.PostAsJsonAsync("/api/v1/agency/email/events", new
            {
                clientAccountId = ws.ClientId, email, name = "cart_abandoned", eventId = "cart-" + email, properties = new { item_name = "Serum" },
            })).ReadJsonAsync();
            Assert.Equal(1, result.GetProperty("enrolled").GetInt32());
        }
        // Duplicate event id: ignored.
        var dup = await (await staff.PostAsJsonAsync("/api/v1/agency/email/events", new { clientAccountId = ws.ClientId, email = emails[0], name = "cart_abandoned", eventId = "cart-" + emails[0] })).ReadJsonAsync();
        Assert.True(dup.GetProperty("duplicate").GetBoolean());
        await fx.RunJobAsync<AutomationJob>();

        // The first contact buys before the wait ends → exits without the email.
        await fx.Db(async db =>
        {
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids.OrderBy(x => x).First(), Type = EngagementType.Conversion, OccurredAt = fx.Now, Value = 20, Currency = "USD", DedupKey = "t-" + Guid.NewGuid() });
            await db.SaveChangesAsync();
        });
        fx.Api.Clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(1)));
        await fx.RunJobAsync<AutomationJob>();
        var sent = fx.Email.For(ws.ClientId).Where(e => e.Subject == "Your cart misses you").Select(e => e.To).ToList();
        Assert.Equal(new[] { emails[1] }, sent);
        var enrollments = await Enrollments(id);
        Assert.Contains(enrollments, e => e.Status == EnrollmentStatus.Exited && e.ExitReason == "Goal reached.");
        Assert.Contains(enrollments, e => e.Status == EnrollmentStatus.Completed);
    }

    [Fact]
    public async Task Website_newsletter_and_form_events_create_contacts_with_consent_only_when_given()
    {
        using var scope = fx.App.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var ws = await fx.CreateWorkspaceAsync();
        await publisher.PublishAsync(new NewsletterSubscribed(Guid.NewGuid(), "Reader@Example.com", fx.Now));
        var reader = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == Workspace.AgencyKey && s.NormalizedEmail == "reader@example.com"));
        Assert.Equal(ConsentStatus.Granted, reader.EmailConsent);
        Assert.True(await fx.Db(db => db.Set<ListMembership>().AnyAsync(m => m.SubscriberId == reader.Id && m.Status == MembershipStatus.Subscribed)));

        await publisher.PublishAsync(new FormSubmitted(Guid.NewGuid(), Guid.NewGuid(), ws.ClientId, "lead@example.com", "Lee Ada", null,
            new Dictionary<string, string> { ["company"] = "Acme" }, null, null, null, fx.Now));
        await publisher.PublishAsync(new FormSubmitted(Guid.NewGuid(), Guid.NewGuid(), ws.ClientId, "optin@example.com", "Opt In", null,
            new Dictionary<string, string> { ["newsletter"] = "yes" }, null, null, null, fx.Now));
        var key = Workspace.Key(ws.ClientId);
        var lead = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == key && s.NormalizedEmail == "lead@example.com"));
        var optIn = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == key && s.NormalizedEmail == "optin@example.com"));
        Assert.Equal(ConsentStatus.Unknown, lead.EmailConsent);
        Assert.Equal("Lee", lead.FirstName);
        Assert.Equal(ConsentStatus.Granted, optIn.EmailConsent);
    }
}
