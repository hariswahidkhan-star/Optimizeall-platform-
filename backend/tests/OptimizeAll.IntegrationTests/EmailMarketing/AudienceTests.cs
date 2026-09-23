using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.EmailMarketing;

[Collection(EmailCollection.Name)]
public sealed class AudienceTests(EmailFixture fx)
{
    private static string? LinkFrom(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.IndexOfAny(new[] { '"', '\n', ' ', '\r', '<' }, start);
        return System.Net.WebUtility.HtmlDecode(text[start..(end < 0 ? text.Length : end)]);
    }

    [Fact]
    public async Task Double_opt_in_subscribers_get_nothing_but_the_confirmation_until_they_confirm()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync(doubleOptIn: true);
        var list = await fx.Db(db => db.Set<EmailList>().AsNoTracking().FirstAsync(l => l.Id == ws.ListId));
        var anonymous = fx.Anonymous();

        var form = await (await anonymous.GetAsync($"/api/v1/public/email/forms/{list.PublicKey}")).ReadJsonAsync();
        Assert.True(form.GetProperty("doubleOptIn").GetBoolean());
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/email/forms/{list.PublicKey}", new { email = "new.reader@example.com", firstName = "Rae", consent = false }))
            .ShouldFailAsync(400, "email.consent_required");
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsJsonAsync($"/api/v1/public/email/forms/{list.PublicKey}",
            new { email = "New.Reader@Example.com", firstName = "Rae", consent = true })).StatusCode);

        var s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.ScopeKey == Workspace.Key(ws.ClientId) && x.NormalizedEmail == "new.reader@example.com"));
        Assert.Equal(ConsentStatus.Pending, s.EmailConsent);
        var membership = await fx.Db(db => db.Set<ListMembership>().AsNoTracking().FirstAsync(m => m.SubscriberId == s.Id));
        Assert.Equal(MembershipStatus.Pending, membership.Status);
        var consentRecord = await fx.Db(db => db.Set<ConsentRecord>().AsNoTracking().FirstAsync(c => c.SubscriberId == s.Id));
        Assert.Equal("form", consentRecord.Source);
        Assert.NotNull(consentRecord.ConsentTextVersion);
        var confirmation = fx.Email.For(ws.ClientId).Last(e => e.To == "new.reader@example.com");
        Assert.Contains("Confirm your subscription", confirmation.Subject);

        // A campaign to the list now excludes the pending contact.
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        var checklist = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{campaign.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.Equal(0, checklist.GetProperty("audienceCount").GetInt32());

        // Forged / tampered tokens are rejected; the real one confirms.
        var link = LinkFrom(confirmation.Text, "http://app.test/email/confirm/")!;
        var token = link[(link.LastIndexOf('/') + 1)..];
        await (await anonymous.PostAsync($"/api/v1/public/email/confirm/{token}x", null)).ShouldFailAsync(400, "email.confirmation_invalid");
        var confirmed = await (await anonymous.PostAsync($"/api/v1/public/email/confirm/{token}", null)).ReadJsonAsync();
        Assert.Equal("Newsletter", confirmed.GetProperty("list").GetString());
        s = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(x => x.Id == s.Id));
        Assert.Equal(ConsentStatus.Granted, s.EmailConsent);
        checklist = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{campaign.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.Equal(1, checklist.GetProperty("audienceCount").GetInt32());

        // Expired links fail.
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/email/forms/{list.PublicKey}", new { email = "late@example.com", consent = true })).ReadJsonAsync();
        var late = fx.Email.For(ws.ClientId).Last(e => e.To == "late@example.com");
        var lateToken = LinkFrom(late.Text, "http://app.test/email/confirm/")!.Split('/').Last();
        fx.Api.Clock.Advance(TimeSpan.FromDays(8));
        await (await anonymous.PostAsync($"/api/v1/public/email/confirm/{lateToken}", null)).ShouldFailAsync(400);
    }

    [Fact]
    public async Task Csv_import_requires_attestation_and_reports_per_row_errors_duplicates_and_suppressions()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var key = Workspace.Key(ws.ClientId);
        await fx.Db(async db =>
        {
            db.Add(new Suppression { ClientAccountId = ws.ClientId, ScopeKey = key, Channel = MessageChannel.Email, Value = "bounced@example.com", Reason = SuppressionReason.HardBounce, Source = "test", CreatedAt = fx.Now });
            await db.SaveChangesAsync();
        });
        var previouslyUnsubscribed = await fx.AddSubscribersAsync(ws, 1, (s, _) =>
        {
            s.Email = s.NormalizedEmail = "gone@example.com";
            s.Status = SubscriberStatus.Unsubscribed;
            s.EmailConsent = ConsentStatus.Withdrawn;
        });
        const string csv = "Email,First Name,Country,Tags,Plan\n" +
                           "ann@example.com,Ann,us,vip;beta,pro\n" +
                           "ANN@example.com,Ann again,US,,\n" +
                           "not-an-email,Bob,US,,\n" +
                           "bounced@example.com,Cy,US,,\n" +
                           "gone@example.com,Di,US,,\n" +
                           "eve@example.com,Eve,Narnia,,basic\n";
        var mapping = new Dictionary<string, string> { ["Email"] = "email", ["First Name"] = "first_name", ["Country"] = "country", ["Tags"] = "tags", ["Plan"] = "custom.plan" };

        var preview = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/lists/{ws.ListId}/imports/preview", new { csv })).ReadJsonAsync();
        Assert.Equal(6, preview.GetProperty("totalRows").GetInt32());
        Assert.Equal("email", preview.GetProperty("suggestedMapping").GetProperty("Email").GetString());

        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/lists/{ws.ListId}/imports",
            new { fileName = "contacts.csv", csv, mapping, confirmConsent = false, consentSource = "Checkout" })).ShouldFailAsync(400, "email.import_consent_required");
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/lists/{ws.ListId}/imports",
            new { fileName = "contacts.csv", csv, mapping = new Dictionary<string, string> { ["First Name"] = "first_name" }, confirmConsent = true, consentSource = "Checkout" }))
            .ShouldFailAsync(400, "email.import_mapping_invalid");

        var import = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/lists/{ws.ListId}/imports",
            new { fileName = "contacts.csv", csv, mapping, tags = new[] { "imported" }, confirmConsent = true, consentSource = "Checkout opt-in 2026" })).ReadJsonAsync();
        Assert.Equal("Completed", import.GetProperty("status").GetString());
        Assert.Equal(2, import.GetProperty("created").GetInt32());
        Assert.Equal(1, import.GetProperty("failed").GetInt32());
        Assert.Equal(3, import.GetProperty("skipped").GetInt32());
        var errors = import.GetProperty("errors").EnumerateArray().Select(e => (e.GetProperty("row").GetInt32(), e.GetProperty("message").GetString()!)).ToList();
        Assert.Contains(errors, e => e.Item1 == 3 && e.Item2.Contains("Duplicate"));
        Assert.Contains(errors, e => e.Item1 == 4 && e.Item2.Contains("Invalid email"));
        Assert.Contains(errors, e => e.Item1 == 5 && e.Item2.Contains("suppression list"));
        Assert.Contains(errors, e => e.Item1 == 6 && e.Item2.Contains("previously unsubscribed"));
        Assert.Contains(errors, e => e.Item1 == 7 && e.Item2.Contains("country"));

        var ann = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == key && s.NormalizedEmail == "ann@example.com"));
        Assert.Equal(ConsentStatus.Granted, ann.EmailConsent);
        Assert.Equal("US", ann.CountryCode);
        var detail = await (await staff.GetAsync($"/api/v1/agency/email/subscribers/{ann.Id}")).ReadJsonAsync();
        Assert.Equal(new[] { "beta", "imported", "vip" }, detail.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToArray());
        Assert.Equal("pro", detail.GetProperty("customFields").GetProperty("plan").GetString());
        Assert.Equal("import", detail.GetProperty("consentHistory")[0].GetProperty("source").GetString());
        var gone = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.Id == previouslyUnsubscribed[0]));
        Assert.Equal(SubscriberStatus.Unsubscribed, gone.Status);
        Assert.False(await fx.Db(db => db.Set<Subscriber>().AnyAsync(s => s.ScopeKey == key && s.NormalizedEmail == "bounced@example.com")));

        // Export.
        var export = await staff.GetAsync($"/api/v1/agency/email/lists/{ws.ListId}/export.csv");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var text = await export.Content.ReadAsStringAsync();
        Assert.Contains("ann@example.com", text);
        Assert.Contains("custom.plan", text);
    }

    [Fact]
    public async Task Csv_import_never_grants_sms_consent_to_a_number_that_texted_stop()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var key = Workspace.Key(ws.ClientId);
        await fx.Db(async db =>
        {
            db.Add(new Suppression { ClientAccountId = ws.ClientId, ScopeKey = key, Channel = MessageChannel.Sms, Value = "+923001234567", Reason = SuppressionReason.StopKeyword, Source = "sms-stop", CreatedAt = fx.Now });
            await db.SaveChangesAsync();
        });
        const string csv = "Email,Phone\nstopped@example.com,+923001234567\nfine@example.com,+923007654321\n";
        var import = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/lists/{ws.ListId}/imports", new
        {
            fileName = "sms.csv", csv, mapping = new Dictionary<string, string> { ["Email"] = "email", ["Phone"] = "phone" },
            confirmConsent = true, consentSource = "Store opt-in (email and SMS)", grantSmsConsent = true,
        })).ReadJsonAsync();
        Assert.Equal(2, import.GetProperty("created").GetInt32());
        Assert.Contains(import.GetProperty("errors").EnumerateArray(), e => e.GetProperty("row").GetInt32() == 2 && e.GetProperty("message").GetString()!.Contains("opted out of SMS"));

        var stopped = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == key && s.NormalizedEmail == "stopped@example.com"));
        Assert.NotEqual(ConsentStatus.Granted, stopped.SmsConsent);
        Assert.Equal(ConsentStatus.Granted, stopped.EmailConsent);
        Assert.False(await fx.Db(db => db.Set<ConsentRecord>().AnyAsync(c => c.SubscriberId == stopped.Id && c.Channel == MessageChannel.Sms && c.Status == ConsentStatus.Granted)));
        var fine = await fx.Db(db => db.Set<Subscriber>().AsNoTracking().FirstAsync(s => s.ScopeKey == key && s.NormalizedEmail == "fine@example.com"));
        Assert.Equal(ConsentStatus.Granted, fine.SmsConsent);
    }

    [Fact]
    public async Task Segment_rules_are_evaluated_server_side()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var ids = await fx.AddSubscribersAsync(ws, 6, (s, i) =>
        {
            s.CountryCode = i < 3 ? "US" : "GB";
            s.FirstName = i == 0 ? "Zed" : "Sub" + i;
        });
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        var campaignId = Guid.Parse(campaign.GetProperty("id").GetString()!);
        await fx.Db(async db =>
        {
            db.Add(new SubscriberTag { SubscriberId = ids[0], Tag = "vip", AddedAt = fx.Now });
            db.Add(new SubscriberTag { SubscriberId = ids[3], Tag = "vip", AddedAt = fx.Now });
            db.Add(new SubscriberField { SubscriberId = ids[1], Key = "plan", Value = "pro" });
            db.Add(new SubscriberField { SubscriberId = ids[4], Key = "plan", Value = "free" });
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids[0], CampaignId = campaignId, Type = EngagementType.Open, OccurredAt = fx.Now.AddDays(-2) });
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids[1], Type = EngagementType.Open, OccurredAt = fx.Now.AddDays(-45) });
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids[2], Type = EngagementType.Open, OccurredAt = fx.Now.AddDays(-1), IsMachine = true });
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids[3], CampaignId = campaignId, Type = EngagementType.Click, OccurredAt = fx.Now.AddDays(-3) });
            db.Add(new EngagementEvent { ClientAccountId = ws.ClientId, SubscriberId = ids[5], Type = EngagementType.Conversion, OccurredAt = fx.Now.AddDays(-10), Value = 10, Currency = "USD" });
            await db.SaveChangesAsync();
        });

        async Task<int> Count(object definition)
        {
            var result = await (await staff.PostAsJsonAsync("/api/v1/agency/email/segments/preview", new { clientAccountId = ws.ClientId, definition })).ReadJsonAsync();
            return result.GetProperty("count").GetInt32();
        }

        object C(object o) => o;
        Assert.Equal(3, await Count(new { match = "all", conditions = new[] { C(new { kind = "field", field = "country", op = "in", values = new[] { "us" } }) } }));
        Assert.Equal(1, await Count(new { match = "all", conditions = new[] { C(new { kind = "engagement", @event = "opened", withinDays = 30 }) } })); // machine open excluded
        Assert.Equal(2, await Count(new { match = "all", conditions = new[] { C(new { kind = "engagement", @event = "opened", withinDays = 60 }) } }));
        Assert.Equal(1, await Count(new { match = "all", conditions = new[] { C(new { kind = "engagement", @event = "clicked", withinDays = 30, campaignId }) } }));
        Assert.Equal(5, await Count(new { match = "all", conditions = new[] { C(new { kind = "purchase", op = "not_purchased", withinDays = 30 }) } }));
        Assert.Equal(1, await Count(new { match = "all", conditions = new[] { C(new { kind = "custom", field = "plan", op = "equals", value = "pro" }) } }));
        Assert.Equal(4, await Count(new { match = "all", conditions = new[] { C(new { kind = "tag", op = "has_not", value = "vip" }) } }));
        // (US AND vip) OR (GB AND plan = free)
        Assert.Equal(2, await Count(new
        {
            match = "any",
            conditions = Array.Empty<object>(),
            groups = new[]
            {
                new { match = "all", conditions = new[] { C(new { kind = "field", field = "country", op = "equals", value = "US" }), C(new { kind = "tag", op = "has", value = "vip" }) } },
                new { match = "all", conditions = new[] { C(new { kind = "field", field = "country", op = "equals", value = "GB" }), C(new { kind = "custom", field = "plan", op = "equals", value = "free" }) } },
            },
        }));
        Assert.Equal(1, await Count(new { match = "all", conditions = new[] { C(new { kind = "field", field = "first_name", op = "starts_with", value = "Ze" }) } }));

        // Saved segment drives the campaign audience.
        var segment = await (await staff.PostAsJsonAsync("/api/v1/agency/email/segments", new
        {
            clientAccountId = ws.ClientId, name = "US VIPs",
            definition = new { match = "all", conditions = new[] { C(new { kind = "field", field = "country", op = "equals", value = "US" }), C(new { kind = "tag", op = "has", value = "vip" }) } },
        })).ReadJsonAsync();
        Assert.Equal(1, segment.GetProperty("lastCount").GetInt32());
        var targeted = await fx.CreateCampaignAsync(staff, ws, extra: new { segmentId = segment.GetProperty("id").GetString() });
        var checklist = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{targeted.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.Equal(1, checklist.GetProperty("audienceCount").GetInt32());

        await (await staff.PostAsJsonAsync("/api/v1/agency/email/segments/preview", new { clientAccountId = ws.ClientId, definition = new { match = "all", conditions = new[] { C(new { kind = "sql", value = "1=1" }) } } }))
            .ShouldFailAsync(400, "email.segment_invalid");
    }

    [Fact]
    public async Task Templates_are_sanitized_validated_and_rendered_with_safe_merge_values()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        var created = await (await staff.PostAsJsonAsync("/api/v1/agency/email/templates", new
        {
            clientAccountId = ws.ClientId, name = "Promo", category = "promo", subject = "Hi {{first_name|there}}",
            design = EmailFixture.Design("<p onclick=\"x()\">Deal<script>alert(1)</script> <a href=\"javascript:alert(1)\">here</a></p>"),
        })).ReadJsonAsync();
        var html = created.GetProperty("design").GetProperty("blocks")[1].GetProperty("html").GetString()!;
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("javascript", html);

        await (await staff.PostAsJsonAsync("/api/v1/agency/email/templates", new
        {
            clientAccountId = ws.ClientId, name = "Bad", subject = "Your {{password}}", design = EmailFixture.Design(),
        })).ShouldFailAsync(400, "email.design_invalid");

        var render = await (await staff.PostAsJsonAsync("/api/v1/agency/email/templates/render", new
        {
            clientAccountId = ws.ClientId, subject = "Hi {{first_name|there}}", design = EmailFixture.Design(footer: false),
        })).ReadJsonAsync();
        Assert.Equal("Hi Alex", render.GetProperty("subject").GetString());
        Assert.Contains(render.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("footer"));
        Assert.Contains("Hello Alex", render.GetProperty("html").GetString());

        // Merge values from contact data are escaped in the real send.
        var ids = await fx.AddSubscribersAsync(ws, 1, (s, _) => s.FirstName = "<img src=x onerror=alert(1)>");
        var campaign = await fx.CreateCampaignAsync(staff, ws);
        await EmailFixture.ConfirmSendAsync(staff, campaign);
        await fx.RunJobAsync<CampaignSendJob>();
        var sent = fx.Email.For(ws.ClientId).Last();
        Assert.DoesNotContain("<img src=x", sent.Html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", sent.Html);

        // Test sends only go to verified staff addresses.
        var templateId = created.GetProperty("id").GetString();
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/templates/{templateId}/test", new { to = "customer@example.com" }))
            .ShouldFailAsync(400, "email.test_recipient_not_allowed");
        var admin = await fx.StaffUserAsync();
        var test = await (await staff.PostAsJsonAsync($"/api/v1/agency/email/templates/{templateId}/test", new { to = admin.Email })).ReadJsonAsync();
        Assert.True(test.GetProperty("sent").GetBoolean());
        Assert.StartsWith("[Test] ", fx.Email.Sent.Last().Subject);

        // Starter library is available to every workspace.
        var library = await (await staff.GetAsync($"/api/v1/agency/email/templates?clientId={ws.ClientId}")).ReadAsync<JsonElement>();
        Assert.True(library.EnumerateArray().Count(t => t.GetProperty("isGlobal").GetBoolean()) >= 7);
    }

    [Fact]
    public async Task Manual_contacts_consent_and_suppression_rules()
    {
        var staff = await fx.StaffAsync();
        var ws = await fx.CreateWorkspaceAsync();
        await (await staff.PostAsJsonAsync("/api/v1/agency/email/subscribers", new { clientAccountId = ws.ClientId, email = "x@example.com", attestEmailConsent = true }))
            .ShouldFailAsync(400, "email.consent_source_required");
        await (await staff.PostAsJsonAsync("/api/v1/agency/email/subscribers", new { clientAccountId = ws.ClientId, email = "x@example.com", customFields = new { email = "no" } }))
            .ShouldFailAsync(400, "email.subscriber_invalid");
        var contact = await (await staff.PostAsJsonAsync("/api/v1/agency/email/subscribers", new
        {
            clientAccountId = ws.ClientId, email = "x@example.com", phone = "+1 415 555 0100", firstName = "Xi", attestEmailConsent = true,
            consentSource = "Signed paper form at the event", listIds = new[] { ws.ListId }, tags = new[] { "event" }, customFields = new { birthday = "1990-05-02" },
        })).ReadJsonAsync();
        Assert.Equal("Granted", contact.GetProperty("emailConsent").GetString());
        Assert.Equal("+14155550100", contact.GetProperty("phone").GetString());
        Assert.Equal("Subscribed", contact.GetProperty("lists")[0].GetProperty("status").GetString());
        var id = contact.GetProperty("id").GetString();

        // Manual suppression wins, and consent cannot be re-granted over it.
        await (await staff.PostAsJsonAsync("/api/v1/agency/email/suppressions", new { clientAccountId = ws.ClientId, channel = "Email", value = "X@example.com", reason = "Manual" })).ReadJsonAsync();
        var checklistCampaign = await fx.CreateCampaignAsync(staff, ws);
        var checklist = await (await staff.GetAsync($"/api/v1/agency/email/campaigns/{checklistCampaign.GetProperty("id").GetString()}/checklist")).ReadJsonAsync();
        Assert.Equal(0, checklist.GetProperty("audienceCount").GetInt32());
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/subscribers/{id}/consent", new { channel = "Email", status = "Withdrawn", source = "Asked by phone" })).ReadJsonAsync();
        await (await staff.PostAsJsonAsync($"/api/v1/agency/email/subscribers/{id}/consent", new { channel = "Email", status = "Granted", source = "Changed mind" }))
            .ShouldFailAsync(409, "email.consent_suppressed");

        // Bounce import: hard bounces and complaints are suppressed.
        var result = await (await staff.PostAsJsonAsync("/api/v1/agency/email/suppressions/bounces", new
        {
            clientAccountId = ws.ClientId, content = "email,type\nhb@example.com,hard\nsb@example.com,soft\nspam@example.com,complaint\nnope,hard\n",
        })).ReadJsonAsync();
        Assert.Equal(1, result.GetProperty("hard").GetInt32());
        Assert.Equal(1, result.GetProperty("invalid").GetInt32());
        var suppressions = await (await staff.GetAsync($"/api/v1/agency/email/suppressions?clientAccountId={ws.ClientId}&pageSize=50")).ReadJsonAsync();
        var values = suppressions.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("value").GetString()).ToList();
        Assert.Contains("hb@example.com", values);
        Assert.Contains("spam@example.com", values);
        Assert.DoesNotContain("sb@example.com", values);

        // Erasure.
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/agency/email/subscribers/{id}")).StatusCode);
        await (await staff.GetAsync($"/api/v1/agency/email/subscribers/{id}")).ShouldFailAsync(404);
    }
}
