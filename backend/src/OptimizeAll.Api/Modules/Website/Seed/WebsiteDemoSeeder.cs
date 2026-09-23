using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Seed;

/// <summary>
/// "Demo" website content for staging: team, testimonials, case studies (sample clients, metrics labelled measured or
/// estimated), blog posts in every workflow state, open jobs with applications, inquiries, consultations and newsletter
/// subscribers. STAGING / DEMO DATA ONLY. Idempotent via the <see cref="MarkerKey"/> setting.
/// </summary>
public sealed class WebsiteDemoSeeder(TimeProvider clock, IDatabaseDialect dialect, ILogger<WebsiteDemoSeeder> logger) : ISeeder
{
    public const string MarkerKey = "website.demo_seeded";

    public string Profile => "Demo";
    public int Order => 300;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<SystemSetting>().AnyAsync(s => s.Key == MarkerKey, ct)) return;
        var services = await db.Set<AgencyService>().ToDictionaryAsync(s => s.Slug, s => s.Id, ct);
        var industries = await db.Set<Industry>().ToDictionaryAsync(i => i.Slug, i => i.Id, ct);
        var categories = await db.Set<BlogCategory>().ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
        if (services.Count == 0)
        {
            logger.LogWarning("Website demo seed skipped: run the Baseline profile first");
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        List<Guid> S(params string[] slugs) => slugs.Where(services.ContainsKey).Select(s => services[s]).ToList();

        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);

        // ---- Team
        var team = new[]
        {
            Member("amira-hassan", "Amira Hassan", "Managing Director", "Amira founded Optimize All's agency practice after a decade leading growth teams at e-commerce and SaaS companies.", new[] { "Growth strategy", "Marketing leadership" }, 10),
            Member("daniel-okafor", "Daniel Okafor", "Head of Search", "Daniel has led SEO and paid search programmes for brands in retail, travel and finance across 20+ markets.", new[] { "SEO", "Google Ads", "Analytics" }, 20),
            Member("lena-vogel", "Lena Vogel", "Head of Content", "Lena runs our editorial and video teams, turning subject-matter expertise into content people actually read.", new[] { "Content strategy", "Copywriting", "Video" }, 30),
            Member("rafael-costa", "Rafael Costa", "Paid Social Lead", "Rafael manages paid social budgets across Meta, TikTok and LinkedIn with an obsession for creative testing.", new[] { "Meta Ads", "TikTok Ads", "Creative testing" }, 40),
            Member("priya-nair", "Priya Nair", "Creator Partnerships Manager", "Priya oversees our creator network and the review team that checks every influencer post.", new[] { "Influencer marketing", "UGC", "Compliance" }, 50),
            Member("tom-becker", "Tom Becker", "Lead Web Developer", "Tom builds fast, accessible websites and keeps our clients' tracking and integrations in shape.", new[] { "Web development", "Technical SEO", "Accessibility" }, 60),
        };
        db.AddRange(team);

        // ---- Testimonials
        db.AddRange(
            Quote("Within six months organic leads overtook paid search as our biggest channel. The monthly reports are the clearest we've ever had.", "Sofia Martins", "Marketing Director", "Northwind Outdoors", 5, "seo", 10),
            Quote("They rebuilt our Meta account from scratch and our cost per purchase is lower than it's been in two years.", "James Whitfield", "Founder", "Loom & Ladder", 5, "meta-ads", 20),
            Quote("The creator campaigns felt genuinely authentic, and knowing every post was reviewed and disclosed gave our legal team peace of mind.", "Hana Kobayashi", "Brand Manager", "Kinfolk Coffee Co.", 5, "influencer-ugc-marketing", 30),
            Quote("Our new website launched on time, loads in under two seconds and converts far better than the old one.", "Marcus Reed", "COO", "Brightpath Academy", 5, "web-design-development", 40),
            Quote("Finally an agency that tells us what isn't working as clearly as what is.", "Elena Petrova", "Head of Growth", "Ledgerly", 4, "marketing-strategy-consulting", 50),
            Quote("Our Google rating went from 3.9 to 4.7 in a year and the phone hasn't stopped ringing.", "Omar Siddiqui", "Owner", "Harbour Dental Studio", 5, "online-reputation-management", 60));

        // ---- Case studies
        db.AddRange(
            Case("northwind-outdoors-organic-growth", "Tripling organic revenue for an outdoor retailer", "Northwind Outdoors", "ecommerce",
                "A technical overhaul, category content and digital PR tripled organic revenue in 12 months.", S("seo", "technical-seo", "digital-pr-outreach"),
                "Northwind's catalogue had grown to 4,000 products, but thousands of faceted URLs were diluting crawl budget and category pages ranked on page two.",
                "We prioritised technical fixes that unlocked crawling, rewrote the 40 highest-value category pages around search intent and launched a data-led PR campaign on hiking safety.",
                "Over 12 months we shipped 180 technical fixes, 40 category rewrites and 3 PR campaigns that earned coverage in national outdoor and lifestyle media.",
                new[] { M("Organic revenue", "+212%", MetricMeasurement.Measured, "12 months vs. prior year, GA4"), M("Non-branded top-3 keywords", "+340", MetricMeasurement.Measured, "Semrush"), M("Referring domains", "+128", MetricMeasurement.Measured), M("Annual revenue from organic", "$1.4M", MetricMeasurement.Estimated, "Projected from last 90 days") },
                "Within six months organic leads overtook paid search as our biggest channel.", "Sofia Martins", "Marketing Director", true, 10, now.AddDays(-80)),
            Case("loom-ladder-meta-ads", "Cutting cost per purchase by 38% on Meta", "Loom & Ladder", "ecommerce",
                "A simplified account structure, Conversions API and weekly creative tests brought Meta back to profit.", S("meta-ads", "social-media-advertising", "influencer-ugc-marketing"),
                "After iOS privacy changes, Loom & Ladder's Meta campaigns had fragmented into 60 ad sets that never exited learning, and reported ROAS no longer matched Shopify.",
                "We implemented the Conversions API, consolidated into three campaigns, and introduced a weekly creative-testing cadence using UGC from our creator network.",
                "Across 16 weeks we launched 120 creatives, retired losing angles fast and scaled the winners with Advantage+ shopping campaigns.",
                new[] { M("Cost per purchase", "−38%", MetricMeasurement.Measured, "Meta Ads Manager, 16 weeks"), M("Blended ROAS", "3.4×", MetricMeasurement.Measured, "Shopify revenue ÷ Meta spend"), M("Creative win rate", "1 in 6", MetricMeasurement.Measured) },
                "They rebuilt our Meta account from scratch and our cost per purchase is lower than it's been in two years.", "James Whitfield", "Founder", true, 20, now.AddDays(-60)),
            Case("kinfolk-coffee-creator-campaign", "1,200 disclosed creator posts for a coffee launch", "Kinfolk Coffee Co.", "hospitality",
                "An always-on creator programme generated authentic, reviewed posts at a predictable cost per approved post.", S("influencer-ugc-marketing", "social-media-management"),
                "Kinfolk wanted to launch a new cold-brew range with authentic social proof, without the risk of fake followers or undisclosed ads.",
                "We ran a campaign through our creator network: established accounts only, a clear content kit and mandatory paid-partnership disclosure.",
                "Every submitted post was checked by our review team before it was paid; top-performing posts were licensed for paid social.",
                new[] { M("Approved creator posts", "1,200", MetricMeasurement.Measured, "Human-reviewed"), M("Estimated reach", "2.1M", MetricMeasurement.Estimated, "Sum of creator follower counts; not deduplicated"), M("Engagement rate", "4.8%", MetricMeasurement.Measured, "Across approved posts") },
                "The creator campaigns felt genuinely authentic.", "Hana Kobayashi", "Brand Manager", true, 30, now.AddDays(-40)),
            Case("brightpath-academy-website", "A new website that doubled enquiries", "Brightpath Academy", "education",
                "A research-led redesign, accessible build and CRO programme doubled course enquiries.", S("web-design-development", "landing-pages-cro", "analytics-tracking-setup"),
                "Brightpath's old website was slow, hard to update and buried course information behind PDFs.",
                "We interviewed students and admissions staff, restructured content around programmes and intake dates, and designed a fast, accessible site.",
                "The new site launched in 10 weeks with redirects, tracking and a CRO backlog; we've since run 14 tests on enquiry forms and programme pages.",
                new[] { M("Course enquiries", "+104%", MetricMeasurement.Measured, "90 days post-launch vs. prior 90"), M("Mobile page load", "1.8 s", MetricMeasurement.Measured, "Largest Contentful Paint, field data"), M("Enquiry form completion", "+31%", MetricMeasurement.Measured) },
                "Our new website launched on time and converts far better than the old one.", "Marcus Reed", "COO", false, 40, now.AddDays(-25)),
            Case("harbour-dental-local-seo", "From 3.9 to 4.7 stars for a local dental studio", "Harbour Dental Studio", "healthcare",
                "Local SEO and a review programme made Harbour Dental the top map result in its area.", S("local-seo-google-business-profile", "online-reputation-management"),
                "Harbour Dental had excellent patient care but few online reviews and an incomplete Google Business Profile.",
                "We optimised the profile, cleaned up 60 inconsistent citations and set up an automated, compliant review request after appointments.",
                "Staff were trained to respond to reviews within 24 hours; monthly GBP posts highlight new treatments.",
                new[] { M("Google rating", "4.7★", MetricMeasurement.Measured, "From 3.9★"), M("New reviews", "+310", MetricMeasurement.Measured, "12 months"), M("Additional bookings", "+25%", MetricMeasurement.Estimated, "Based on tracked calls and practice data") },
                "Our Google rating went from 3.9 to 4.7 in a year.", "Omar Siddiqui", "Owner", false, 50, now.AddDays(-10)));

        await db.SaveChangesAsync(ct);

        // ---- Blog
        var author = team.ToDictionary(m => m.Slug, m => m.Id);
        db.AddRange(
            Post("google-business-profile-checklist", "The 2026 Google Business Profile checklist", "Twelve settings most local businesses get wrong — and how to fix them in an afternoon.",
                Body("Google Business Profile", "Local search", "Most local businesses claim their profile and never look at it again. Here's how to turn it into your best-performing channel."),
                author["daniel-okafor"], new[] { categories["seo"] }, new[] { "local seo", "google business profile" }, BlogPostStatus.Published, now.AddDays(-30)),
            Post("meta-ads-creative-testing", "A simple creative-testing system for Meta ads", "How we test ten new ad concepts a week without wasting budget.",
                Body("Creative testing", "Meta ads", "Creative is the biggest lever left in Meta advertising. A structured testing system finds winners faster and stops fatigue."),
                author["rafael-costa"], new[] { categories["paid-media"] }, new[] { "meta ads", "creative" }, BlogPostStatus.Published, now.AddDays(-21)),
            Post("influencer-disclosure-rules", "Influencer disclosure: what brands must get right", "Paid-partnership labels, #ad and the rules that keep creator campaigns trustworthy.",
                Body("Disclosure", "Creator marketing", "Undisclosed sponsored posts damage trust and can breach advertising law. Here's how to run creator campaigns the right way."),
                author["priya-nair"], new[] { categories["social-media"] }, new[] { "influencer marketing", "compliance" }, BlogPostStatus.Published, now.AddDays(-14)),
            Post("ga4-consent-mode-guide", "GA4 and consent mode, explained", "What changes when visitors decline cookies, and how to keep your reporting useful.",
                Body("Consent mode", "Analytics", "Respecting cookie choices and measuring marketing aren't in conflict. Consent mode lets you do both."),
                author["tom-becker"], new[] { categories["analytics"] }, new[] { "ga4", "privacy" }, BlogPostStatus.Published, now.AddDays(-7)),
            Post("email-flows-every-store-needs", "Five email flows every online store needs", "Welcome, browse abandonment, cart abandonment, post-purchase and win-back.",
                Body("Email flows", "E-commerce", "Automated flows earn money every day without extra work. Start with these five."),
                author["lena-vogel"], new[] { categories["email"] }, new[] { "email", "ecommerce" }, BlogPostStatus.Scheduled, null, now.AddDays(3)),
            Post("tiktok-for-b2b", "Does TikTok work for B2B?", "Draft: when TikTok makes sense for business audiences.",
                Body("TikTok", "B2B", "It depends on your audience — and your willingness to be human on camera."),
                author["rafael-costa"], new[] { categories["paid-media"] }, new[] { "tiktok", "b2b" }, BlogPostStatus.InReview, null),
            Post("content-audit-template", "How to run a content audit", "Draft: a step-by-step content audit process.",
                Body("Content audit", "Content", "Before creating more content, find out what you already have and what it's doing."),
                author["lena-vogel"], new[] { categories["content"] }, new[] { "content" }, BlogPostStatus.Draft, null));

        // ---- Careers
        var seoJob = Job("seo-specialist", "SEO Specialist", "Search", "Remote (EMEA)", WorkplaceType.Remote, null, 45000, 60000, "EUR",
            "Own technical and content SEO for a portfolio of e-commerce and SaaS clients.", now.AddDays(-20));
        var socialJob = Job("paid-social-manager", "Paid Social Manager", "Paid Media", "London, UK", WorkplaceType.Hybrid, "GB", 50000, 65000, "GBP",
            "Plan, launch and optimise Meta, TikTok and LinkedIn campaigns with a creative-testing mindset.", now.AddDays(-12));
        var writerJob = Job("content-writer", "Content Writer (Freelance)", "Content", "Remote", WorkplaceType.Remote, null, null, null, null,
            "Write expert, SEO-led articles for B2B and e-commerce clients.", now.AddDays(-5), EmploymentType.Contract);
        db.AddRange(seoJob, socialJob, writerJob);
        var cv = new CareerCvFile
        {
            FileName = "sample-cv.pdf", Content = SamplePdf, SizeBytes = SamplePdf.Length, Sha256 = Normalization.Sha256Hex(SamplePdf), CreatedAt = now.AddDays(-9),
        };
        db.Add(cv);
        db.AddRange(
            Applicant(seoJob.Id, cv.Id, "Nadia Rahman", "nadia.rahman@example.com", ApplicationStage.New, now.AddDays(-2)),
            Applicant(seoJob.Id, cv.Id, "Jonas Lindqvist", "jonas.lindqvist@example.com", ApplicationStage.Screening, now.AddDays(-6)),
            Applicant(socialJob.Id, cv.Id, "Chloe Martin", "chloe.martin@example.com", ApplicationStage.Interview, now.AddDays(-9)),
            Applicant(writerJob.Id, cv.Id, "Samuel Adeyemi", "samuel.adeyemi@example.com", ApplicationStage.Offer, now.AddDays(-4)));

        // ---- Inquiries, consultations and subscribers
        var inquiries = new[]
        {
            Inquiry(InquiryType.Audit, "Grace Liu", "grace@fernandfable.example", "Fern & Fable", "https://fernandfable.example", new[] { "seo", "google-ads-ppc" }, "3k-10k", "google", "cpc", "free-audit", now.AddDays(-1), InquiryStatus.New),
            Inquiry(InquiryType.Quote, "Mateo Garcia", "mateo@cobaltfit.example", "Cobalt Fitness", null, new[] { "social-media-management", "meta-ads" }, "1k-3k", "instagram", "social", "spring-promo", now.AddDays(-2), InquiryStatus.InProgress),
            Inquiry(InquiryType.Contact, "Aisha Bello", "aisha@lumenlegal.example", "Lumen Legal", null, new[] { "content-marketing-strategy" }, null, null, null, null, now.AddDays(-4), InquiryStatus.Qualified),
            Inquiry(InquiryType.Audit, "Oliver Hughes", "oliver@tidewater.example", "Tidewater Hotels", "https://tidewater.example", new[] { "local-seo-google-business-profile", "online-reputation-management" }, "10k-25k", "linkedin", "paid-social", "hospitality-q3", now.AddDays(-9), InquiryStatus.Converted),
            Inquiry(InquiryType.Contact, "Ben Carter", "ben@example.com", null, null, Array.Empty<string>(), null, null, null, null, now.AddDays(-12), InquiryStatus.Closed),
        };
        db.AddRange(inquiries);
        var consult = Inquiry(InquiryType.Consultation, "Yuki Tanaka", "yuki@sakuraskin.example", "Sakura Skin", "https://sakuraskin.example", new[] { "influencer-ugc-marketing" }, null, "newsletter", "email", "sept-newsletter", now.AddDays(-1), InquiryStatus.New);
        db.Add(consult);
        var slot = NextWeekday(now.AddDays(2)).Date.AddHours(10);
        db.Add(new ConsultationBooking
        {
            SlotStart = slot, SlotEnd = slot.AddMinutes(30), SlotKey = ConsultationBooking.KeyFor(slot), Name = consult.Name, Email = consult.Email, Company = consult.Company,
            Website = consult.Website, ServiceSlugs = consult.ServiceSlugs, VisitorTimeZone = "Asia/Tokyo", InquiryId = consult.Id,
            Notes = "Launching a new serum range in Q4 and interested in creator campaigns.",
        });
        foreach (var (email, status, days, source) in new[]
        {
            ("reader1@example.com", NewsletterStatus.Confirmed, 40, "footer"), ("reader2@example.com", NewsletterStatus.Confirmed, 33, "blog"),
            ("reader3@example.com", NewsletterStatus.Confirmed, 20, "home"), ("reader4@example.com", NewsletterStatus.Pending, 1, "footer"),
            ("reader5@example.com", NewsletterStatus.Unsubscribed, 60, "blog"), ("reader6@example.com", NewsletterStatus.Confirmed, 5, "blog"),
        })
        {
            var sub = new NewsletterSubscriber
            {
                Email = email, NormalizedEmail = email, Status = status, ConsentVersion = ConsentTexts.NewsletterVersion, ConsentAt = now.AddDays(-days),
                Source = source, ConfirmedAt = status == NewsletterStatus.Pending ? null : now.AddDays(-days).AddHours(1),
                UnsubscribedAt = status == NewsletterStatus.Unsubscribed ? now.AddDays(-days + 10) : null, CreatedAt = now.AddDays(-days),
            };
            sub.UnsubscribeTokenHash = NewsletterService.Sha256("demo-unsubscribe-" + sub.Id);
            db.Add(sub);
        }

        // ---- Home stats in site settings (labelled measured/estimated)
        var settingsDoc = await db.Set<SiteSettingsDocument>().FirstOrDefaultAsync(d => d.Key == SiteSettingsDocument.DefaultKey, ct);
        if (settingsDoc is not null)
        {
            var settings = SiteSettingsService.Parse(settingsDoc.Json);
            if (settings.HomeStats.Count == 0)
            {
                settings = settings with
                {
                    HomeStats = new[]
                    {
                        new HomeStat("Client revenue influenced", "$48M", MetricMeasurement.Estimated, "Sample figure for the demo site"),
                        new HomeStat("Average client retention", "3.2 yrs", MetricMeasurement.Measured, "Sample figure for the demo site"),
                        new HomeStat("Creator posts reviewed", "25k+", MetricMeasurement.Measured, "Sample figure for the demo site"),
                        new HomeStat("Specialists on the team", "40", MetricMeasurement.Measured, null),
                    },
                    Contact = settings.Contact with { Phone = "+1 415 555 0100", Address = "100 Market Street, San Francisco, CA" },
                };
                settingsDoc.Json = JsonSerializer.Serialize(settings, SiteSettingsService.Json);
            }
        }

        db.Set<SystemSetting>().Add(new SystemSetting
        {
            Key = MarkerKey, ValueJson = JsonSerializer.Serialize(new { seededAt = now, version = 1 }),
            Description = "Marker written by the Website demo seed (staging/demo data only).", UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogWarning("Website demo seed created team, testimonials, case studies, posts, jobs, leads and subscribers. Demo/staging data only.");

        // ---- local builders
        TeamMember Member(string slug, string name, string role, string bio, string[] expertise, int sort) => new()
        {
            Slug = slug, Name = name, Role = role, Bio = bio, Expertise = expertise.ToList(), IsPublished = true, SortOrder = sort,
            SocialLinks = new List<SiteLink> { new("LinkedIn", $"https://www.linkedin.com/in/{slug}-demo") },
        };
        Testimonial Quote(string quote, string authorName, string role, string company, int rating, string service, int sort) => new()
        {
            Quote = quote, AuthorName = authorName, AuthorRole = role, Company = company, Rating = rating,
            ServiceId = services.TryGetValue(service, out var sid) ? sid : null, IsPublished = true, IsFeatured = sort <= 30, SortOrder = sort,
        };
        CaseStudy Case(string slug, string title, string client, string industry, string summary, List<Guid> serviceIds, string challenge, string strategy,
            string execution, ResultMetric[] metrics, string quote, string quoteAuthor, string quoteRole, bool featured, int sort, DateTime published) => new()
        {
            Slug = slug, Title = title, ClientName = client, Summary = summary, IndustryId = industries.TryGetValue(industry, out var iid) ? iid : null,
            ServiceIds = serviceIds, ChallengeMarkdown = challenge, StrategyMarkdown = strategy, ExecutionMarkdown = execution, Metrics = metrics.ToList(),
            TestimonialQuote = quote, TestimonialAuthor = quoteAuthor, TestimonialRole = quoteRole, IsPublished = true, IsFeatured = featured,
            PublishedAt = published, SortOrder = sort, Seo = new SeoMeta { Description = summary },
        };
        BlogPost Post(string slug, string title, string excerpt, string body, Guid authorId, Guid[] cats, string[] tags, BlogPostStatus status,
            DateTime? publishedAt, DateTime? publishAt = null) => new()
        {
            Slug = slug, Title = title, Excerpt = excerpt, BodyMarkdown = body, AuthorId = authorId, CategoryIds = cats.ToList(), Tags = tags.ToList(),
            ReadingMinutes = MarkdownSanitizer.ReadingMinutes(body), Status = status, PublishedAt = publishedAt, PublishAt = publishAt,
            CreatedAt = (publishedAt ?? now).AddDays(-2), Seo = new SeoMeta { Description = excerpt },
        };
        JobOpening Job(string slug, string title, string dept, string location, WorkplaceType workplace, string? country, decimal? min, decimal? max,
            string? currency, string summary, DateTime posted, EmploymentType type = EmploymentType.FullTime) => new()
        {
            Slug = slug, Title = title, Department = dept, Location = location, Workplace = workplace, CountryCode = country, EmploymentType = type,
            Summary = summary, Status = JobOpeningStatus.Open, PostedAt = posted, SalaryMin = min, SalaryMax = max, SalaryCurrency = currency,
            SalaryPeriod = currency is null ? null : SalaryPeriod.Year,
            DescriptionMarkdown = $"## About the role\n\n{summary}\n\n## What you'll do\n\n- Work with a small, senior team on a varied client portfolio\n- Plan, execute and report on work you're proud of\n- Share what you learn with the rest of the agency\n\n## How we work\n\nFlexible hours, a written-first culture and a real budget for learning.",
            Requirements = new List<string> { "2+ years of relevant agency or in-house experience", "Clear written English", "Comfort with data and reporting" },
            Benefits = new List<string> { "Flexible and remote-friendly working", "Annual learning budget", "25 days' paid leave plus public holidays" },
        };
        JobApplication Applicant(Guid jobId, Guid cvId, string name, string email, ApplicationStage stage, DateTime at) => new()
        {
            JobOpeningId = jobId, CvFileId = cvId, Name = name, Email = email, Stage = stage, ConsentAt = at, ConsentVersion = ConsentTexts.CareersVersion,
            CoverLetter = "I've followed your work for a while and would love to bring my experience to the team.", CreatedAt = at,
        };
        WebsiteInquiry Inquiry(InquiryType type, string name, string email, string? company, string? website, string[] slugs, string? budget,
            string? utmSource, string? utmMedium, string? utmCampaign, DateTime at, InquiryStatus status) => new()
        {
            Type = type, Name = name, Email = email, Company = company, Website = website, ServiceSlugs = slugs.ToList(), BudgetRange = budget,
            UtmSource = utmSource, UtmMedium = utmMedium, UtmCampaign = utmCampaign, Status = status, CreatedAt = at, ConsentAt = at,
            ConsentVersion = ConsentTexts.FormVersion, LandingPath = "/", Message = "We'd like to understand how you could help us grow over the next two quarters.",
        };
    }

    private static ResultMetric M(string label, string value, MetricMeasurement measurement, string? context = null) => new(label, value, measurement, context);

    private static DateTime NextWeekday(DateTime day)
    {
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        return day;
    }

    private static string Body(string topic, string area, string intro) =>
        $"{intro}\n\n## Why {topic.ToLowerInvariant()} matters\n\n{topic} is one of the highest-leverage activities in {area.ToLowerInvariant()}. " +
        "Small, consistent improvements compound over months, and the businesses that win are usually the ones that simply do the basics well, every week.\n\n" +
        "## Where most teams go wrong\n\n- They set it up once and never revisit it\n- They measure activity instead of outcomes\n- They copy competitors instead of talking to customers\n\n" +
        "## A practical approach\n\n1. **Audit** what you have today and note the gaps.\n2. **Prioritise** the changes most likely to move revenue.\n3. **Ship** a small batch of improvements every week.\n4. **Measure** the effect and keep what works.\n\n" +
        "> The best time to fix the fundamentals was last year. The second-best time is this week.\n\n" +
        "## Next steps\n\nIf you'd like a second pair of eyes, [request a free marketing audit](/free-audit) and one of our specialists will review your setup.";

    /// <summary>A minimal valid one-page PDF used as the demo applicants' CV.</summary>
    private static readonly byte[] SamplePdf = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
}
