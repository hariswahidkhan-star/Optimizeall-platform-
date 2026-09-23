using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.Api.Modules.EmailMarketing.Seed;

/// <summary>
/// Starter templates of the agency library (global: every workspace can use or copy them). Copy is production-ready
/// and uses merge tags with fallbacks; links point at example.com and are replaced with the client's URLs when the
/// template is adapted.
/// </summary>
public static class StarterTemplates
{
    public sealed record Starter(string Key, string Name, string Category, string Subject, string PreviewText, EmailDesign Design);

    private const string Navy = "#1F2659";
    private const string Amber = "#FCB31E";

    private static DesignBlock Header(string title, string? subtitle = null) =>
        new() { Type = "header", Title = title, Subtitle = subtitle, Align = "center", BackgroundColor = "#ffffff", TextColor = Navy };

    private static DesignBlock TextBlock(string html) => new() { Type = "text", Html = html };

    private static DesignBlock Button(string text, string href, bool accent = false) =>
        new() { Type = "button", Text = text, Href = href, Align = "center", Color = accent ? Amber : Navy, TextColor = accent ? Navy : "#ffffff" };

    private static DesignBlock Divider() => new() { Type = "divider", Height = 16 };

    private static DesignBlock Footer(string? extra = null) => new() { Type = "footer", Html = extra, ShowPreferencesLink = true };

    private static DesignBlock Social() => new()
    {
        Type = "social",
        Links = new List<SocialLink>
        {
            new() { Network = "instagram", Url = "https://www.instagram.com/example" },
            new() { Network = "linkedin", Url = "https://www.linkedin.com/company/example" },
            new() { Network = "website", Url = "https://www.example.com" },
        },
    };

    private static EmailDesign Design(params DesignBlock[] blocks) => new() { Blocks = blocks.ToList() };

    public static readonly IReadOnlyList<Starter> All = new[]
    {
        new Starter("starter-welcome", "Welcome email", "welcome",
            "Welcome to {{org_name}}, {{first_name|friend}}",
            "Here's what to expect from us — and a little something to get you started.",
            Design(
                Header("Welcome aboard", "We're glad you're here."),
                TextBlock("<p>Hi {{first_name|there}},</p><p>Thanks for joining {{org_name}}. From now on you'll be the first to hear about new releases, practical tips and the occasional members-only offer.</p><p>Here's what you can expect:</p><ul><li><strong>One useful email a week</strong> — no filler, ever.</li><li><strong>Early access</strong> to launches and events.</li><li><strong>Easy control</strong> — change how often you hear from us at any time.</li></ul>"),
                Button("Explore what's new", "https://www.example.com/start?utm_source=email&utm_medium=welcome"),
                TextBlock("<p>Questions? Just reply to this email — a real person reads every message.</p><p>Warmly,<br>The {{org_name}} team</p>"),
                Social(),
                Footer())),

        new Starter("starter-newsletter", "Monthly newsletter", "newsletter",
            "Your {{org_name}} update: what's new this month",
            "Three stories worth your time, plus one thing to try this week.",
            Design(
                Header("This month at {{org_name}}"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>Here's a quick round-up of what we've been working on and what we think you'll find useful.</p>"),
                Divider(),
                TextBlock("<h2>1. The feature you asked for</h2><p>We listened. Our latest update makes the thing you use most every day faster and simpler — here's a two-minute walkthrough.</p>"),
                Button("Read the story", "https://www.example.com/blog/latest?utm_source=email&utm_medium=newsletter"),
                Divider(),
                new DesignBlock
                {
                    Type = "columns",
                    Columns = new List<DesignColumn>
                    {
                        new() { Blocks = { TextBlock("<h3>From the blog</h3><p>Five habits of teams that hit their goals — and how to borrow them.</p><p><a href=\"https://www.example.com/blog?utm_source=email\">Read more</a></p>") } },
                        new() { Blocks = { TextBlock("<h3>Customer spotlight</h3><p>How one customer doubled their results in a single quarter.</p><p><a href=\"https://www.example.com/customers?utm_source=email\">See how</a></p>") } },
                    },
                },
                Divider(),
                TextBlock("<h2>Try this week</h2><p>Set aside 15 minutes on Friday to review what worked. Small reviews compound into big improvements.</p><p>Until next month,<br>The {{org_name}} team</p>"),
                Social(),
                Footer())),

        new Starter("starter-promo", "Promotion / sale", "promo",
            "{{first_name|Hi}}, 20% off ends Sunday",
            "Our biggest offer of the season — take 20% off everything until Sunday at midnight.",
            Design(
                Header("20% off everything", "Until Sunday at midnight"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>As a thank-you for being part of the {{org_name}} community, we're taking <strong>20% off everything</strong> this weekend. Use the code below at checkout.</p><p style=\"text-align:center\"><strong>Code: THANKYOU20</strong></p>"),
                Button("Shop the offer", "https://www.example.com/offer?utm_source=email&utm_medium=promo", accent: true),
                TextBlock("<p>The offer ends Sunday at 23:59 and can't be combined with other discounts. Items in limited supply may sell out.</p>"),
                Footer("<p>Prices and availability are subject to change. See the offer page for full terms.</p>"))),

        new Starter("starter-abandoned-cart", "Abandoned cart reminder", "abandoned-cart",
            "You left something behind, {{first_name|friend}}",
            "Your cart is saved — complete your order before items sell out.",
            Design(
                Header("Still thinking it over?"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>You left {{event.item_name|a few items}} in your cart. We've saved them for you, but popular items don't stay in stock for long.</p><p>Need help deciding? Our team is happy to answer questions about sizing, delivery or returns — just reply.</p>"),
                Button("Return to my cart", "https://www.example.com/cart?utm_source=email&utm_medium=abandoned-cart"),
                TextBlock("<ul><li>Free returns within 30 days</li><li>Secure checkout</li><li>Fast, tracked delivery</li></ul>"),
                Footer())),

        new Starter("starter-reengagement", "Re-engagement (we miss you)", "re-engagement",
            "Is this goodbye, {{first_name|friend}}?",
            "We haven't heard from you in a while. Tell us what you'd like to receive — or leave in one click.",
            Design(
                Header("We miss you"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>It's been a while since you opened one of our emails, and we don't want to crowd your inbox with messages you don't read.</p><p>If you'd still like to hear from us, click below and we'll keep you on the list. You can also choose to hear from us less often.</p>"),
                Button("Yes, keep me subscribed", "https://www.example.com/stay?utm_source=email&utm_medium=reengagement"),
                TextBlock("<p>Prefer fewer emails? <a href=\"{{preferences_url}}\">Update your preferences</a>. If we don't hear from you, we'll stop emailing you in 30 days — no hard feelings.</p>"),
                Footer())),

        new Starter("starter-event-invite", "Event invitation", "event",
            "You're invited: join us live",
            "Save your seat for a free live session with our experts — spaces are limited.",
            Design(
                Header("You're invited", "A free live session with our experts"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>We'd love you to join us for a live, interactive session where our team shares what's working right now — and answers your questions.</p><p><strong>When:</strong> Thursday, 18:00 (your local time)<br><strong>Where:</strong> Online — a link is sent after you register<br><strong>Cost:</strong> Free</p>"),
                Button("Save my seat", "https://www.example.com/events/live?utm_source=email&utm_medium=invite", accent: true),
                TextBlock("<p>Can't make it live? Register anyway and we'll send you the recording.</p><p>See you there,<br>The {{org_name}} team</p>"),
                Footer())),

        new Starter("starter-nps", "NPS survey", "nps",
            "{{first_name|Quick question}}: how likely are you to recommend us?",
            "One question, ten seconds. Your answer shapes what we build next.",
            Design(
                Header("How are we doing?"),
                TextBlock("<p>Hi {{first_name|there}},</p><p>On a scale from 0 to 10, how likely are you to recommend {{org_name}} to a friend or colleague?</p>"),
                new DesignBlock
                {
                    Type = "columns",
                    Columns = new List<DesignColumn>
                    {
                        new() { Blocks = { Button("0–6", "https://www.example.com/survey?score=detractor&utm_source=email") } },
                        new() { Blocks = { Button("7–8", "https://www.example.com/survey?score=passive&utm_source=email") } },
                        new() { Blocks = { Button("9–10", "https://www.example.com/survey?score=promoter&utm_source=email", accent: true) } },
                    },
                },
                TextBlock("<p>Click the range that fits and add a comment if you have a moment. We read every response.</p><p>Thank you,<br>The {{org_name}} team</p>"),
                Footer())),
    };
}
