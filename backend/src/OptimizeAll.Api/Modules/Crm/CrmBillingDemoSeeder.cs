using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>
/// "Demo" profile for CRM, proposals, contracts and billing (STAGING/DEMO DATA ONLY). Ensures the shared demo agency staff,
/// client accounts and client users of <see cref="DeliveryDemoData"/> exist (through <see cref="DeliveryDemoAccounts"/>, so
/// they are the same whichever demo seeder runs first), then adds a realistic pipeline, activities, proposals, retainers,
/// invoices (numbered through the real gapless sequence), payments and a credit note. Idempotent via <see cref="MarkerKey"/>.
/// </summary>
public sealed class CrmBillingDemoSeeder(
    TimeProvider clock,
    IPasswordHasher<User> passwordHasher,
    DocumentNumberService numbers,
    IDatabaseDialect dialect,
    PublicLinkTokens tokens,
    ILogger<CrmBillingDemoSeeder> logger) : ISeeder
{
    public const string MarkerKey = "demo.crm-billing.seeded";
    /// <summary>Password of every demo account (<see cref="DeliveryDemoData.Password"/>).</summary>
    public const string Password = DeliveryDemoData.Password;
    public const string StaffDomain = DeliveryDemoData.Domain;

    public string Profile => "Demo";
    public int Order => 300;

    private DateTime _now;
    private DateOnly _today;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<SystemSetting>().AnyAsync(s => s.Key == MarkerKey, ct))
        {
            logger.LogInformation("CRM & billing demo data already present; skipping");
            return;
        }
        _now = clock.GetUtcNow().UtcDateTime;
        _today = DateOnly.FromDateTime(_now);

        // Shared people and clients: the canonical DeliveryDemoData records, created when missing exactly as the delivery demo
        // seeder creates them (so the result doesn't depend on which seeder runs first).
        var (people, clients) = await DeliveryDemoAccounts.EnsureAsync(db, passwordHasher, _now, ct);
        var staff = DeliveryDemoData.Staff.ToDictionary(x => x.Email.Split('@')[0], x => people[x.Email].Id);
        await db.SaveChangesAsync(ct);
        if (!await db.Set<PipelineStage>().AnyAsync(ct)) await new CrmBaselineSeeder().SeedAsync(db, ct);
        if (!await db.Set<TaxRate>().AnyAsync(ct)) await new BillingBaselineSeeder().SeedAsync(db, ct);

        foreach (var series in new[] { DocumentNumberService.InvoiceSeries, DocumentNumberService.CreditNoteSeries,
                     DocumentNumberService.ContractSeries, DocumentNumberService.ProposalSeries })
        {
            await numbers.EnsureAsync(db, series, ct, _today.Year);
            await numbers.EnsureAsync(db, series, ct, _today.AddDays(-130).Year);
        }

        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var stages = await db.Set<PipelineStage>().AsNoTracking().ToDictionaryAsync(s => s.Name, ct);
        Guid Stage(string name) => stages.TryGetValue(name, out var s) ? s.Id : stages.Values.Where(x => x.Kind == StageKind.Open).OrderBy(x => x.Position).First().Id;
        var wonStage = stages.Values.First(s => s.Kind == StageKind.Won).Id;
        var lostStage = stages.Values.First(s => s.Kind == StageKind.Lost).Id;
        var rates = await db.Set<TaxRate>().AsNoTracking().ToListAsync(ct);
        TaxRate? Rate(string country) => rates.FirstOrDefault(r => r.CountryCode == country && r.RatePercent > 0 && r.IsActive);

        // ---------------- Companies & contacts
        var sales = staff["sales"];
        var am = staff["am"];
        var companies = new Dictionary<string, CrmCompany>();
        CrmCompany Company(string key, string name, string? domain, string industry, CompanySize size, string country, Guid owner, Guid? client = null,
            params string[] tags)
        {
            var c = new CrmCompany
            {
                Name = name, Domain = domain, Industry = industry, Size = size, CountryCode = country, OwnerUserId = owner, ClientAccountId = client,
                Tags = tags.ToList(), TagIndex = CrmService.TagIndex(tags), CreatedAt = _now.AddDays(-120),
                CustomFieldsJson = JsonSerializer.Serialize(new SortedDictionary<string, object?> { ["segment"] = size >= CompanySize.Medium ? "mid-market" : "smb" }),
            };
            db.Set<CrmCompany>().Add(c);
            companies[key] = c;
            return c;
        }
        foreach (var c in DeliveryDemoData.Clients)
            Company(c.Slug, c.Name, CrmNormalization.Domain(c.Website), c.Industry, CompanySize.Medium, c.CountryCode, am, clients[c.Slug].Id, "client");
        Company("brightline", "Brightline Dental Group", "brightlinedental.example", "healthcare", CompanySize.Small, "US", sales, null, "multi-location");
        Company("peak", "Peak Outdoors", "peakoutdoors.example", "e-commerce", CompanySize.Medium, "GB", sales, null, "shopify");
        Company("lumen", "Lumen Analytics", "lumenanalytics.example", "saas", CompanySize.Medium, "AE", sales, null, "b2b");
        Company("saffron", "Saffron Spice Co.", "saffronspice.example", "restaurant", CompanySize.Small, "PK", sales, null, "local");
        Company("harbor", "Harbor Legal LLP", "harborlegal.example", "legal", CompanySize.Small, "US", am, null);
        Company("velo", "Velo Couriers", "velocouriers.example", "logistics", CompanySize.Large, "GB", sales, null, "enterprise");

        var contacts = new Dictionary<string, CrmContact>();
        CrmContact Contact(string key, string company, string first, string last, string email, string title, LifecycleStage stage, int score,
            ConsentStatus consent, string source, string? budget = null)
        {
            var c = new CrmContact
            {
                FirstName = first, LastName = last, Email = email, NormalizedEmail = Normalization.Email(email), JobTitle = title,
                CompanyId = companies[company].Id, LifecycleStage = stage, OwnerUserId = companies[company].OwnerUserId, ConsentStatus = consent,
                Source = source, BudgetRange = budget, Score = score, ScoredAt = _now, CreatedAt = _now.AddDays(-90),
                FirstTouch = new UtmTouch { Source = source == "WebsiteInquiry" ? "google" : null, Medium = source == "WebsiteInquiry" ? "cpc" : null,
                    Campaign = source == "WebsiteInquiry" ? "agency-brand" : null, At = _now.AddDays(-90) },
                LastTouch = new UtmTouch { Source = "newsletter", Medium = "email", Campaign = "monthly-insights", At = _now.AddDays(-5) },
            };
            db.Set<CrmContact>().Add(c);
            contacts[key] = c;
            return c;
        }
        Contact("olivia", "nimbus-fitness", "Olivia", "Carter", "olivia.carter@nimbusfitness.example", "VP Growth", LifecycleStage.Customer, 75, ConsentStatus.Subscribed, "Referral", "10k-25k");
        Contact("james", "wanderly-travel", "James", "Whitfield", "james@wanderly.example", "Head of Marketing", LifecycleStage.Customer, 60, ConsentStatus.Subscribed, "Event", "5k-10k");
        Contact("noor", "aurora-skincare", "Noor", "Al Mansoori", "noor@auroraskincare.example", "Founder", LifecycleStage.Customer, 70, ConsentStatus.Subscribed, "WebsiteInquiry", "10k-25k");
        Contact("bilal", "karachi-eats", "Bilal", "Siddiqui", "bilal@karachieats.example", "Operations Director", LifecycleStage.Evangelist, 55, ConsentStatus.NotGiven, "Referral", "2k-5k");
        Contact("rachel", "brightline", "Rachel", "Nguyen", "rachel.nguyen@brightlinedental.example", "Practice Manager", LifecycleStage.SalesQualifiedLead, 58, ConsentStatus.Subscribed, "WebsiteInquiry", "5k-10k");
        Contact("tom", "peak", "Tom", "Ellison", "tom@peakoutdoors.example", "E-commerce Manager", LifecycleStage.Opportunity, 66, ConsentStatus.Subscribed, "Form", "10k-25k");
        Contact("fatima", "lumen", "Fatima", "Khan", "fatima@lumenanalytics.example", "CMO", LifecycleStage.Opportunity, 72, ConsentStatus.Subscribed, "Outbound", "10k-25k");
        Contact("ali", "saffron", "Ali", "Hamdani", "ali@saffronspice.example", "Owner", LifecycleStage.Lead, 23, ConsentStatus.Unknown, "WebsiteInquiry", "2k-5k");
        Contact("sarah", "harbor", "Sarah", "Mitchell", "smitchell@harborlegal.example", "Managing Partner", LifecycleStage.MarketingQualifiedLead, 35, ConsentStatus.Subscribed, "Event");
        Contact("george", "velo", "George", "Hart", "george.hart@velocouriers.example", "Marketing Director", LifecycleStage.Lead, 40, ConsentStatus.Unsubscribed, "Outbound", "25k+");

        // ---------------- Deals across the pipeline
        var deals = new Dictionary<string, CrmDeal>();
        CrmDeal Deal(string key, string title, string company, string contact, string stage, decimal value, string currency, DealSource source,
            int ageDays, Guid owner, string[] services, string? lostReason = null, string? utmSource = null, string? utmCampaign = null)
        {
            var stageId = stage == "Won" ? wonStage : stage == "Lost" ? lostStage : Stage(stage);
            var d = new CrmDeal
            {
                Title = title, CompanyId = companies[company].Id, PrimaryContactId = contacts[contact].Id, StageId = stageId, Value = value,
                Currency = currency, ExpectedCloseDate = _today.AddDays(stage is "Won" or "Lost" ? -ageDays / 2 : 30 - ageDays / 3),
                ServiceSlugs = services.ToList(), OwnerUserId = owner, Source = source, SourceDetail = source == DealSource.WebsiteInquiry ? "free-audit" : null,
                Status = stage == "Won" ? DealStatus.Won : stage == "Lost" ? DealStatus.Lost : DealStatus.Open, LostReason = lostReason,
                ClientAccountId = companies[company].ClientAccountId, CreatedAt = _now.AddDays(-ageDays), StageChangedAt = _now.AddDays(-ageDays / 2),
                ClosedAt = stage is "Won" or "Lost" ? _now.AddDays(-ageDays / 2) : null,
                FirstTouch = new UtmTouch { Source = utmSource, Medium = utmSource is null ? null : "cpc", Campaign = utmCampaign, At = _now.AddDays(-ageDays) },
                LastTouch = new UtmTouch { Source = utmSource ?? "direct", Medium = utmSource is null ? "none" : "cpc", Campaign = utmCampaign, At = _now.AddDays(-2) },
            };
            db.Set<CrmDeal>().Add(d);
            deals[key] = d;
            return d;
        }
        Deal("nimbus", "Nimbus Fitness — growth retainer", "nimbus-fitness", "olivia", "Won", 54_000m, "USD", DealSource.Referral, 150, am,
            new[] { "seo", "content-marketing", "paid-ads" });
        Deal("wanderly", "Wanderly Travel — social media management", "wanderly-travel", "james", "Won", 38_400m, "GBP", DealSource.Event, 140, am,
            new[] { "social-media" });
        Deal("aurora", "Aurora Skincare — performance marketing", "aurora-skincare", "noor", "Won", 144_000m, "AED", DealSource.WebsiteInquiry, 130, am,
            new[] { "paid-ads", "email-marketing" }, utmSource: "google", utmCampaign: "ecommerce-growth");
        Deal("karachi", "Karachi Eats — local SEO & social", "karachi-eats", "bilal", "Won", 4_200_000m, "PKR", DealSource.Referral, 125, am,
            new[] { "local-seo", "social-media" });
        Deal("brightline", "Brightline Dental — patient acquisition", "brightline", "rachel", "Proposal sent", 36_000m, "USD", DealSource.WebsiteInquiry, 21,
            sales, new[] { "local-seo", "paid-ads" }, utmSource: "google", utmCampaign: "dental-marketing");
        Deal("peak", "Peak Outdoors — Shopify CRO + email", "peak", "tom", "Negotiation", 28_000m, "GBP", DealSource.Form, 35, sales,
            new[] { "cro", "email-marketing" }, utmSource: "linkedin", utmCampaign: "ecommerce-webinar");
        Deal("lumen", "Lumen Analytics — B2B demand gen", "lumen", "fatima", "Discovery call", 96_000m, "AED", DealSource.Outbound, 14, sales,
            new[] { "paid-ads", "content-marketing", "seo" });
        Deal("saffron", "Saffron Spice — Google Business & Instagram", "saffron", "ali", "New", 600_000m, "PKR", DealSource.WebsiteInquiry, 2, sales,
            new[] { "local-seo", "social-media" }, utmSource: "google", utmCampaign: "restaurant-marketing");
        Deal("harbor", "Harbor Legal — website redesign", "harbor", "sarah", "Qualified", 18_500m, "USD", DealSource.Event, 28, am,
            new[] { "web-design" });
        Deal("velo", "Velo Couriers — brand campaign", "velo", "george", "Contacted", 60_000m, "GBP", DealSource.Outbound, 9, sales,
            new[] { "branding", "paid-ads" });
        Deal("lost", "Harbor Legal — PPC pilot", "harbor", "sarah", "Lost", 6_000m, "USD", DealSource.Outbound, 80, am, new[] { "paid-ads" },
            lostReason: "Chose an in-house hire for PPC.");

        // ---------------- Activities (timeline, tasks due today/overdue, meetings)
        void Activity(ActivityType type, string subject, string deal, Guid? assignee, int createdDaysAgo, DateTime? due = null, DateTime? occurs = null,
            string? body = null, bool completed = false)
        {
            var d = deals[deal];
            db.Set<CrmActivity>().Add(new CrmActivity
            {
                Type = type, Subject = subject, Body = body, DealId = d.Id, ContactId = d.PrimaryContactId, CompanyId = d.CompanyId,
                AssigneeUserId = assignee, CreatedByUserId = assignee, DueAt = due, OccursAt = occurs, RemindAt = due?.AddHours(-2),
                CompletedAt = completed ? _now.AddDays(-1) : null, CreatedAt = _now.AddDays(-createdDaysAgo),
                ReminderSentAt = due < _now ? due : null, OverdueNotifiedAt = null,
            });
        }
        Activity(ActivityType.Note, "Free audit requested", "saffron", sales, 2, body: "Wants more walk-ins from Google Maps; 3 branches in Karachi.");
        Activity(ActivityType.Task, "Call Ali to book discovery call", "saffron", sales, 2, due: _now.Date.AddHours(15));
        Activity(ActivityType.Call, "Intro call with Rachel", "brightline", sales, 18, occurs: _now.AddDays(-18), body: "Six clinics, weak local rankings.");
        Activity(ActivityType.Meeting, "Discovery call — Brightline", "brightline", sales, 14, occurs: _now.AddDays(-14));
        Activity(ActivityType.Task, "Follow up on Brightline proposal", "brightline", sales, 3, due: _now.AddDays(-1));
        Activity(ActivityType.Email, "Sent revised pricing", "peak", sales, 5, occurs: _now.AddDays(-5));
        Activity(ActivityType.Task, "Send Peak Outdoors contract redlines", "peak", sales, 2, due: _now.Date.AddHours(17));
        Activity(ActivityType.Meeting, "Discovery call — Lumen Analytics", "lumen", sales, 3, occurs: _now.Date.AddDays(1).AddHours(10));
        Activity(ActivityType.Task, "Prepare Lumen competitor analysis", "lumen", staff["strategist"], 3, due: _now.AddDays(3));
        Activity(ActivityType.Note, "Met at LegalTech Summit", "harbor", am, 28, body: "Interested in a new site before Q1.");
        Activity(ActivityType.Task, "Quarterly business review with Nimbus", "nimbus", am, 10, due: _now.AddDays(7));
        Activity(ActivityType.Task, "Collect Wanderly brand assets", "wanderly", am, 40, due: _now.AddDays(-30), completed: true);

        // ---------------- Proposals (accepted for Nimbus, sent to Brightline, draft for Lumen)
        var settings = new BillingSettings();
        var proposalNimbus = NewProposal(await numbers.NextAsync(db, DocumentNumberService.ProposalSeries, settings.ProposalPrefix, 4, ct, _today.AddDays(-125).Year),
            "Nimbus Fitness growth retainer", deals["nimbus"], contacts["olivia"], clients["nimbus-fitness"].Id, "USD", -125, am);
        var nimbusVersion = AddVersion(db, proposalNimbus, 1, "USD", _today.AddDays(-95), new[]
        {
            Line("SEO & content retainer", "seo", 1, 2_500m, Recurrence.Monthly, null),
            Line("Paid ads management (Google & Meta)", "paid-ads", 1, 2_000m, Recurrence.Monthly, null),
            Line("Onboarding & analytics setup", "analytics", 1, 3_000m, Recurrence.OneTime, null, DiscountType.Percent, 10m),
        }, sent: true, locked: true);
        proposalNimbus.Status = ProposalStatus.Accepted;
        proposalNimbus.SentVersion = 1;
        proposalNimbus.AcceptedVersion = 1;
        proposalNimbus.SentAt = _now.AddDays(-124);
        proposalNimbus.ViewCount = 6;
        proposalNimbus.FirstViewedAt = _now.AddDays(-124);
        proposalNimbus.LastViewedAt = _now.AddDays(-121);
        proposalNimbus.AcceptedAt = _now.AddDays(-120);
        proposalNimbus.SignerName = "Olivia Carter";
        proposalNimbus.SignerTitle = "VP Growth";
        proposalNimbus.SignerEmail = "owner@nimbus.demo.optimizeall.app";
        proposalNimbus.SignerUserAgent = "Demo data";

        var proposalBrightline = NewProposal(await numbers.NextAsync(db, DocumentNumberService.ProposalSeries, settings.ProposalPrefix, 4, ct, _today.AddDays(-6).Year),
            "Brightline Dental patient acquisition", deals["brightline"], contacts["rachel"], null, "USD", -8, sales);
        AddVersion(db, proposalBrightline, 1, "USD", _today.AddDays(20), new[]
        {
            Line("Local SEO for 6 clinics", "local-seo", 6, 400m, Recurrence.Monthly, null),
            Line("Google Ads management", "paid-ads", 1, 1_200m, Recurrence.Monthly, null),
            Line("Google Business Profile optimization", "local-seo", 6, 250m, Recurrence.OneTime, null),
        }, sent: true, locked: false, createdDaysAgo: 8);
        AddVersion(db, proposalBrightline, 2, "USD", _today.AddDays(24), new[]
        {
            Line("Local SEO for 6 clinics", "local-seo", 6, 375m, Recurrence.Monthly, null),
            Line("Google Ads management", "paid-ads", 1, 1_200m, Recurrence.Monthly, null),
            Line("Google Business Profile optimization", "local-seo", 6, 250m, Recurrence.OneTime, null, DiscountType.Amount, 150m),
        }, sent: true, locked: false, createdDaysAgo: 6);
        proposalBrightline.CurrentVersion = 2;
        proposalBrightline.SentVersion = 2;
        proposalBrightline.Status = ProposalStatus.Viewed;
        proposalBrightline.SentAt = _now.AddDays(-6);
        proposalBrightline.ViewCount = 3;
        proposalBrightline.FirstViewedAt = _now.AddDays(-7);
        proposalBrightline.LastViewedAt = _now.AddDays(-2);
        var share = tokens.Create();
        proposalBrightline.ShareTokenHash = share.Hash;
        proposalBrightline.ShareTokenProtected = share.Protected;

        var proposalLumen = NewProposal(await numbers.NextAsync(db, DocumentNumberService.ProposalSeries, settings.ProposalPrefix, 4, ct, _today.Year),
            "Lumen Analytics demand generation", deals["lumen"], contacts["fatima"], null, "AED", -1, sales);
        AddVersion(db, proposalLumen, 1, "AED", _today.AddDays(30), new[]
        {
            Line("LinkedIn & Google Ads management", "paid-ads", 1, 9_000m, Recurrence.Monthly, Rate("AE")),
            Line("Thought-leadership content (4 articles)", "content-marketing", 4, 1_500m, Recurrence.Monthly, Rate("AE")),
            Line("Annual SEO audit", "seo", 1, 12_000m, Recurrence.Annually, Rate("AE")),
        }, sent: false, locked: false, createdDaysAgo: 1);

        // ---------------- Retainers (contracts) and their invoices
        var contractNimbus = AddContract(db, await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, 4, ct, _today.AddDays(-120).Year),
            "Nimbus Fitness growth retainer", clients["nimbus-fitness"], "USD", _today.AddDays(-120), BillingFrequency.Monthly, proposalNimbus.Id, new[]
            {
                Line("SEO & content retainer", "seo", 1, 2_500m, Recurrence.Monthly, null),
                Line("Paid ads management (Google & Meta)", "paid-ads", 1, 2_000m, Recurrence.Monthly, null),
            });
        var contractWanderly = AddContract(db, await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, 4, ct, _today.AddDays(-110).Year),
            "Wanderly social media management", clients["wanderly-travel"], "GBP", _today.AddDays(-110), BillingFrequency.Monthly, null, new[]
            {
                Line("Social media management (Instagram, TikTok, Pinterest)", "social-media", 1, 3_200m, Recurrence.Monthly, Rate("GB")),
            });
        var contractAurora = AddContract(db, await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, 4, ct, _today.AddDays(-100).Year),
            "Aurora performance marketing", clients["aurora-skincare"], "AED", _today.AddDays(-100), BillingFrequency.Quarterly, null, new[]
            {
                Line("Performance marketing (Meta, Google, TikTok) — quarter", "paid-ads", 1, 36_000m, Recurrence.Quarterly, Rate("AE")),
                Line("Email & SMS flows", "email-marketing", 3, 2_500m, Recurrence.Quarterly, Rate("AE")),
            });
        var contractKarachi = AddContract(db, await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, 4, ct, _today.AddDays(-95).Year),
            "Karachi Eats local SEO & social", clients["karachi-eats"], "PKR", _today.AddDays(-95), BillingFrequency.Monthly, null, new[]
            {
                Line("Local SEO — 3 branches", "local-seo", 3, 60_000m, Recurrence.Monthly, Rate("PK")),
                Line("Social media content & community", "social-media", 1, 170_000m, Recurrence.Monthly, Rate("PK")),
            });

        // Nimbus: onboarding invoice (paid) + monthly invoices: paid, paid, partially paid, overdue.
        var nimbusOnboarding = await AddInvoiceAsync(db, settings, clients["nimbus-fitness"], "USD", issueDaysAgo: 120, termsDays: 14, contract: null,
            proposalId: proposalNimbus.Id, periodIndex: null, ct, lines: new[]
            {
                Line("Onboarding & analytics setup", "analytics", 1, 3_000m, Recurrence.OneTime, null, DiscountType.Percent, 10m),
                Line("SEO & content retainer (first monthly period)", "seo", 1, 2_500m, Recurrence.Monthly, null),
                Line("Paid ads management (first monthly period)", "paid-ads", 1, 2_000m, Recurrence.Monthly, null),
            });
        Pay(db, nimbusOnboarding, nimbusOnboarding.Total, PaymentMethod.BankTransfer, "WIRE-NF-0419", daysAgo: 110, staff["am"]);
        for (var i = 1; i <= 3; i++)
        {
            var inv = await AddInvoiceAsync(db, settings, clients["nimbus-fitness"], "USD", issueDaysAgo: 120 - 30 * i, termsDays: 14, contractNimbus,
                null, i, ct);
            if (i == 1) Pay(db, inv, inv.Total, PaymentMethod.BankTransfer, "WIRE-NF-0521", 120 - 30 * i - 10, staff["am"]);
            if (i == 2) Pay(db, inv, 2_000m, PaymentMethod.Card, "CARD-7781", 120 - 30 * i - 5, staff["am"]);
        }
        contractNimbus.NextPeriodIndex = 4;

        // Wanderly: 3 paid invoices, the latest issued and not yet due; one credit note for a missed deliverable.
        for (var i = 0; i < 4; i++)
        {
            var inv = await AddInvoiceAsync(db, settings, clients["wanderly-travel"], "GBP", 110 - 30 * i, 30, contractWanderly, null, i, ct);
            if (i < 3) Pay(db, inv, inv.Total, PaymentMethod.BankTransfer, $"BACS-WT-{i + 1:000}", 110 - 30 * i - 20, staff["am"]);
            if (i == 3)
            {
                var credit = new CreditNote
                {
                    Number = await numbers.NextAsync(db, DocumentNumberService.CreditNoteSeries, settings.CreditNotePrefix, 4, ct, _today.Year),
                    ClientAccountId = inv.ClientAccountId, InvoiceId = inv.Id, Currency = "GBP", Amount = 384m, AmountApplied = 384m,
                    Reason = "Two Pinterest boards not delivered in the period (pro-rated credit).", Status = CreditNoteStatus.Applied,
                    IssueDate = _today.AddDays(-5), RequestId = Guid.NewGuid(), CreatedByUserId = staff["am"],
                };
                db.Set<CreditNote>().Add(credit);
                db.Set<CreditNoteApplication>().Add(new CreditNoteApplication
                {
                    CreditNoteId = credit.Id, InvoiceId = inv.Id, Amount = 384m, AppliedAt = _now.AddDays(-5), AppliedByUserId = staff["am"],
                });
                inv.AmountCredited = 384m;
                inv.RecalculateBalance(inv.Currency);
                inv.Status = inv.SettlementStatus(_today);
            }
        }
        contractWanderly.NextPeriodIndex = 4;

        // Aurora (quarterly): first quarter paid, second quarter issued recently.
        for (var i = 0; i < 2; i++)
        {
            var inv = await AddInvoiceAsync(db, settings, clients["aurora-skincare"], "AED", 100 - 91 * i, 14, contractAurora, null, i, ct);
            if (i == 0) Pay(db, inv, inv.Total, PaymentMethod.BankTransfer, "FAB-TRX-99812", 80, staff["am"]);
        }
        contractAurora.NextPeriodIndex = 2;

        // Karachi Eats: paid, paid, one 60+ days overdue scenario is avoided — a 35-day overdue and a current one.
        for (var i = 0; i < 4; i++)
        {
            var inv = await AddInvoiceAsync(db, settings, clients["karachi-eats"], "PKR", 95 - 30 * i, 7, contractKarachi, null, i, ct);
            if (i < 2) Pay(db, inv, inv.Total, PaymentMethod.BankTransfer, $"IBFT-KE-{i + 1:000}", 95 - 30 * i - 5, staff["am"]);
        }
        contractKarachi.NextPeriodIndex = 4;

        // A draft one-off invoice waiting for review.
        var draft = new Invoice
        {
            ClientAccountId = clients["nimbus-fitness"].Id, Currency = "USD", PaymentTermsDays = 14, CreatedByUserId = staff["am"],
            Notes = "Landing page sprint for the spring challenge campaign.",
            Lines = new List<InvoiceLine> { ToInvoiceLine(Line("Landing page design & build", "web-design", 1, 1_800m, Recurrence.OneTime, null), "USD", 1) },
        };
        LineBuilder.ApplyTotals(draft, LineBuilder.TotalsOf(draft.Lines, "USD"));
        db.Set<Invoice>().Add(draft);

        db.Set<SystemSetting>().Add(new SystemSetting
        {
            Key = MarkerKey, ValueJson = JsonSerializer.Serialize(new { seededAt = _now, version = 1 }),
            Description = "Marker written by the CRM & billing Demo seed (staging/demo data only).", UpdatedAt = _now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogWarning("CRM & billing demo data created (demo/staging only). Brightline proposal link: /p/{Token}", share.Raw);
    }

    // ------------------------------------------------------------------ Helpers

    private sealed record SeedLine(string Description, string Service, decimal Quantity, decimal UnitPrice, Recurrence Recurrence, TaxRate? Rate,
        DiscountType DiscountType, decimal DiscountValue);

    private static SeedLine Line(string description, string service, decimal quantity, decimal unitPrice, Recurrence recurrence, TaxRate? rate,
        DiscountType discountType = DiscountType.None, decimal discountValue = 0) =>
        new(description, service, quantity, unitPrice, recurrence, rate, discountType, discountValue);

    private static T Fill<T>(T line, SeedLine s, string currency, int position) where T : PricedLine
    {
        line.Position = position;
        line.Description = s.Description;
        line.ServiceSlug = s.Service;
        line.Quantity = s.Quantity;
        line.UnitPrice = s.UnitPrice;
        line.DiscountType = s.DiscountType;
        line.DiscountValue = s.DiscountValue;
        line.TaxRateId = s.Rate?.Id;
        line.TaxName = s.Rate?.Name;
        line.TaxPercent = s.Rate?.RatePercent ?? 0;
        line.TaxInclusive = s.Rate?.Inclusive ?? false;
        line.ApplyAmounts(Pricing.Compute(line.ToInput(s.Recurrence), currency));
        return line;
    }

    private static InvoiceLine ToInvoiceLine(SeedLine s, string currency, int position) => Fill(new InvoiceLine(), s, currency, position);

    private Proposal NewProposal(string number, string title, CrmDeal deal, CrmContact contact, Guid? clientId, string currency, int createdDaysAgo, Guid owner) =>
        new()
        {
            Number = number, Title = title, DealId = deal.Id, CompanyId = deal.CompanyId, ContactId = contact.Id, ClientAccountId = clientId,
            Currency = currency, RecipientName = contact.DisplayName, RecipientEmail = contact.Email, CreatedByUserId = owner, SentByUserId = owner,
            CreatedAt = _now.AddDays(createdDaysAgo),
        };

    private ProposalVersion AddVersion(AppDbContext db, Proposal p, int number, string currency, DateOnly validUntil, SeedLine[] lines, bool sent, bool locked,
        int createdDaysAgo = 125)
    {
        if (db.Entry(p).State == EntityState.Detached) db.Set<Proposal>().Add(p);
        var v = new ProposalVersion
        {
            ProposalId = p.Id, VersionNumber = number, Title = p.Title, Currency = currency, ValidUntil = validUntil, CreatedAt = _now.AddDays(-createdDaysAgo),
            CreatedByUserId = p.CreatedByUserId, SentAt = sent ? _now.AddDays(-createdDaysAgo + 1) : null, Locked = locked,
            ExecutiveSummary = $"{p.Title}: a focused plan to grow qualified demand and revenue over the next 12 months.",
            Goals = "• Grow qualified leads 40% in two quarters\n• Lower blended cost per acquisition by 20%\n• Monthly reporting against agreed KPIs",
            Scope = "Strategy, execution and optimization of the channels listed below, with a named account manager.",
            Deliverables = "Monthly plan, campaign builds, content, weekly optimizations, monthly performance report and review call.",
            Timeline = "Weeks 1–2 onboarding and audit; week 3 launch; monthly optimization cycles thereafter.",
            Terms = "Monthly retainers are invoiced in advance and auto-renew; 30 days' notice to cancel. Payment terms 14 days.",
        };
        var inputs = new List<PriceLineInput>();
        var position = 1;
        foreach (var s in lines)
        {
            var line = Fill(new ProposalLine { ProposalVersionId = v.Id, Recurrence = s.Recurrence }, s, currency, position++);
            v.Lines.Add(line);
            inputs.Add(line.ToInput(s.Recurrence));
        }
        var totals = Pricing.Totals(inputs, currency);
        var recurring = Pricing.Recurring(inputs, currency);
        v.GrossTotal = totals.GrossTotal;
        v.DiscountTotal = totals.DiscountTotal;
        v.Subtotal = totals.Subtotal;
        v.TaxTotal = totals.TaxTotal;
        v.Total = totals.Total;
        v.OneTimeTotal = recurring.OneTimeTotal;
        v.MonthlyRecurringValue = recurring.MonthlyRecurringValue;
        v.FirstYearValue = recurring.FirstYearValue;
        db.Set<ProposalVersion>().Add(v);
        return v;
    }

    private Contract AddContract(AppDbContext db, string number, string title, ClientAccount client, string currency, DateOnly start,
        BillingFrequency frequency, Guid? proposalId, SeedLine[] lines)
    {
        var contract = new Contract
        {
            Number = number, Title = title, ClientAccountId = client.Id, Currency = currency, StartDate = start, BillingFrequency = frequency,
            Status = ContractStatus.Active, AutoRenew = true, NoticePeriodDays = 30, PaymentTermsDays = 14, ProposalId = proposalId,
            ProposalVersion = proposalId is null ? null : 1, ActivatedAt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            CreatedAt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
        var position = 1;
        foreach (var s in lines) contract.Lines.Add(Fill(new ContractLine(), s, currency, position++));
        db.Set<Contract>().Add(contract);
        return contract;
    }

    private async Task<Invoice> AddInvoiceAsync(AppDbContext db, BillingSettings settings, ClientAccount client, string currency, int issueDaysAgo,
        int termsDays, Contract? contract, Guid? proposalId, int? periodIndex, CancellationToken ct, SeedLine[]? lines = null)
    {
        var issue = _today.AddDays(-issueDaysAgo);
        var invoice = new Invoice
        {
            ClientAccountId = client.Id, Currency = currency, PaymentTermsDays = termsDays, IssueDate = issue, DueDate = issue.AddDays(termsDays),
            IssuedAt = issue.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), SentAt = issue.ToDateTime(new TimeOnly(9, 5), DateTimeKind.Utc),
            ProposalId = proposalId, ContractId = contract?.Id, CreatedAt = issue.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc),
            Number = await numbers.NextAsync(db, DocumentNumberService.InvoiceSeries, settings.InvoicePrefix, settings.NumberPadding, ct, issue.Year),
        };
        if (contract is not null && periodIndex is { } index)
        {
            invoice.PeriodStart = BillingPeriods.PeriodStart(contract.StartDate, contract.BillingFrequency, index);
            invoice.PeriodEnd = BillingPeriods.PeriodEnd(contract.StartDate, contract.BillingFrequency, index);
            invoice.IdempotencyKey = BillingPeriods.InvoiceKey(contract.Id, invoice.PeriodStart.Value);
            invoice.Reference = contract.Number;
            invoice.Notes = $"{contract.Title} — {invoice.PeriodStart:d MMM yyyy} to {invoice.PeriodEnd:d MMM yyyy}";
            foreach (var l in contract.Lines.OrderBy(l => l.Position)) invoice.Lines.Add(LineBuilder.Copy(l, () => new InvoiceLine(), currency));
        }
        else if (lines is not null)
        {
            var position = 1;
            foreach (var s in lines) invoice.Lines.Add(ToInvoiceLine(s, currency, position++));
            invoice.IdempotencyKey = proposalId is { } p ? $"proposal:{p}:initial" : null;
        }
        LineBuilder.ApplyTotals(invoice, LineBuilder.TotalsOf(invoice.Lines, currency));
        invoice.Status = invoice.DueDate < _today ? InvoiceStatus.Overdue : InvoiceStatus.Issued;
        var token = tokens.Create();
        invoice.PublicTokenHash = token.Hash;
        invoice.PublicTokenProtected = token.Protected;
        db.Set<Invoice>().Add(invoice);
        return invoice;
    }

    private void Pay(AppDbContext db, Invoice invoice, decimal amount, PaymentMethod method, string reference, int daysAgo, Guid recordedBy)
    {
        amount = Money.Round(amount, invoice.Currency);
        db.Set<Payment>().Add(new Payment
        {
            InvoiceId = invoice.Id, ClientAccountId = invoice.ClientAccountId, Amount = amount, Currency = invoice.Currency, Method = method,
            Reference = reference, ActiveReference = reference, PaidOn = _today.AddDays(-daysAgo), RequestId = Guid.NewGuid(), RecordedByUserId = recordedBy,
            CreatedAt = _now.AddDays(-daysAgo),
        });
        invoice.AmountPaid += amount;
        invoice.RecalculateBalance(invoice.Currency);
        invoice.Status = invoice.SettlementStatus(_today);
        if (invoice.Status == InvoiceStatus.Paid) invoice.PaidAt = _now.AddDays(-daysAgo);
    }
}
