using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Demo;

/// <summary>
/// The four canonical demo clients (<see cref="DeliveryDemoData.Clients"/>) shared by every agency module's demo data, looked
/// up by slug; any that are missing are created with exactly the canonical values, whichever seeder runs first.
/// </summary>
public static class DemoClients
{
    public static IReadOnlyList<DeliveryDemoData.DemoClient> All => DeliveryDemoData.Clients;

    /// <summary>Returns the canonical clients by slug, creating any that are missing with exactly the canonical values.</summary>
    public static async Task<Dictionary<string, ClientAccount>> EnsureAsync(AppDbContext db, CancellationToken ct)
    {
        var slugs = All.Select(d => d.Slug).ToList();
        var existing = await db.Set<ClientAccount>().Where(c => slugs.Contains(c.Slug)).ToDictionaryAsync(c => c.Slug, ct);
        foreach (var d in All.Where(d => !existing.ContainsKey(d.Slug)))
        {
            var client = new ClientAccount
            {
                Slug = d.Slug, Name = d.Name, Industry = d.Industry, CountryCode = d.CountryCode, Currency = d.Currency, TimeZone = d.TimeZone,
                Website = d.Website, Summary = d.Summary, Status = d.Status,
            };
            db.Add(client);
            existing[d.Slug] = client;
        }
        await db.SaveChangesAsync(ct);
        return existing;
    }
}

/// <summary>
/// Demo profile (Order 300) for the SEO toolkit, landing pages/forms and integrations: canonical clients, the SEO and
/// designer demo staff, SEO sites with two completed audits (so the diff has content), 60 days of rank history with
/// competitors, Search Console rows, backlinks and outreach, local SEO, a content brief, and published landing pages
/// (one running an A/B test) with views and submissions. Idempotent: guarded by the <see cref="MarkerKey"/> setting and
/// per-record existence checks. DEMO DATA ONLY.
/// </summary>
public sealed class AgencyToolkitDemoSeeder(IDatabaseDialect dialect, IPasswordHasher<User> passwordHasher, IPrivacyHasher hasher,
    TimeProvider clock, ILogger<AgencyToolkitDemoSeeder> logger) : ISeeder
{
    public const string MarkerKey = "demo.seo_pages.seeded";
    public const string DemoPassword = DeliveryDemoData.Password;
    public static string SeoEmail => DeliveryDemoData.Seo.Email;
    public static string DesignerEmail => DeliveryDemoData.Designer.Email;

    public string Profile => "Demo";
    public int Order => 300;

    private readonly Random _random = new(20260923);
    private DateTime _now;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        _now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var clients = await DemoClients.EnsureAsync(db, ct);
        await EnsureStaffAsync(db, DeliveryDemoData.Seo, ct);
        var designer = await EnsureStaffAsync(db, DeliveryDemoData.Designer, ct);

        if (await db.Set<SystemSetting>().AnyAsync(s => s.Key == MarkerKey, ct))
        {
            await tx.CommitAsync(ct);
            logger.LogInformation("SEO/landing-page demo data already present; skipping");
            return;
        }

        await SeedSeoAsync(db, clients["nimbus-fitness"], "Nimbus Fitness app", "nimbusfitness.app", "US", new[] { "fitbod.me", "strava.com" },
            new[] { "home workout app", "ai personal trainer", "workout planner app", "strength training app", "hiit workouts at home", "fitness app for beginners", "calorie and workout tracker", "best gym app" }, ct);
        await SeedSeoAsync(db, clients["wanderly-travel"], "Wanderly Travel", "wanderlytravel.co.uk", "GB", new[] { "lonelyplanet.com", "expedia.co.uk" },
            new[] { "city breaks europe", "tailor made holidays", "honeymoon destinations", "family holidays abroad", "weekend getaways uk", "luxury safari holidays" }, ct);
        await SeedSeoAsync(db, clients["aurora-skincare"], "Aurora Skincare store", "auroraskincare.ae", "AE", new[] { "sephora.ae", "faces.ae" },
            new[] { "vitamin c serum uae", "halal skincare", "sunscreen for oily skin", "hyaluronic acid serum dubai", "korean skincare dubai" }, ct);
        await SeedSeoAsync(db, clients["karachi-eats"], "Karachi Eats", "karachieats.pk", "PK", new[] { "foodpanda.pk" },
            new[] { "best biryani in karachi", "restaurants in dha karachi", "family restaurant karachi", "bbq karachi", "food delivery karachi" }, ct);
        await SeedLocalAsync(db, clients["karachi-eats"], ct);

        await SeedPagesAsync(db, clients["nimbus-fitness"], designer, "free-trial", "Start your free 14-day trial", "lead-generation", experiment: true, ct);
        await SeedPagesAsync(db, clients["wanderly-travel"], designer, "travel-guide", "Free 2026 travel guide", "ebook-download", experiment: false, ct);
        await SeedPagesAsync(db, clients["aurora-skincare"], designer, "summer-launch", "Summer glow collection launch", "product-launch", experiment: false, ct);
        await SeedPagesAsync(db, clients["karachi-eats"], designer, "iftar-event", "Iftar night at Karachi Eats", "event", experiment: false, ct);

        db.Add(new SystemSetting
        {
            Key = MarkerKey, ValueJson = JsonSerializer.Serialize(new { seededAt = _now, version = 1 }),
            Description = "Marker written by the SEO/landing-page demo seeder (staging/demo data only).", UpdatedAt = _now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogWarning("SEO and landing-page demo data created. Demo/staging data only.");
    }

    /// <summary>The canonical demo staff member (same email, display name, role and home as the delivery demo seeder creates).</summary>
    private async Task<User> EnsureStaffAsync(AppDbContext db, DeliveryDemoData.DemoStaff staff, CancellationToken ct)
    {
        var (email, name, role) = (staff.Email, staff.DisplayName, staff.Role);
        var normalized = Normalization.Email(email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is not null)
        {
            if (!user.HasRole(role)) user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
            await db.SaveChangesAsync(ct);
            return user;
        }
        string code;
        do code = "AG" + _random.Next(100000, 999999);
        while (await db.Set<User>().AnyAsync(u => u.ReferralCode == code, ct));
        user = new User
        {
            Email = email, NormalizedEmail = normalized, DisplayName = name, CountryCode = "PK", TimeZone = "Asia/Karachi", EmailVerifiedAt = _now,
            ReferralCode = code,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, DemoPassword);
        user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
        db.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    // ------------------------------------------------------------------------------------------------ SEO

    private async Task SeedSeoAsync(AppDbContext db, ClientAccount client, string name, string domain, string country, string[] competitors,
        string[] keywords, CancellationToken ct)
    {
        if (await db.Set<SeoSite>().AnyAsync(s => s.ClientAccountId == client.Id && s.Domain == domain, ct)) return;
        var site = new SeoSite
        {
            ClientAccountId = client.Id, Name = name, Domain = domain, Protocol = "https", SitemapUrl = $"https://{domain}/sitemap.xml",
            TargetCountry = country, TargetLanguage = "en", Competitors = competitors.ToList(),
        };
        db.Add(site);
        await db.SaveChangesAsync(ct);

        // Two completed audits: 30 days ago and 2 days ago, so the diff shows fixed and new issues.
        var older = AddAudit(db, site, _now.AddDays(-30), 38, 71, new (string, int)[]
        {
            (SeoAuditRules.Http4xx, 3), (SeoAuditRules.TitleDuplicate, 6), (SeoAuditRules.DescriptionMissing, 9), (SeoAuditRules.ImageAltMissing, 12),
            (SeoAuditRules.RedirectChain, 2), (SeoAuditRules.H1Missing, 4), (SeoAuditRules.CanonicalMissing, 10), (SeoAuditRules.OpenGraphMissing, 14),
            (SeoAuditRules.SlowResponse, 3), (SeoAuditRules.StructuredDataMissing, 18),
        });
        var latest = AddAudit(db, site, _now.AddDays(-2), 42, 84, new (string, int)[]
        {
            (SeoAuditRules.Http4xx, 1), (SeoAuditRules.DescriptionMissing, 3), (SeoAuditRules.ImageAltMissing, 7), (SeoAuditRules.ThinContent, 4),
            (SeoAuditRules.CanonicalMissing, 6), (SeoAuditRules.OpenGraphMissing, 5), (SeoAuditRules.StructuredDataMissing, 11),
            (SeoAuditRules.TitleTooLong, 3),
        });
        _ = older;
        _ = latest;

        // Keywords with 60 days of history (own domain + competitors).
        var own = RankMath.BareHost(domain);
        var index = 0;
        foreach (var text in keywords)
        {
            index++;
            var keyword = new SeoKeyword
            {
                SiteId = site.Id, ClientAccountId = client.Id, Keyword = text, NormalizedKeyword = SeoText.NormalizeKeyword(text),
                Intent = index % 3 == 0 ? KeywordIntent.Transactional : index % 2 == 0 ? KeywordIntent.Commercial : KeywordIntent.Informational,
                SearchVolume = 300 + _random.Next(0, 40) * 150, Difficulty = 20 + _random.Next(0, 60), Tags = new List<string> { index <= 3 ? "priority" : "growth" },
                TargetUrl = $"https://{domain}/{SeoText.NormalizeKeyword(text).Replace(' ', '-')}",
            };
            db.Add(keyword);
            var start = 8 + _random.Next(0, 30);
            var end = Math.Max(1, start - 4 - _random.Next(0, 12));
            for (var day = 60; day >= 0; day -= 1)
            {
                var date = DateOnly.FromDateTime(_now.AddDays(-day));
                var progress = (60 - day) / 60.0;
                var position = (int)Math.Round(start + (end - start) * progress + _random.Next(-1, 2));
                position = Math.Clamp(position, 1, 100);
                var features = new List<string>();
                if (index == 1 && day < 20) features.Add("featured_snippet");
                if (index % 2 == 0) features.Add("people_also_ask");
                if (index == 3) features.Add("video");
                db.Add(new SeoRankSnapshot
                {
                    KeywordId = keyword.Id, SiteId = site.Id, ClientAccountId = client.Id, Date = date, Domain = own, Position = position,
                    Url = keyword.TargetUrl, SerpFeatures = features, Source = RankSource.DataForSeo, RecordedAt = _now,
                });
                if (day % 7 == 0)
                {
                    foreach (var competitor in competitors)
                    {
                        var cp = Math.Clamp(3 + _random.Next(0, 15), 1, 100);
                        db.Add(new SeoRankSnapshot
                        {
                            KeywordId = keyword.Id, SiteId = site.Id, ClientAccountId = client.Id, Date = date, Domain = competitor, Position = cp,
                            Source = RankSource.DataForSeo, RecordedAt = _now,
                        });
                    }
                }
            }
            // Search Console rows for the last 28 days.
            for (var day = 28; day >= 1; day--)
            {
                var impressions = 40 + _random.Next(0, 160) + (28 - day) * 3;
                var clicks = Math.Max(0, (int)(impressions * (0.02 + _random.NextDouble() * 0.08)));
                var page = keyword.TargetUrl!;
                db.Add(new SeoSearchPerformance
                {
                    SiteId = site.Id, ClientAccountId = client.Id, Date = DateOnly.FromDateTime(_now.AddDays(-day)), Query = text, Page = page,
                    RowHash = Normalization.Sha256Hex(text + "\n" + page), Clicks = clicks, Impressions = impressions,
                    Ctr = Math.Round((double)clicks / impressions, 4), Position = Math.Round(end + _random.NextDouble() * 3, 1), Source = RankSource.SearchConsole,
                });
            }
        }

        var sources = new[]
        {
            ($"https://www.healthline-blog.example/best-{own.Split('.')[0]}-alternatives", "Live", "best alternatives", (string?)null),
            ("https://news.techdaily.example/startups-to-watch-2026", "Live", own, (string?)null),
            ("https://forum.communityboard.example/t/recommendations/4411", "Nofollow", "this one", "ugc nofollow"),
            ("https://www.partnerdirectory.example/listing/" + own, "Live", client.Name, (string?)null),
            ("https://oldblog.example/2023/roundup", "Lost", "great resource", (string?)null),
            ("https://press.example/releases/" + own + "-funding", "Unchecked", client.Name + " announces", (string?)null),
        };
        var i = 0;
        foreach (var (source, status, anchor, rel) in sources)
        {
            i++;
            var target = $"https://{domain}/";
            db.Add(new SeoBacklink
            {
                SiteId = site.Id, ClientAccountId = client.Id, SourceUrl = source, TargetUrl = target,
                LinkHash = Normalization.Sha256Hex(Backlinks.BacklinkChecker.Comparable(source) + "\n" + Backlinks.BacklinkChecker.Comparable(target)),
                AnchorText = anchor, Rel = rel, FirstSeenAt = _now.AddDays(-90 + i * 9), Status = Enum.Parse<BacklinkStatus>(status),
                LastCheckedAt = status == "Unchecked" ? null : _now.AddDays(-1), LastStatusCode = status == "Lost" ? 404 : status == "Unchecked" ? null : 200,
                CheckMessage = status switch { "Live" => "Live and followed.", "Nofollow" => "Link is rel=\"ugc nofollow\".", "Lost" => "The linking page answers 404.", _ => null },
            });
        }
        db.Add(new SeoOutreachProspect { SiteId = site.Id, ClientAccountId = client.Id, ProspectUrl = "https://www.industryweekly.example/contribute", ContactName = "Maya Collins", ContactEmail = "editor@industryweekly.example", Status = OutreachStatus.Contacted, LastContactedAt = _now.AddDays(-6), Notes = "Pitched a data-led guest post; follow up next week." });
        db.Add(new SeoOutreachProspect { SiteId = site.Id, ClientAccountId = client.Id, ProspectUrl = "https://resources.example/best-tools", ContactName = "Omar Siddiqui", ContactEmail = "omar@resources.example", Status = OutreachStatus.Replied, LastContactedAt = _now.AddDays(-3), Notes = "Open to adding us to the tools list with a short blurb." });
        db.Add(new SeoOutreachProspect { SiteId = site.Id, ClientAccountId = client.Id, ProspectUrl = "https://podcast.example/guests", Status = OutreachStatus.Identified, Notes = "Founder interview opportunity." });

        db.Add(new SeoContentBrief
        {
            SiteId = site.Id, ClientAccountId = client.Id, Title = $"The complete guide to {keywords[0]}", TargetKeyword = keywords[0],
            RelatedKeywords = keywords.Skip(1).Take(3).ToList(),
            Questions = new List<string> { $"What is the best {keywords[0]}?", $"How much does {keywords[0]} cost?", $"Is {keywords[0]} worth it?" },
            Outline = new List<string> { "## Why it matters", "## How to choose", "### Features to compare", "## Our top picks", "## FAQs" },
            WordCountTarget = 1800, CompetitorUrls = competitors.Select(c => $"https://{c}/").ToList(), Status = ContentBriefStatus.Ready,
            Notes = "Include original data from our customer survey and link to the pricing page.",
        });
        await db.SaveChangesAsync(ct);
    }

    private SeoAudit AddAudit(AppDbContext db, SeoSite site, DateTime at, int pages, int score, (string Rule, int Count)[] issues)
    {
        var audit = new SeoAudit
        {
            SiteId = site.Id, ClientAccountId = site.ClientAccountId, Status = SeoAuditStatus.Completed, QueuedAt = at, StartedAt = at,
            FinishedAt = at.AddMinutes(4), PagesCrawled = pages, HealthScore = score, RobotsTxtFound = true, SitemapFound = true,
        };
        db.Add(audit);
        var paths = new[] { "", "pricing", "features", "blog", "blog/getting-started", "blog/tips", "about", "contact", "careers", "help", "help/faq",
            "blog/guide", "blog/news", "privacy", "terms", "download", "partners", "press", "case-studies", "integrations" };
        for (var p = 0; p < pages; p++)
        {
            var path = p < paths.Length ? paths[p] : $"blog/post-{p}";
            db.Add(new SeoAuditPage
            {
                AuditId = audit.Id, Url = $"https://{site.Domain}/{path}", StatusCode = p == 7 ? 404 : 200, Depth = path.Count(c => c == '/') + (path.Length > 0 ? 1 : 0),
                ResponseTimeMs = 180 + _random.Next(0, 900), ContentLength = 30_000 + _random.Next(0, 90_000), ContentType = "text/html; charset=utf-8",
                Title = $"{site.Name} — {path}", WordCount = 250 + _random.Next(0, 1200), H1Count = 1, InSitemap = true, InboundLinks = _random.Next(0, 20),
            });
        }
        var errors = 0; var warnings = 0; var notices = 0;
        foreach (var (rule, count) in issues)
        {
            var definition = SeoAuditRules.Find(rule)!;
            var urls = Enumerable.Range(0, count).Select(n => $"https://{site.Domain}/{paths[(n * 3 + rule.Length) % paths.Length]}").Distinct().ToList();
            db.Add(new SeoAuditIssue { AuditId = audit.Id, RuleKey = rule, Severity = definition.Severity, AffectedCount = urls.Count, AffectedUrls = urls });
            switch (definition.Severity)
            {
                case SeoSeverity.Error: errors += urls.Count; break;
                case SeoSeverity.Warning: warnings += urls.Count; break;
                default: notices += urls.Count; break;
            }
        }
        audit.ErrorCount = errors;
        audit.WarningCount = warnings;
        audit.NoticeCount = notices;
        return audit;
    }

    private async Task SeedLocalAsync(AppDbContext db, ClientAccount client, CancellationToken ct)
    {
        var site = await db.Set<SeoSite>().FirstOrDefaultAsync(s => s.ClientAccountId == client.Id, ct);
        if (site is null || await db.Set<SeoLocalProfile>().AnyAsync(p => p.SiteId == site.Id, ct)) return;
        db.Add(new SeoLocalProfile
        {
            SiteId = site.Id, ClientAccountId = client.Id, BusinessName = "Karachi Eats", Address = "Plot 12-C, Khayaban-e-Ittehad, Phase 6, DHA, Karachi",
            Phone = "+92 21 3584 0000", Website = "https://karachieats.pk",
            CompletedChecklist = LocalSeoCatalog.GbpChecklist.Take(11).Select(i => i.Key).ToList(),
        });
        var sources = await db.Set<SeoCitationSource>().OrderBy(s => s.SortOrder).ToListAsync(ct);
        foreach (var source in sources.Where(s => s.Countries.Count == 0 || s.Countries.Contains("PK")).Take(8))
        {
            var mismatch = source.Key == "foodpanda";
            db.Add(new SeoCitation
            {
                SiteId = site.Id, ClientAccountId = client.Id, SourceId = source.Id,
                Status = mismatch ? CitationStatus.NeedsUpdate : source.Key is "yelp" or "tripadvisor" ? CitationStatus.Submitted : CitationStatus.Live,
                ListingUrl = $"{source.Url}/karachi-eats", ListedName = "Karachi Eats",
                ListedAddress = mismatch ? "Phase 5, DHA, Karachi" : "Plot 12-C, Khayaban-e-Ittehad, Phase 6, DHA, Karachi",
                ListedPhone = "021 3584 0000", Notes = mismatch ? "Old address from before the move — update requested." : null,
            });
        }
        var reviews = new (string Platform, int Rating, string Author, string Text, bool Responded)[]
        {
            ("Google", 5, "Ayesha K.", "Best biryani in DHA, generous portions and quick service.", true),
            ("Google", 4, "Bilal A.", "Great family atmosphere; parking can be tricky on weekends.", true),
            ("Tripadvisor", 5, "Sophie T.", "Wonderful BBQ platter and friendly staff — a highlight of our trip.", false),
            ("Google", 2, "Hamza R.", "Delivery took over an hour on Friday night.", true),
            ("Facebook", 5, "Zara M.", "Booked the iftar buffet for 20 people and everything was perfect.", false),
        };
        var d = 3;
        foreach (var r in reviews)
        {
            db.Add(new SeoReview
            {
                SiteId = site.Id, ClientAccountId = client.Id, Platform = r.Platform, Rating = r.Rating, AuthorName = r.Author, Text = r.Text,
                ReviewedAt = _now.AddDays(-d), Responded = r.Responded,
                ResponseText = r.Responded ? "Thank you for your feedback — we hope to welcome you again soon!" : null,
            });
            d += 5;
        }
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------------ Landing pages

    private async Task SeedPagesAsync(AppDbContext db, ClientAccount client, User designer, string slug, string name, string templateKey, bool experiment,
        CancellationToken ct)
    {
        if (await db.Set<LandingPage>().AnyAsync(p => p.ClientAccountId == client.Id && p.Slug == slug, ct)) return;
        var template = TemplateCatalog.Pages.First(t => t.Key == templateKey);
        var formTemplate = TemplateCatalog.Forms.First(t => t.Key == template.FormTemplateKey);
        var form = new Form
        {
            ClientAccountId = client.Id, Name = $"{name} form", SchemaJson = formTemplate.SchemaJson, SubmitLabel = formTemplate.SubmitLabel,
            SuccessMessage = formTemplate.SuccessMessage, TemplateKey = formTemplate.Key, AutoresponderEnabled = true,
            AutoresponderSubject = formTemplate.AutoresponderSubject, AutoresponderBody = formTemplate.AutoresponderBody,
            ConsentText = formTemplate.ConsentText, ConsentVersion = 1, NotifyUserIds = new List<Guid> { designer.Id },
            AllowedOrigins = new List<string> { DemoClients.All.First(c => c.Slug == client.Slug).Website },
        };
        db.Add(form);
        db.Add(new FormConsentVersion { FormId = form.Id, Version = 1, Text = form.ConsentText!, CreatedAt = _now.AddDays(-45) });

        var context = new BlockValidationContext(_ => false, id => id == form.Id);
        var baseVariants = LandingPageService.InstantiateTemplate(template.BlocksJson, form.Id, _now);
        var variants = LandingBlocks.ParseVariants(baseVariants, context).ToList();
        if (experiment)
        {
            var control = variants[0];
            var blocks = control.Blocks.Select(b => b.Props is HeroProps hero
                ? b with { Props = new HeroProps { Headline = "Your personal AI trainer — free for 14 days", Subheadline = hero.Subheadline, CtaLabel = "Start my free trial", CtaHref = hero.CtaHref, Align = hero.Align, Theme = hero.Theme } }
                : b).ToList();
            variants[0] = control with { Name = "Control" };
            variants.Add(new LandingVariant("B", "AI trainer headline", 50, blocks));
        }
        var page = new LandingPage
        {
            ClientAccountId = client.Id, Name = name, Slug = slug, TemplateKey = template.Key, MetaTitle = $"{name} | {client.Name}",
            MetaDescription = template.MetaDescription, VariantsJson = LandingBlocks.Serialize(variants), ExperimentEnabled = experiment,
            ExperimentStartedAt = experiment ? _now.AddDays(-30) : null, CreatedByUserId = designer.Id, Status = LandingPageStatus.Published,
            HasUnpublishedChanges = false,
        };
        var snapshot = LandingPageService.BuildSnapshot(page);
        var version = new LandingPageVersion
        {
            PageId = page.Id, ClientAccountId = client.Id, Version = 1, SnapshotJson = snapshot, ContentHash = Normalization.Sha256Hex(snapshot),
            PublishedAt = _now.AddDays(-30), PublishedByUserId = designer.Id,
        };
        page.PublishedVersionId = version.Id;
        page.PublishedAt = version.PublishedAt;
        db.Add(page);
        db.Add(version);

        // 30 days of traffic: unique visitors, sticky assignments for the experiment and conversions (B converts better).
        var firstNames = new[] { "Olivia", "Liam", "Aisha", "Noah", "Fatima", "Ethan", "Sara", "Lucas", "Zainab", "Mia", "Omar", "Chloe", "Ali", "Emma", "Hassan" };
        var lastNames = new[] { "Khan", "Smith", "Ahmed", "Johnson", "Patel", "Brown", "Hussain", "Taylor", "Malik", "Wilson" };
        var visitors = 420 + _random.Next(0, 200);
        var sources = new[] { "google", "facebook", "instagram", "newsletter", "linkedin" };
        var submissions = 0;
        for (var v = 0; v < visitors; v++)
        {
            var visitorHash = hasher.Hash($"v:demo-{client.Slug}-{slug}-{v}")!;
            var key = experiment ? VariantAssigner.Assign(page.ExperimentId, VariantAssigner.VisitorSubject(visitorHash),
                variants.Select(x => new WeightedVariant(x.Key, x.Weight)).ToList()) : "A";
            var viewedAt = _now.AddDays(-_random.Next(0, 30)).AddMinutes(-_random.Next(0, 1440));
            if (experiment)
                db.Add(new LandingPageAssignment { PageId = page.Id, ExperimentId = page.ExperimentId, SubjectKey = VariantAssigner.VisitorSubject(visitorHash), VariantKey = key, AssignedAt = viewedAt });
            var utm = sources[_random.Next(sources.Length)];
            db.Add(new LandingPageView
            {
                PageId = page.Id, ClientAccountId = client.Id, VersionId = version.Id, VariantKey = key, ExperimentId = experiment ? page.ExperimentId : null,
                VisitorHash = visitorHash, ViewedAt = viewedAt, IsUnique = true, UtmSource = utm, ReferrerHost = utm == "google" ? "www.google.com" : null,
            });
            var rate = key == "B" ? 0.11 : 0.07;
            if (_random.NextDouble() >= rate) continue;
            submissions++;
            var first = firstNames[_random.Next(firstNames.Length)];
            var last = lastNames[_random.Next(lastNames.Length)];
            var email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{v}@example.com";
            var values = new Dictionary<string, string> { ["email"] = email, ["consent"] = "yes", ["utm_source"] = utm };
            if (formTemplate.Key == "newsletter") values["first_name"] = first;
            else values["name"] = $"{first} {last}";
            if (formTemplate.Key == "contact") { values["topic"] = "sales-enquiry"; values["message"] = "I'd like to learn more about your plans for teams."; }
            if (formTemplate.Key == "webinar-registration") values["company_size"] = "11-50";
            var submittedAt = viewedAt.AddMinutes(2 + _random.Next(0, 10));
            db.Add(new FormSubmission
            {
                FormId = form.Id, ClientAccountId = client.Id, LandingPageId = page.Id, VariantKey = key, ExperimentId = experiment ? page.ExperimentId : null,
                DataJson = JsonSerializer.Serialize(values), Email = email, Name = values.GetValueOrDefault("name") ?? first,
                UtmSource = utm, UtmMedium = utm is "google" ? "cpc" : "social", UtmCampaign = slug, ConsentGiven = true, ConsentVersion = 1,
                IpHash = hasher.Hash($"demo-ip-{v}"), SubmittedAt = submittedAt, EventPublishedAt = submittedAt,
            });
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo landing page {Slug}: {Visitors} visitors, {Submissions} submissions", slug, visitors, submissions);
    }
}
