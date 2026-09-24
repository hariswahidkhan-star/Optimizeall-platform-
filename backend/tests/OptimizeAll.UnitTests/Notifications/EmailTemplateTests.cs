using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Api.Modules.Notifications.Templates;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;

namespace OptimizeAll.UnitTests.Notifications;

public sealed class EmailTemplateTests
{
    private static EmailTemplateTexts Layout()
    {
        var d = EmailTemplateCatalog.Find(EmailTemplateCatalog.LayoutKey)!;
        return new EmailTemplateTexts(d.Subject, d.Body, d.ActionLabel);
    }

    private static readonly EmailTemplateTexts Own = new("{{title}}", "{{body}}", null);

    [Fact]
    public void Default_templates_reproduce_the_original_notification_email_text()
    {
        var r = EmailTemplateService.RenderNotification(Layout(), Own, "Ada", "Approved", "Your post was approved.",
            "https://app.example.com/app/submissions/1", "https://app.example.com/app/profile/notification-preferences", "Optimize All");

        Assert.Equal("Approved", r.Subject);
        Assert.Equal(
            "Hi Ada,\n\nYour post was approved.\n\nOpen in Optimize All: https://app.example.com/app/submissions/1\n\n— Optimize All\n" +
            "Manage your notification settings: https://app.example.com/app/profile/notification-preferences\n",
            r.Text);
        Assert.Contains("href=\"https://app.example.com/app/submissions/1\"", r.Html);
        Assert.Contains(">Open in Optimize All</a>", r.Html);
    }

    [Fact]
    public void Without_a_link_there_is_no_button()
    {
        var r = EmailTemplateService.RenderNotification(Layout(), Own, "Ada", "T", "B", null, "https://x.test/prefs", "Optimize All");
        Assert.Equal("Hi Ada,\n\nB\n\n— Optimize All\nManage your notification settings: https://x.test/prefs\n", r.Text);
        Assert.DoesNotContain("Open in Optimize All", r.Html);
    }

    [Fact]
    public void Edited_templates_are_applied_and_every_value_is_encoded_in_html()
    {
        var layout = new EmailTemplateTexts("[{{siteName}}] {{subject}}", "Hello {{displayName}}!\n\n{{content}}\n\n{{action}}Cheers", "View it");
        var own = new EmailTemplateTexts("Update: {{title}}", "{{body}}\nLink: {{link}}", null);
        var r = EmailTemplateService.RenderNotification(layout, own, "<b>Sara</b>", "Paid", "<script>x</script>", "https://a.test/p",
            "https://a.test/prefs", "Acme");

        Assert.Equal("[Acme] Update: Paid", r.Subject);
        Assert.StartsWith("Hello <b>Sara</b>!\n\n<script>x</script>\nLink: https://a.test/p\n\nView it: https://a.test/p\n\nCheers", r.Text);
        Assert.DoesNotContain("<script>", r.Html);
        Assert.DoesNotContain("<b>Sara</b>", r.Html);
        Assert.Contains("&lt;b&gt;Sara&lt;/b&gt;", r.Html);
        Assert.Contains(">View it</a>", r.Html);
        // The notification-settings link cannot be edited away.
        Assert.Contains("href=\"https://a.test/prefs\"", r.Html);
        Assert.Contains("Manage your notification settings: https://a.test/prefs", r.Text);
    }

    [Fact]
    public void Compose_uses_the_default_layout()
    {
        var n = new Notification { Type = NotificationTypes.PayoutPaid, Title = "Paid", Body = "Sent.", LinkUrl = "/app/payouts" };
        var user = new User { Email = "a@example.com", DisplayName = "Ada" };
        var message = EmailChannelSender.Compose(n, user, "https://app.example.com");
        Assert.Equal("Paid", message.Subject);
        Assert.StartsWith("Hi Ada,\n\nSent.\n\nOpen in Optimize All: https://app.example.com/app/payouts", message.TextBody);
    }

    [Fact]
    public void Catalog_has_a_template_for_every_notification_type_and_the_website_emails()
    {
        var keys = EmailTemplateCatalog.All.Select(t => t.Key).ToHashSet();
        foreach (var type in NotificationCatalog.AllTypes)
            Assert.Contains(EmailTemplateCatalog.NotificationKey(type), keys);
        Assert.Contains(EmailTemplateCatalog.NewsletterConfirm, keys);
        Assert.Contains(EmailTemplateCatalog.BookingConfirmed, keys);
        Assert.Contains(EmailTemplateCatalog.BookingCancelled, keys);
        Assert.Contains(EmailTemplateCatalog.BookingRescheduled, keys);
        Assert.Equal(keys.Count, EmailTemplateCatalog.All.Count);
    }

    [Fact]
    public void Plain_text_rendering_substitutes_known_and_blanks_unknown_variables()
    {
        var values = new Dictionary<string, string> { ["name"] = "Ada" };
        Assert.Equal("Hi Ada, ", EmailTemplateRenderer.RenderText("Hi {{ name }}, {{missing}}", values));
    }
}
