using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// "Baseline" reference data for delivery: service project templates, creative brief templates per service and report
/// templates. Idempotent: rows are matched by key and never overwritten (edits made in the app are preserved).
/// </summary>
public sealed class DeliveryBaselineSeeder(ILogger<DeliveryBaselineSeeder> logger) : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 40;

    private static List<string> L(params string[] v) => v.ToList();

    private static TemplateTask T(string title, string? milestone, int offset, decimal? hours, bool clientVisible, params string[] labels) =>
        new(title, null, milestone, offset, hours, labels.ToList(), clientVisible);

    public static IReadOnlyList<ProjectTemplate> ProjectTemplates() => new[]
    {
        new ProjectTemplate
        {
            Key = "seo-monthly-retainer", Name = "SEO monthly retainer", ProjectType = ProjectType.SeoProgram, ServiceLines = L("seo", "content"),
            Description = "A month of technical SEO, on-page optimisation, content and link building, closed by the monthly report.",
            DefaultBudgetHours = 40, DurationDays = 30,
            Milestones = new() { new("technical", "Technical health check", 7), new("onpage", "On-page & content", 21), new("report", "Monthly report", 30) },
            Tasks = new()
            {
                T("Crawl the site and triage technical issues", "technical", 3, 4, true, "technical"),
                T("Fix priority technical issues (redirects, canonicals, Core Web Vitals)", "technical", 7, 6, true, "technical"),
                T("Review keyword rankings and opportunities", "onpage", 10, 3, false, "research"),
                T("Optimise 5 priority pages (titles, meta, headings, internal links)", "onpage", 18, 8, true, "on-page"),
                T("Write 2 SEO blog posts", "onpage", 21, 10, true, "content"),
                T("Link-building outreach", "onpage", 25, 5, false, "links"),
                T("Update Google Business Profile", null, 14, 1, true, "local"),
                T("Compile the monthly SEO report", "report", 30, 3, true, "report"),
            },
            Recurring = new() { new("Monthly SEO report", "Collect rankings, traffic and conversions; write insights and next month's plan.", 1, 4, 3, L("report")) },
        },
        new ProjectTemplate
        {
            Key = "social-monthly-content", Name = "Social media monthly content", ProjectType = ProjectType.SocialContent,
            ServiceLines = L("social", "design", "content"), DefaultBudgetHours = 45, DurationDays = 30,
            Description = "Plan, create, approve, schedule and report a month of organic social content.",
            Milestones = new() { new("plan", "Content calendar approved", 5), new("create", "Content set approved", 15), new("live", "Content live & reported", 30) },
            Tasks = new()
            {
                T("Draft next month's content calendar", "plan", 3, 4, true, "planning"),
                T("Client approval of the content calendar", "plan", 5, 1, true, "approval"),
                T("Design 12 feed posts and 8 stories", "create", 12, 16, true, "design"),
                T("Write captions and hashtags", "create", 12, 6, true, "copy"),
                T("Client approval of the content set", "create", 15, 1, true, "approval"),
                T("Schedule posts in the publishing tool", "live", 17, 2, false, "publishing"),
                T("Community management and replies", "live", 30, 8, false, "community"),
                T("Monthly social report", "live", 30, 3, true, "report"),
            },
            Recurring = new()
            {
                new("Content calendar for next month", null, 20, 5, 4, L("planning")),
                new("Monthly social report", null, 1, 4, 3, L("report")),
            },
        },
        new ProjectTemplate
        {
            Key = "google-ads-launch", Name = "Google Ads launch", ProjectType = ProjectType.PaidAdsLaunch, ServiceLines = L("ads", "analytics"),
            Description = "Audit, tracking, structure, creative and launch of a Google Ads account, with the first optimisation week.",
            DefaultBudgetHours = 35, DurationDays = 30,
            Milestones = new() { new("setup", "Tracking & structure ready", 10), new("launch", "Campaigns live", 18), new("optimise", "First optimisation", 30) },
            Tasks = new()
            {
                T("Account audit (or new account setup)", "setup", 3, 3, true, "setup"),
                T("Conversion tracking in GA4 and Google Tag Manager", "setup", 7, 5, true, "tracking"),
                T("Keyword research and negative keyword lists", "setup", 8, 4, false, "research"),
                T("Campaign and ad group structure", "setup", 10, 3, false, "structure"),
                T("Responsive search ads and assets (client approval)", "launch", 14, 5, true, "creative"),
                T("Landing page review", "launch", 14, 2, true, "cro"),
                T("Launch campaigns", "launch", 18, 2, true, "launch"),
                T("Week-one bid and query optimisation", "optimise", 25, 4, false, "optimisation"),
                T("Launch performance report", "optimise", 30, 3, true, "report"),
            },
        },
        new ProjectTemplate
        {
            Key = "website-build", Name = "Website build", ProjectType = ProjectType.WebsiteBuild, ServiceLines = L("web", "design", "seo", "content"),
            Description = "Discovery to launch for a marketing website, including SEO migration.", DefaultBudgetHours = 220, DurationDays = 75,
            Milestones = new()
            {
                new("discovery", "Discovery signed off", 10), new("design", "Design approved", 30), new("build", "Build complete", 60),
                new("launch", "Launched", 75),
            },
            Tasks = new()
            {
                T("Discovery workshop", "discovery", 5, 6, true, "discovery"),
                T("Sitemap and information architecture", "discovery", 10, 8, true, "ux"),
                T("Wireframes for key templates", "design", 18, 20, true, "ux"),
                T("Visual design (home + 4 templates)", "design", 30, 40, true, "design"),
                T("Copywriting for core pages", "build", 40, 24, true, "copy"),
                T("Front-end and CMS development", "build", 58, 80, true, "development"),
                T("QA across browsers and devices", "build", 60, 12, true, "qa"),
                T("SEO migration: redirects, metadata, sitemaps", "launch", 70, 10, true, "seo"),
                T("Launch and post-launch checks", "launch", 75, 6, true, "launch"),
            },
        },
        new ProjectTemplate
        {
            Key = "email-program-setup", Name = "Email program setup", ProjectType = ProjectType.EmailProgram, ServiceLines = L("email", "design", "content"),
            Description = "Deliverability, segmentation, core automated flows and a reusable newsletter template.", DefaultBudgetHours = 60, DurationDays = 45,
            Milestones = new() { new("foundation", "Foundations ready", 12), new("flows", "Core flows live", 35), new("newsletter", "First newsletter sent", 45) },
            Tasks = new()
            {
                T("ESP audit and deliverability setup (SPF, DKIM, DMARC)", "foundation", 5, 4, true, "deliverability"),
                T("List hygiene and segmentation plan", "foundation", 12, 5, true, "segmentation"),
                T("Email template design", "flows", 20, 10, true, "design"),
                T("Welcome flow (3 emails)", "flows", 28, 10, true, "automation"),
                T("Abandoned cart / browse flow", "flows", 35, 10, true, "automation"),
                T("First newsletter", "newsletter", 42, 6, true, "campaign"),
                T("Email performance baseline report", "newsletter", 45, 3, true, "report"),
            },
        },
    };

    private static BriefField F(string key, string label, BriefFieldType type, bool required, string? help = null, params string[] options) =>
        new(key, label, type, required, help, options.ToList());

    private static List<BriefField> Core(params BriefField[] extra)
    {
        var fields = new List<BriefField>
        {
            F("goal", "Goal", BriefFieldType.LongText, true, "What should this work achieve? How will we know it worked?"),
            F("audience", "Audience", BriefFieldType.LongText, true, "Who is it for? Include personas from the brand kit if relevant."),
            F("key_message", "Key message", BriefFieldType.LongText, true, "The one thing the audience should remember."),
        };
        fields.AddRange(extra);
        fields.Add(F("deadline", "Deadline", BriefFieldType.Date, false));
        fields.Add(F("references", "References", BriefFieldType.Url, false, "Links to examples you like, one per line."));
        fields.Add(F("mandatories", "Mandatories", BriefFieldType.LongText, false, "Legal lines, logos, offers, things that must (not) appear."));
        return fields;
    }

    public static IReadOnlyList<BriefTemplate> BriefTemplates() => new[]
    {
        new BriefTemplate
        {
            Key = "social-content", ServiceLine = "social", Name = "Social media content", Description = "Posts, stories, reels or a campaign set.",
            Fields = Core(F("channels", "Channels", BriefFieldType.List, true, "e.g. Instagram, TikTok, LinkedIn (one per line)"),
                F("format", "Format", BriefFieldType.Select, false, null, "Feed posts", "Stories", "Reels / short video", "Carousel", "Mixed")),
        },
        new BriefTemplate
        {
            Key = "ad-campaign", ServiceLine = "ads", Name = "Paid ads campaign", Description = "Google, Meta, TikTok or LinkedIn ads.",
            Fields = Core(F("channels", "Channels", BriefFieldType.List, true), F("budget", "Monthly budget", BriefFieldType.Text, false, "Amount and currency"),
                F("offer", "Offer / call to action", BriefFieldType.Text, false), F("landing_page", "Landing page", BriefFieldType.Url, false)),
        },
        new BriefTemplate
        {
            Key = "blog-content", ServiceLine = "content", Name = "Blog / article", Description = "SEO articles, thought leadership, guides.",
            Fields = Core(F("topic", "Topic or working title", BriefFieldType.Text, true), F("keywords", "Target keywords", BriefFieldType.List, false),
                F("length", "Length", BriefFieldType.Select, false, null, "Short (600–900 words)", "Standard (1,000–1,500 words)", "Long-form (2,000+ words)")),
        },
        new BriefTemplate
        {
            Key = "seo-page", ServiceLine = "seo", Name = "SEO landing page", Description = "A new or improved page targeting search demand.",
            Fields = Core(F("page_url", "Existing page (if any)", BriefFieldType.Url, false), F("keywords", "Target keywords", BriefFieldType.List, true)),
        },
        new BriefTemplate
        {
            Key = "email-campaign", ServiceLine = "email", Name = "Email campaign", Description = "Newsletter, promotion or automated flow email.",
            Fields = Core(F("segment", "Segment / list", BriefFieldType.Text, false), F("send_date", "Preferred send date", BriefFieldType.Date, false),
                F("offer", "Offer / call to action", BriefFieldType.Text, false)),
        },
        new BriefTemplate
        {
            Key = "design-request", ServiceLine = "design", Name = "Design request", Description = "Graphics, print, presentations, brand assets.",
            Fields = Core(F("deliverables", "What do you need?", BriefFieldType.List, true, "Formats and sizes, one per line"),
                F("channels", "Where will it be used?", BriefFieldType.List, false)),
        },
        new BriefTemplate
        {
            Key = "video", ServiceLine = "content", Name = "Video", Description = "Short-form social video, explainer or ad video.",
            Fields = Core(F("duration", "Duration", BriefFieldType.Select, true, null, "6–15 seconds", "15–30 seconds", "30–60 seconds", "1–3 minutes"),
                F("channels", "Channels", BriefFieldType.List, false), F("voiceover", "Voice-over / captions", BriefFieldType.Text, false)),
        },
    };

    public static IReadOnlyList<ReportTemplate> ReportTemplates() => new[]
    {
        new ReportTemplate
        {
            Key = ReportService.DefaultTemplateKey, Name = "Monthly performance report",
            Description = "Executive summary, KPIs, a section per channel, wins and next month's plan.",
            Sections = new()
            {
                new("summary", "summary", "Executive summary", null, "Three to five sentences: what happened, why it matters, what's next."),
                new("delivery", "kpis", "Delivery this month", "delivery", null),
                new("seo", "channel", "SEO", "seo", null),
                new("social", "channel", "Social media", "social", null),
                new("ads", "channel", "Paid advertising", "ads", null),
                new("email", "channel", "Email marketing", "email", null),
                new("content", "channel", "Content", "content", null),
                new("feedback", "kpis", "Client satisfaction", "feedback", null),
                new("wins", "wins", "Wins", null, "Highlights worth celebrating."),
                new("plan", "plan", "Next month's plan", null, "Priorities, experiments and what we need from you."),
            },
        },
        new ReportTemplate
        {
            Key = "campaign-wrap-up", Name = "Campaign wrap-up",
            Description = "One-off campaign results: objectives vs. outcomes, channel results, learnings.",
            Sections = new()
            {
                new("summary", "summary", "Campaign summary", null, "Objective, what we ran and the headline result."),
                new("ads", "channel", "Paid advertising", "ads", null),
                new("social", "channel", "Social media", "social", null),
                new("wins", "wins", "What worked", null, null),
                new("plan", "plan", "Learnings and recommendations", null, null),
            },
        },
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var added = 0;
        var projectKeys = await db.Set<ProjectTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in ProjectTemplates().Where(t => !projectKeys.Contains(t.Key))) { db.Add(t); added++; }
        var briefKeys = await db.Set<BriefTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in BriefTemplates().Where(t => !briefKeys.Contains(t.Key))) { db.Add(t); added++; }
        var reportKeys = await db.Set<ReportTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in ReportTemplates().Where(t => !reportKeys.Contains(t.Key))) { db.Add(t); added++; }
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Delivery baseline: {Count} template(s) added", added);
        }
    }
}
