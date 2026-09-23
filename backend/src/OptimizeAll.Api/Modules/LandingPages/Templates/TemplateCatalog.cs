using System.Text.Json;
using OptimizeAll.Domain.LandingPages;
using FormFieldDef = OptimizeAll.Domain.LandingPages.FormField;

namespace OptimizeAll.Api.Modules.LandingPages.Templates;

/// <summary>
/// Baseline landing-page and form templates (seeded into landing_page_templates / form_templates). Landing templates use
/// the placeholders <c>{{form}}</c> (the form created or chosen with the page) and <c>{{in14days}}</c> (countdown end).
/// </summary>
public static class TemplateCatalog
{
    public const string FormPlaceholder = "{{form}}";
    public const string CountdownPlaceholder = "{{in14days}}";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static object Block(string id, string type, object props) => new { id, type, props };

    private static FormFieldDef Field(string key, string type, string label, bool required = false, string? placeholder = null,
        string? width = null, List<FormOption>? options = null, FieldCondition? showIf = null, string? help = null, string? urlParam = null) => new()
    {
        Key = key, Type = type, Label = label, Required = required, Placeholder = placeholder, Width = width, Options = options, ShowIf = showIf,
        HelpText = help, UrlParam = urlParam,
    };

    private static List<FormOption> Options(params string[] labels) =>
        labels.Select(l => new FormOption { Value = new string(l.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'), Label = l }).ToList();

    private static IEnumerable<FormFieldDef> Utm() => new[]
    {
        Field("utm_source", "hidden", "UTM source", urlParam: "utm_source"),
        Field("utm_medium", "hidden", "UTM medium", urlParam: "utm_medium"),
        Field("utm_campaign", "hidden", "UTM campaign", urlParam: "utm_campaign"),
    };

    private const string StandardConsent =
        "I agree to be contacted about my enquiry and accept the privacy policy. You can unsubscribe or ask us to delete your data at any time.";

    public static IReadOnlyList<FormTemplate> Forms { get; } = new List<FormTemplate>
    {
        FormTemplateOf("contact", "Contact us", "Name, email, phone, topic and message with consent — the everyday enquiry form.",
            new FormSchema
            {
                Steps =
                {
                    new FormStep
                    {
                        Id = "details",
                        Fields = new List<FormFieldDef>
                        {
                            Field("name", "text", "Full name", true, "Jane Smith", "half"),
                            Field("email", "email", "Work email", true, "jane@company.com", "half"),
                            Field("phone", "phone", "Phone", false, "+1 555 010 0100", "half"),
                            Field("company", "text", "Company", false, null, "half"),
                            Field("topic", "select", "How can we help?", true, options: Options("Sales enquiry", "Support", "Partnerships", "Press", "Something else")),
                            Field("topic_other", "text", "Tell us the topic", true, showIf: new FieldCondition { Field = "topic", Operator = "equals", Value = "something-else" }),
                            Field("message", "textarea", "Message", true, "A few lines about what you need…"),
                            Field("consent", "consent", "Consent", true),
                        }.Concat(Utm()).ToList(),
                    },
                },
            },
            "Send message", "Thanks for getting in touch — a member of our team will reply within one business day.",
            "Thanks for contacting us, {{name}}",
            "Hi {{name}},\n\nThank you for reaching out. We have received your message and a member of our team will get back to you within one business day.\n\nIf your request is urgent, simply reply to this email.\n\nBest regards,\nThe team", 10),

        FormTemplateOf("quote", "Request a quote", "Two-step quote request: project needs first, then contact details.",
            new FormSchema
            {
                Steps =
                {
                    new FormStep
                    {
                        Id = "project", Title = "Your project", Description = "Tell us what you need so we can prepare an accurate quote.",
                        Fields = new List<FormFieldDef>
                        {
                            Field("services", "multiselect", "Services you are interested in", true,
                                options: Options("SEO", "Paid advertising", "Social media", "Website design", "Email marketing", "Content")),
                            Field("budget", "radio", "Monthly budget", true, options: Options("Under $2,000", "$2,000–$5,000", "$5,000–$15,000", "Over $15,000")),
                            Field("timeline", "select", "When would you like to start?", true, options: Options("As soon as possible", "Within a month", "In 1–3 months", "Just exploring")),
                            Field("website", "text", "Current website", false, "https://"),
                            Field("brief", "file", "Brief or RFP (optional)", false, help: "PDF or image, up to 5 MB."),
                        },
                    },
                    new FormStep
                    {
                        Id = "contact", Title = "Your details",
                        Fields = new List<FormFieldDef>
                        {
                            Field("name", "text", "Full name", true, width: "half"),
                            Field("email", "email", "Work email", true, width: "half"),
                            Field("phone", "phone", "Phone", true, width: "half"),
                            Field("company", "text", "Company", true, width: "half"),
                            Field("consent", "consent", "Consent", true),
                        }.Concat(Utm()).ToList(),
                    },
                },
            },
            "Get my quote", "Thank you! Your strategist will send a tailored proposal within two business days.",
            "Your quote request is in", "Hi {{name}},\n\nThanks for requesting a quote. A strategist is reviewing your requirements and will send a tailored proposal within two business days.\n\nBest regards,\nThe team", 20),

        FormTemplateOf("newsletter", "Newsletter sign-up", "Single-field email capture with optional first name and consent.",
            new FormSchema
            {
                Steps =
                {
                    new FormStep
                    {
                        Id = "subscribe",
                        Fields = new List<FormFieldDef>
                        {
                            Field("first_name", "text", "First name", false, "Jane", "half"),
                            Field("email", "email", "Email address", true, "you@example.com", "half"),
                            Field("consent", "consent", "Consent", true),
                        }.Concat(Utm()).ToList(),
                    },
                },
            },
            "Subscribe", "You're on the list! Look out for our next issue in your inbox.",
            "Welcome aboard", "Hi {{first_name}},\n\nThanks for subscribing. Every two weeks you'll get practical, no-fluff marketing ideas you can put to work straight away.\n\nSee you in your inbox,\nThe team", 30,
            "I agree to receive the newsletter and accept the privacy policy. Unsubscribe at any time with one click."),

        FormTemplateOf("webinar-registration", "Webinar registration", "Registration with role, company size and questions for the speakers.",
            new FormSchema
            {
                Steps =
                {
                    new FormStep
                    {
                        Id = "register",
                        Fields = new List<FormFieldDef>
                        {
                            Field("name", "text", "Full name", true, width: "half"),
                            Field("email", "email", "Work email", true, width: "half"),
                            Field("job_title", "text", "Job title", false, width: "half"),
                            Field("company_size", "select", "Company size", true, width: "half", options: Options("1–10", "11–50", "51–200", "201–1,000", "1,000+")),
                            Field("attend_live", "checkbox", "I plan to attend live (otherwise we'll send the recording)"),
                            Field("question", "textarea", "What would you like the speakers to cover?"),
                            Field("consent", "consent", "Consent", true),
                        }.Concat(Utm()).ToList(),
                    },
                },
            },
            "Save my seat", "You're registered! We've sent the joining link to your inbox.",
            "Your seat is confirmed", "Hi {{name}},\n\nYou're registered for the webinar. We'll email your personal joining link and a calendar invite before the session, plus the recording afterwards.\n\nSee you there,\nThe team", 40),
    };

    public static IReadOnlyList<LandingPageTemplate> Pages { get; } = new List<LandingPageTemplate>
    {
        PageTemplate("lead-generation", "Lead generation", "Lead gen",
            "Benefit-led hero, proof points, testimonials and a short enquiry form above the fold.",
            "Grow qualified leads with a proven marketing partner",
            "Book a free strategy call and get a 90-day growth plan built around your goals.", "contact", 10,
            Block("hero", "hero", new { headline = "Turn your website into your best salesperson", subheadline = "We plan, build and optimize campaigns that bring in qualified leads every week — measured, transparent and tied to revenue.", ctaLabel = "Book a free strategy call", ctaHref = "#get-started", align = "left", theme = "brand" }),
            Block("proof", "features", new { heading = "Why growing brands choose us", items = new[]
            {
                new { title = "Revenue-first strategy", body = "Every campaign starts from your pipeline targets, not vanity metrics.", icon = "target" },
                new { title = "Full-funnel execution", body = "SEO, paid media, landing pages and email working as one system.", icon = "layers" },
                new { title = "Weekly reporting", body = "A live dashboard and a weekly call so you always know what's working.", icon = "bar-chart" },
            } }),
            Block("reviews", "testimonials", new { heading = "Results our clients talk about", items = new[]
            {
                new { quote = "Qualified demo requests doubled in the first quarter, and our cost per lead fell by a third.", author = "Head of Marketing", role = "B2B SaaS company", rating = 5 },
                new { quote = "They feel like part of our team — proactive, honest and obsessed with the numbers.", author = "Founder", role = "Retail brand", rating = 5 },
            } }),
            Block("get-started", "form", new { formId = FormPlaceholder, heading = "Get your free 90-day growth plan", description = "Tell us about your business — we'll reply within one business day." }),
            Block("faq", "faq", new { heading = "Common questions", items = new[]
            {
                new { question = "Is the strategy call really free?", answer = "Yes. It's a 30-minute working session with a senior strategist, and you keep the plan whether or not we work together." },
                new { question = "How soon will we see results?", answer = "Paid campaigns typically produce leads within weeks; SEO compounds over three to six months. We set milestones for both." },
            } })),

        PageTemplate("webinar", "Webinar registration", "Events",
            "Session details, speaker credibility, agenda and a registration form with a countdown to the live date.",
            "Free live webinar — reserve your seat", "Join our experts live and leave with an actionable playbook.", "webinar-registration", 20,
            Block("hero", "hero", new { headline = "The 2026 Performance Marketing Playbook", subheadline = "A free 45-minute live session on the channels, budgets and creative that are driving growth this year.", ctaLabel = "Save my seat", ctaHref = "#register", align = "center", theme = "dark" }),
            Block("countdown", "countdown", new { heading = "We go live in", endsAt = CountdownPlaceholder, expiredText = "This session has started — register to get the recording." }),
            Block("agenda", "features", new { heading = "What you'll learn", items = new[]
            {
                new { title = "Where to spend your next dollar", body = "Channel benchmarks from hundreds of campaigns.", icon = "coins" },
                new { title = "Creative that converts", body = "The ad and landing page patterns winning right now.", icon = "sparkles" },
                new { title = "Measurement you can trust", body = "Attribution that survives privacy changes.", icon = "gauge" },
            } }),
            Block("register", "form", new { formId = FormPlaceholder, heading = "Register for free", description = "Can't make it live? Register anyway and we'll send you the recording." }),
            Block("faq", "faq", new { heading = "Webinar FAQ", items = new[]
            {
                new { question = "Will there be a recording?", answer = "Yes — every registrant receives the recording and slides within 24 hours." },
                new { question = "Can I ask questions?", answer = "Absolutely. The last 15 minutes are open Q&A with the speakers." },
            } })),

        PageTemplate("product-launch", "Product launch", "Launch",
            "Launch announcement with features, pricing tiers and an early-access form.",
            "Introducing our newest product", "Be first to try it — early-access pricing for a limited time.", "newsletter", 30,
            Block("hero", "hero", new { headline = "Meet the smarter way to run your day", subheadline = "Everything you need in one beautifully simple app. Launching this month — join early access for founding-member pricing.", ctaLabel = "Get early access", ctaHref = "#early-access", align = "center", theme = "brand" }),
            Block("features", "features", new { heading = "Built for how you actually work", items = new[]
            {
                new { title = "Set up in minutes", body = "Import your data and start in under five minutes.", icon = "rocket" },
                new { title = "Works everywhere", body = "Desktop, phone and tablet, always in sync.", icon = "smartphone" },
                new { title = "Private by design", body = "Your data is encrypted and never sold.", icon = "shield-check" },
            } }),
            Block("pricing", "pricing", new { heading = "Founding-member pricing", footnote = "Prices lock in for as long as you stay subscribed.", plans = new object[]
            {
                new { name = "Starter", price = "$9", period = "per month", description = "For individuals getting organized.", features = new[] { "All core features", "Mobile and desktop apps", "Email support" }, ctaLabel = "Join early access", ctaHref = "#early-access", highlighted = false },
                new { name = "Pro", price = "$19", period = "per month", description = "For power users and small teams.", features = new[] { "Everything in Starter", "Automations", "Priority support" }, ctaLabel = "Join early access", ctaHref = "#early-access", highlighted = true },
            } }),
            Block("early-access", "form", new { formId = FormPlaceholder, heading = "Join early access", description = "We'll email your invite the moment your spot opens." })),

        PageTemplate("free-audit", "Free audit offer", "Lead gen",
            "Offer a free SEO/marketing audit: what's included, a quote form and trust-building FAQs.",
            "Get a free website audit", "Find out what's holding your website back — free, in 48 hours.", "quote", 40,
            Block("hero", "hero", new { headline = "Get a free website & SEO audit in 48 hours", subheadline = "We review your technical SEO, content, speed and conversion paths, then walk you through the fixes that will move the needle.", ctaLabel = "Claim my free audit", ctaHref = "#audit", align = "left", theme = "light" }),
            Block("included", "features", new { heading = "What's included", items = new[]
            {
                new { title = "Technical SEO health check", body = "Crawl errors, indexing, speed and Core Web Vitals.", icon = "search" },
                new { title = "Content & keyword gaps", body = "The searches your competitors win that you don't.", icon = "file-text" },
                new { title = "Conversion review", body = "Friction points on your key landing pages.", icon = "mouse-pointer-click" },
                new { title = "Prioritized action plan", body = "Quick wins first, with effort and impact estimates.", icon = "list-checks" },
            } }),
            Block("audit", "form", new { formId = FormPlaceholder, heading = "Request your free audit", description = "Two quick steps — it takes under a minute." }),
            Block("cta", "cta", new { heading = "Prefer to talk it through?", body = "Our strategists are happy to answer questions before you request an audit.", buttonLabel = "Email the team", buttonHref = "mailto:hello@example.com", style = "secondary" })),

        PageTemplate("ebook-download", "Ebook download", "Content",
            "Gated content page: what readers will learn, social proof and a short download form.",
            "Free ebook — download now", "The practical guide our clients use to plan a year of growth.", "newsletter", 50,
            Block("hero", "hero", new { headline = "The Growth Marketing Handbook", subheadline = "60 pages of frameworks, templates and real examples to plan, launch and measure campaigns that pay back.", ctaLabel = "Download the free ebook", ctaHref = "#download", align = "left", theme = "brand" }),
            Block("learn", "text", new { heading = "Inside the handbook", body = "How to set targets that tie marketing to revenue.\nA channel-by-channel budgeting model you can copy.\nLanding page and email templates that convert.\nThe reporting dashboard we build for every client." }),
            Block("download", "form", new { formId = FormPlaceholder, heading = "Get your free copy", description = "We'll send the download link straight to your inbox." }),
            Block("quote", "testimonials", new { items = new[] { new { quote = "The most practical marketing guide I've read this year — we used the budgeting model the same week.", author = "Marketing Director", role = "Hospitality group", rating = 5 } } })),

        PageTemplate("event", "Event registration", "Events",
            "In-person event page with a countdown, highlights, agenda FAQ and registration form.",
            "Join us in person", "An evening of talks, workshops and networking with growth leaders.", "webinar-registration", 60,
            Block("hero", "hero", new { headline = "Growth Summit — live and in person", subheadline = "Keynotes, hands-on workshops and an evening of networking with marketers who are scaling brands right now.", ctaLabel = "Register now", ctaHref = "#register", align = "center", theme = "dark" }),
            Block("countdown", "countdown", new { heading = "Doors open in", endsAt = CountdownPlaceholder, expiredText = "Registration is closed — see you next year!" }),
            Block("highlights", "features", new { heading = "Event highlights", items = new[]
            {
                new { title = "Keynotes", body = "Lessons from brands that doubled revenue in 12 months.", icon = "mic" },
                new { title = "Workshops", body = "Small-group sessions on SEO, paid social and CRO.", icon = "users" },
                new { title = "Networking", body = "Meet peers, partners and the speakers over dinner.", icon = "handshake" },
            } }),
            Block("register", "form", new { formId = FormPlaceholder, heading = "Reserve your place", description = "Places are limited; registration closes when the room is full." }),
            Block("faq", "faq", new { heading = "Good to know", items = new[]
            {
                new { question = "Is there a cost?", answer = "The summit is free for invited guests and client teams." },
                new { question = "Can I bring a colleague?", answer = "Yes — ask them to register separately so we can prepare their badge." },
            } })),
    };

    private static FormTemplate FormTemplateOf(string key, string name, string description, FormSchema schema, string submit, string success,
        string autoSubject, string autoBody, int order, string? consent = null) => new()
    {
        Key = key, Name = name, Description = description, SchemaJson = FormSchemas.Serialize(schema), SubmitLabel = submit,
        SuccessMessage = success, ConsentText = consent ?? StandardConsent, AutoresponderSubject = autoSubject, AutoresponderBody = autoBody, SortOrder = order,
    };

    private static LandingPageTemplate PageTemplate(string key, string name, string category, string description, string metaTitle,
        string metaDescription, string formTemplate, int order, params object[] blocks) => new()
    {
        Key = key, Name = name, Category = category, Description = description, MetaTitle = metaTitle, MetaDescription = metaDescription,
        FormTemplateKey = formTemplate, SortOrder = order, BlocksJson = JsonSerializer.Serialize(blocks, Json),
    };
}
