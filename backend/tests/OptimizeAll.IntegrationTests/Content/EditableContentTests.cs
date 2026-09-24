using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Website;

namespace OptimizeAll.IntegrationTests.Content;

/// <summary>Editable page copy (website + portal texts) and editable transactional email templates.</summary>
public sealed class EditableContentTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static JsonElement Entry(JsonElement catalog, string key) =>
        catalog.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("entries").EnumerateArray())
            .Single(e => e.GetProperty("key").GetString() == key);

    [Fact]
    public async Task Website_copy_overrides_are_public_audited_concurrency_checked_and_resettable()
    {
        var admin = await api.AdminAsync();
        var anon = api.Anonymous();

        var catalog = await admin.GetJsonAsync("/api/v1/agency/website/copy");
        var title = Entry(catalog, "home.hero.title");
        Assert.Equal("Marketing that grows revenue —", title.GetProperty("default").GetString());
        Assert.False(title.GetProperty("isCustomized").GetBoolean());
        // The website endpoint never lists portal texts.
        Assert.DoesNotContain(catalog.GetProperty("groups").EnumerateArray(), g => g.GetProperty("scope").GetString() == "Portal");

        var before = await anon.GetJsonAsync("/api/v1/content/copy");
        Assert.False(before.GetProperty("values").TryGetProperty("home.hero.title", out _));

        var saved = await admin.PutJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new object[]
            {
                new { key = "home.hero.title", value = "  Growth you can <b>measure</b> — ", concurrencyStamp = (Guid?)null },
                new { key = "home.hero.proof", value = "One\n\n  Two  \nThree", concurrencyStamp = (Guid?)null },
            },
        });
        var edited = Entry(saved, "home.hero.title");
        Assert.True(edited.GetProperty("isCustomized").GetBoolean());
        Assert.Equal("Growth you can measure —", edited.GetProperty("value").GetString()); // markup stripped, trimmed
        Assert.Equal("One\nTwo\nThree", Entry(saved, "home.hero.proof").GetProperty("value").GetString());
        var stamp = edited.GetProperty("concurrencyStamp").GetGuid();

        var values = (await anon.GetJsonAsync("/api/v1/content/copy")).GetProperty("values");
        Assert.Equal("Growth you can measure —", values.GetProperty("home.hero.title").GetString());

        // A second editor who still holds "no override" (null stamp) or an old stamp gets 409, not a silent overwrite.
        var conflict = await admin.PutAsJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new[] { new { key = "home.hero.title", value = "Lost update", concurrencyStamp = (Guid?)null } },
        });
        await conflict.ShouldFailAsync(409, "concurrency.conflict");
        var stale = await admin.PutAsJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new[] { new { key = "home.hero.title", value = "Lost update", concurrencyStamp = (Guid?)Guid.NewGuid() } },
        });
        await stale.ShouldFailAsync(409, "concurrency.conflict");

        // Validation: every problem is reported per key.
        var invalid = await admin.PutAsJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new object[]
            {
                new { key = "home.process.steps", value = "Audit without separator", concurrencyStamp = (Guid?)null },
                new { key = "shared.footer.copyright", value = "© {yeer}", concurrencyStamp = (Guid?)null },
                new { key = "no.such.key", value = "x", concurrencyStamp = (Guid?)null },
                new { key = "faq.hero.title", value = "Portal key on the website endpoint", concurrencyStamp = (Guid?)null },
            },
        });
        Assert.Equal(400, (int)invalid.StatusCode);
        var errors = JsonSerializer.Deserialize<JsonElement>(await invalid.Content.ReadAsStringAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("home.process.steps", out _));
        Assert.True(errors.TryGetProperty("shared.footer.copyright", out _));
        Assert.True(errors.TryGetProperty("changes[2].key", out _));
        Assert.True(errors.TryGetProperty("changes[3].key", out _));

        // Reset (null value) removes the override: the default is back everywhere.
        var reset = await admin.PutJsonAsync("/api/v1/agency/website/copy", new
        {
            changes = new[] { new { key = "home.hero.title", value = (string?)null, concurrencyStamp = (Guid?)stamp } },
        });
        Assert.False(Entry(reset, "home.hero.title").GetProperty("isCustomized").GetBoolean());
        Assert.False((await anon.GetJsonAsync("/api/v1/content/copy")).GetProperty("values").TryGetProperty("home.hero.title", out _));

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == "ContentCopyEntry").Select(a => a.Action).ToListAsync());
        Assert.Contains("content.copy_updated", actions);
        Assert.Contains("content.copy_reset", actions);
    }

    [Fact]
    public async Task Portal_copy_is_edited_by_content_editors_and_both_scopes_need_their_permission()
    {
        var admin = await api.AdminAsync();
        var portal = await admin.GetJsonAsync("/api/v1/admin/content/copy");
        Assert.All(portal.GetProperty("groups").EnumerateArray(), g => Assert.Equal("Portal", g.GetProperty("scope").GetString()));
        await admin.PutJsonAsync("/api/v1/admin/content/copy", new
        {
            changes = new[] { new { key = "faq.hero.title", value = "Help & answers", concurrencyStamp = (Guid?)null } },
        });
        var values = (await api.Anonymous().GetJsonAsync("/api/v1/content/copy")).GetProperty("values");
        Assert.Equal("Help & answers", values.GetProperty("faq.hero.title").GetString());

        // Roles without site.manage / content.manage are refused (403), and anonymous callers are challenged (401).
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        await (await manager.GetAsync("/api/v1/agency/website/copy")).ShouldFailAsync(403);
        await (await manager.GetAsync("/api/v1/admin/content/copy")).ShouldFailAsync(403);
        await (await manager.PutAsJsonAsync("/api/v1/admin/content/copy", new { changes = new[] { new { key = "faq.hero.title", value = "x" } } }))
            .ShouldFailAsync(403);
        await (await api.Anonymous().GetAsync("/api/v1/agency/website/copy")).ShouldFailAsync(401);
    }

    [Fact]
    public async Task Email_templates_are_listed_edited_previewed_validated_and_reset()
    {
        var admin = await api.AdminAsync();
        var list = await admin.GetJsonAsync("/api/v1/admin/email-templates");
        var keys = list.EnumerateArray().Select(t => t.GetProperty("key").GetString()).ToList();
        Assert.Contains("notification.layout", keys);
        Assert.Contains("notification.submission.decision", keys);
        Assert.Contains("website.newsletter_confirm", keys);

        var template = await admin.GetJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm");
        Assert.False(template.GetProperty("isCustomized").GetBoolean());
        Assert.Contains(template.GetProperty("variables").EnumerateArray(), v => v.GetProperty("name").GetString() == "confirmUrl" && v.GetProperty("required").GetBoolean());

        // Unknown variables and a missing required link are rejected.
        var unknown = await admin.PutAsJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm", new
        {
            subject = "Hello {{nobody}}", body = "Confirm: {{confirmUrl}} — leave: {{unsubscribeUrl}}",
        });
        Assert.Equal(400, (int)unknown.StatusCode);
        Assert.True(JsonSerializer.Deserialize<JsonElement>(await unknown.Content.ReadAsStringAsync()).GetProperty("errors").TryGetProperty("subject", out _));
        var missing = await admin.PutAsJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm", new
        {
            subject = "Confirm", body = "Please confirm: {{confirmUrl}}",
        });
        Assert.Equal(400, (int)missing.StatusCode);
        Assert.Contains("unsubscribeUrl", await missing.Content.ReadAsStringAsync());

        // Preview renders unsaved text with sample values.
        var preview = await admin.PostJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm/preview", new
        {
            subject = "Almost there, {{siteName}}", body = "Tap {{confirmUrl}} (valid {{hours}} h). Leave: {{unsubscribeUrl}}",
        }, 200);
        Assert.Equal("Almost there, Optimize All", preview.GetProperty("subject").GetString());
        Assert.Contains("valid 48 h", preview.GetProperty("text").GetString());

        var saved = await admin.PutJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm", new
        {
            subject = "Please confirm your subscription to {{siteName}}",
            body = "Tap to confirm: {{confirmUrl}}\n\nLeave any time: {{unsubscribeUrl}}",
            concurrencyStamp = (Guid?)null,
        });
        Assert.True(saved.GetProperty("isCustomized").GetBoolean());
        var stamp = saved.GetProperty("concurrencyStamp").GetGuid();

        // The next newsletter signup uses the edited wording (and still carries both links).
        var anon = api.Anonymous();
        var email = $"tpl-{Guid.NewGuid():N}@example.test";
        var form = WebsiteTestKit.Form(await api.FormTokenAsync(anon), new Dictionary<string, object?> { ["email"] = email, ["source"] = "footer" },
            ConsentTexts.NewsletterVersion);
        await anon.PostJsonAsync("/api/v1/public/newsletter/subscribe", form, 202);
        var mail = await anon.GetJsonAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(email)}");
        Assert.Equal("Please confirm your subscription to Optimize All", mail.GetProperty("subject").GetString());
        var links = mail.GetProperty("links").EnumerateArray().Select(l => l.GetString()!).ToList();
        Assert.Contains(links, l => l.Contains("/newsletter/confirm?token="));
        Assert.Contains(links, l => l.Contains("/newsletter/unsubscribe?token="));

        // Stale edits are refused; reset needs the current stamp and restores the default.
        await (await admin.PutAsJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm", new
        {
            subject = "Lost", body = "{{confirmUrl}} {{unsubscribeUrl}}", concurrencyStamp = (Guid?)null,
        })).ShouldFailAsync(409, "concurrency.conflict");
        await (await admin.DeleteAsync($"/api/v1/admin/email-templates/website.newsletter_confirm?concurrencyStamp={Guid.NewGuid()}"))
            .ShouldFailAsync(409, "concurrency.conflict");
        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/admin/email-templates/website.newsletter_confirm?concurrencyStamp={stamp}")).StatusCode);
        var afterReset = await admin.GetJsonAsync("/api/v1/admin/email-templates/website.newsletter_confirm");
        Assert.False(afterReset.GetProperty("isCustomized").GetBoolean());
        Assert.Equal("Confirm your Optimize All newsletter subscription", afterReset.GetProperty("subject").GetString());

        await (await admin.GetAsync("/api/v1/admin/email-templates/no.such.template")).ShouldFailAsync(404, "email_template.not_found");
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.GetAsync("/api/v1/admin/email-templates")).ShouldFailAsync(403);
    }

    [Fact]
    public async Task Notification_layout_edits_reach_sent_notification_emails()
    {
        var admin = await api.AdminAsync();
        await admin.PutJsonAsync("/api/v1/admin/email-templates/notification.layout", new
        {
            subject = "[Optimize All] {{subject}}",
            body = "Hello {{displayName}},\n\n{{content}}\n\n{{action}}Thanks, the team",
            actionLabel = "See details",
            concurrencyStamp = (Guid?)null,
        });
        var preview = await admin.PostJsonAsync("/api/v1/admin/email-templates/notification.payout.paid/preview", new
        {
            subject = "Money sent: {{title}}", body = "{{body}}",
        }, 200);
        Assert.Equal("[Optimize All] Money sent: Your submission was approved", preview.GetProperty("subject").GetString());
        Assert.StartsWith("Hello Ada Lovelace,", preview.GetProperty("text").GetString());
        Assert.Contains("See details: https://app.example.com/app/submissions/123", preview.GetProperty("text").GetString());
        Assert.Contains(">See details</a>", preview.GetProperty("html").GetString());

        // The layout must keep {{content}}.
        var broken = await admin.PutAsJsonAsync("/api/v1/admin/email-templates/notification.layout", new
        {
            subject = "{{subject}}", body = "Hello", actionLabel = "Open", concurrencyStamp = (Guid?)null,
        });
        await broken.ShouldFailAsync(400, "email_template.invalid");
    }

    [Fact]
    public async Task Account_email_templates_are_listed_require_their_link_and_reach_sent_auth_emails()
    {
        var admin = await api.AdminAsync();
        var list = await admin.GetJsonAsync("/api/v1/admin/email-templates");
        var account = list.EnumerateArray().Where(t => t.GetProperty("group").GetString() == "Account emails")
            .Select(t => t.GetProperty("key").GetString()).ToList();
        Assert.Contains("auth.verify_email", account);
        Assert.Contains("auth.password_reset", account);
        Assert.Contains("auth.duplicate_registration", account);
        Assert.Contains("auth.google_linked", account);

        // The reset link cannot be edited away.
        var missing = await admin.PutAsJsonAsync("/api/v1/admin/email-templates/auth.password_reset", new
        {
            subject = "Reset", body = "Hi {{displayName}}, reset your password.", concurrencyStamp = (Guid?)null,
        });
        await missing.ShouldFailAsync(400, "email_template.invalid");
        Assert.Contains("resetUrl", await missing.Content.ReadAsStringAsync());

        var saved = await admin.PutJsonAsync("/api/v1/admin/email-templates/auth.password_reset", new
        {
            subject = "Choose a new {{siteName}} password",
            body = "Hello {{displayName}},\n\nPick a new password here: {{resetUrl}}",
            concurrencyStamp = (Guid?)null,
        });
        var stamp = saved.GetProperty("concurrencyStamp").GetGuid();

        var user = await api.CreateUserAsync();
        var anon = api.Anonymous();
        Assert.Equal(202, (int)(await anon.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email })).StatusCode);
        var mail = await anon.GetJsonAsync($"/api/v1/dev/mailbox?to={Uri.EscapeDataString(user.Email)}");
        Assert.Equal("Choose a new Optimize All password", mail.GetProperty("subject").GetString());
        Assert.Contains(mail.GetProperty("links").EnumerateArray(), l => l.GetString()!.Contains("/reset-password?token="));

        Assert.Equal(204, (int)(await admin.DeleteAsync($"/api/v1/admin/email-templates/auth.password_reset?concurrencyStamp={stamp}")).StatusCode);
        var (_, participant) = await api.CreateClientAsync(Role.Participant);
        await (await participant.PutAsJsonAsync("/api/v1/admin/email-templates/auth.password_reset", new
        {
            subject = "x", body = "{{resetUrl}}", concurrencyStamp = (Guid?)null,
        })).ShouldFailAsync(403);
    }
}
