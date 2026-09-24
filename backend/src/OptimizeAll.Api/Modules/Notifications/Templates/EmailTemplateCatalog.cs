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
    public const string AuthVerifyEmail = "auth.verify_email";
    public const string AuthPasswordReset = "auth.password_reset";
    public const string AuthDuplicateRegistration = "auth.duplicate_registration";
    public const string AuthGoogleLinked = "auth.google_linked";

    public const string GroupLayout = "Notification layout";
    public const string GroupNotifications = "Notification emails";
    public const string GroupWebsite = "Website emails";
    public const string GroupAccount = "Account emails";

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

        AddAccountEmails(list);
        return list;
    }

    /// <summary>
    /// Sign-up and sign-in emails sent by the Auth module (<see cref="AccountEmails"/>). The defaults are the texts the
    /// Auth module sent before they became editable, word for word (unit test <c>AccountEmailTemplateTests</c>). Each
    /// must keep its action link.
    /// </summary>
    private static void AddAccountEmails(List<EmailTemplateDefinition> list)
    {
        var forgotUrl = new EmailVariable("forgotPasswordUrl", "The \"forgot password\" page.", "https://app.example.com/forgot-password", Required: true);

        // The display name is attacker-controlled until the address is verified, so it is not offered here.
        list.Add(new EmailTemplateDefinition(AuthVerifyEmail, GroupAccount, "Verify email address",
            "Sent after someone registers with an email and password, and when they ask for a new verification link.",
            "Verify your Optimize All email",
            "Welcome to Optimize All!\n\nConfirm your email address to start joining paid campaigns:\n{{verifyUrl}}\n\n" +
            "This link expires in {{hours}} hours.",
            null,
            new[]
            {
                new EmailVariable("verifyUrl", "The verification link.", "https://app.example.com/verify-email?token=abc", Required: true),
                new EmailVariable("hours", "How long the link stays valid.", "48"),
                SiteName,
            },
            HasHtml: false));

        list.Add(new EmailTemplateDefinition(AuthPasswordReset, GroupAccount, "Reset password",
            "Sent when someone asks to reset their password (\"Forgot password?\").",
            "Reset your Optimize All password",
            "Hi {{displayName}},\n\nUse this link within one hour to choose a new password:\n{{resetUrl}}\n\n" +
            "If you didn't ask for this, you can ignore this email; your password won't change.",
            null,
            new[]
            {
                new EmailVariable("resetUrl", "The password-reset link (valid for one hour).", "https://app.example.com/reset-password?token=abc", Required: true),
                DisplayName, SiteName,
            },
            HasHtml: false));

        list.Add(new EmailTemplateDefinition(AuthDuplicateRegistration, GroupAccount, "Someone tried to register with your email",
            "Sent to the owner of an existing account when someone registers with the same email address (at most once a day).",
            "Someone tried to register with your email",
            "Hi {{displayName}},\n\nSomeone tried to create an Optimize All account with this email address. " +
            "If it was you, sign in or reset your password at {{forgotPasswordUrl}}.\n\n" +
            "If it wasn't you, you can ignore this message.",
            null,
            new[] { forgotUrl, DisplayName, SiteName },
            HasHtml: false));

        list.Add(new EmailTemplateDefinition(AuthGoogleLinked, GroupAccount, "Google sign-in connected",
            "Security notice sent when a Google account is connected to an existing account (from the profile, or automatically by a matching verified email).",
            "Google sign-in was connected to your Optimize All account",
            "Hi {{displayName}},\n\nThe Google account {{googleEmail}} can now be used to sign in to your Optimize All account.\n\n" +
            "If this wasn't you, reset your password at {{forgotPasswordUrl}} right away and disconnect Google in your " +
            "profile's security settings.",
            null,
            new[]
            {
                new EmailVariable("googleEmail", "The email address of the connected Google account.", "ada@gmail.com"),
                forgotUrl, DisplayName, SiteName,
            },
            HasHtml: false));
    }
}
