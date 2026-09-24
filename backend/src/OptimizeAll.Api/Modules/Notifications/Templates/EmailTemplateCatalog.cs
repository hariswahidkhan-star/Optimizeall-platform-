namespace OptimizeAll.Api.Modules.Notifications.Templates;

public sealed record EmailVariable(string Name, string Description, string Sample, bool Required = false);

/// <summary>A transactional email an editor can reword. The defaults are the texts the platform ships with.</summary>
public sealed record EmailTemplateDefinition(
    string Key, string Group, string Name, string Description, string Subject, string Body, string? ActionLabel,
    IReadOnlyList<EmailVariable> Variables, bool HasHtml);

/// <summary>Every editable transactional email template (see docs/DYNAMIC_CONTENT.md).</summary>
public static class EmailTemplateCatalog
{
    public const string LayoutKey = "notification.layout";
    public const string NotificationPrefix = "notification.";
    public const string NewsletterConfirm = "website.newsletter_confirm";
    public const string BookingConfirmed = "website.booking_confirmed";
    public const string BookingCancelled = "website.booking_cancelled";
    public const string BookingRescheduled = "website.booking_rescheduled";

    public const string GroupLayout = "Notification layout";
    public const string GroupNotifications = "Notification emails";
    public const string GroupWebsite = "Website emails";

    private static readonly EmailVariable SiteName = new("siteName", "The site name from site settings.", "Optimize All");
    private static readonly EmailVariable DisplayName = new("displayName", "The recipient's display name.", "Ada Lovelace");

    public static string NotificationKey(string type) => NotificationPrefix + type;

    /// <summary>Notification kinds with their own template (the participant notification catalog plus website inquiries).</summary>
    public static IReadOnlyList<string> TemplatedTypes =>
        NotificationCatalog.AllTypes.Append(Website.Leads.WebsiteLinks.InquiryNotificationType).Distinct().ToArray();

    private static readonly Lazy<IReadOnlyList<EmailTemplateDefinition>> LazyAll = new(Build);

    public static IReadOnlyList<EmailTemplateDefinition> All => LazyAll.Value;

    public static EmailTemplateDefinition? Find(string key) => All.FirstOrDefault(t => t.Key == key);

    private static IReadOnlyList<EmailTemplateDefinition> Build()
    {
        var list = new List<EmailTemplateDefinition>
        {
            new(LayoutKey, GroupLayout, "Notification email layout",
                "Wraps every notification email: greeting, the notification's own text, the button and the sign-off. The notification-settings link is always added below.",
                "{{subject}}",
                "Hi {{displayName}},\n\n{{content}}\n\n{{action}}— Optimize All",
                "Open in Optimize All",
                new[]
                {
                    new EmailVariable("subject", "The notification email's subject (its own template).", "Your submission was approved"),
                    new EmailVariable("content", "The notification email's text (its own template).", "Your post for Summer Launch was approved. The reward has been added to your earnings.", Required: true),
                    new EmailVariable("action", "The button (in plain text: the button label and the link), when the notification has a link.", "Open in Optimize All: https://app.example.com/app/submissions/123"),
                    DisplayName, SiteName,
                },
                HasHtml: true),
        };

        foreach (var type in TemplatedTypes)
        {
            var (label, description) = type == Website.Leads.WebsiteLinks.InquiryNotificationType
                ? ("Website inquiries (staff)", "When someone submits a contact, audit, quote or booking form on the website.")
                : NotificationCatalog.Describe(type);
            list.Add(new EmailTemplateDefinition(NotificationKey(type), GroupNotifications, label,
                string.IsNullOrEmpty(description) ? $"Notification type {type}." : description,
                "{{title}}", "{{body}}", null,
                new[]
                {
                    new EmailVariable("title", "The notification title written by the platform (e.g. the decision).", "Your submission was approved"),
                    new EmailVariable("body", "The notification text written by the platform (details such as amounts and reasons).", "Your post for Summer Launch was approved. The reward has been added to your earnings."),
                    new EmailVariable("link", "The link the button opens (may be empty).", "https://app.example.com/app/submissions/123"),
                    DisplayName, SiteName,
                },
                HasHtml: true));
        }

        list.Add(new EmailTemplateDefinition(NewsletterConfirm, GroupWebsite, "Newsletter: confirm subscription",
            "Double opt-in email sent after someone signs up for the newsletter.",
            "Confirm your Optimize All newsletter subscription",
            "Hi,\n\nPlease confirm that you'd like to receive the Optimize All newsletter:\n{{confirmUrl}}\n\n" +
            "The link expires in {{hours}} hours. If you didn't sign up, ignore this email and you won't hear from us.\n\n" +
            "Unsubscribe at any time: {{unsubscribeUrl}}\n\n— The Optimize All team",
            null,
            new[]
            {
                new EmailVariable("confirmUrl", "The confirmation link.", "https://www.example.com/newsletter/confirm?token=abc", Required: true),
                new EmailVariable("unsubscribeUrl", "The one-click unsubscribe link.", "https://www.example.com/newsletter/unsubscribe?token=xyz", Required: true),
                new EmailVariable("hours", "How long the confirmation link stays valid.", "48"),
                SiteName,
            },
            HasHtml: false));

        var bookingVars = new List<EmailVariable>
        {
            new("name", "The visitor's name.", "Ada Lovelace"),
            new("when", "Date and time of the call in the visitor's time zone.", "Tuesday 6 October 2026, 10:00–10:30 (Europe/London)"),
            new("reference", "The booking reference.", "OA-7F3K2Q"),
            SiteName,
        };
        list.Add(new EmailTemplateDefinition(BookingConfirmed, GroupWebsite, "Consultation: booked",
            "Sent to the visitor right after they book a consultation.",
            "Your consultation with Optimize All is confirmed",
            "Hi {{name}},\n\nYour free consultation is booked for {{when}}.\n\n" +
            "We'll send a video-call link before the meeting. To change or cancel, just reply to this email.\n\n" +
            "Reference: {{reference}}\n\n— The Optimize All team",
            null, bookingVars, HasHtml: false));
        list.Add(new EmailTemplateDefinition(BookingCancelled, GroupWebsite, "Consultation: cancelled by the agency",
            "Sent when staff cancel a booking and choose to notify the visitor.",
            "Your consultation with Optimize All was cancelled",
            "Hi {{name}},\n\nWe're sorry — we had to cancel your consultation on {{when}}.\nReason: {{reason}}\n\n" +
            "Please book another time at {{bookUrl}} or reply to this email.\n\n— The Optimize All team",
            null,
            bookingVars.Concat(new[]
            {
                new EmailVariable("reason", "The cancellation reason entered by staff.", "Our strategist is unwell."),
                new EmailVariable("bookUrl", "The public booking page.", "https://www.example.com/book-a-consultation"),
            }).ToArray(),
            HasHtml: false));
        list.Add(new EmailTemplateDefinition(BookingRescheduled, GroupWebsite, "Consultation: moved to a new time",
            "Sent when staff reschedule a booking and choose to notify the visitor.",
            "Your consultation with Optimize All has a new time",
            "Hi {{name}},\n\nYour consultation has been moved to {{when}}.\n\nIf the new time doesn't work, reply to this email.\n\n— The Optimize All team",
            null, bookingVars, HasHtml: false));
        return list;
    }
}
