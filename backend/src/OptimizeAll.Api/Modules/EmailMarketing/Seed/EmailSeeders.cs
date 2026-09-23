using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Automations;
using OptimizeAll.Api.Modules.EmailMarketing.Campaigns;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Seed;

/// <summary>
/// Baseline: the agency's starter template library and ready-made journeys (drafts). Idempotent by seed key; later
/// edits made in the app are never overwritten.
/// </summary>
public sealed class EmailBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 60;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Set<EmailTemplate>().Where(t => t.ScopeKey == Workspace.AgencyKey && t.SeedKey != null).Select(t => t.SeedKey!).ToListAsync(ct);
        foreach (var starter in StarterTemplates.All.Where(s => !existing.Contains(s.Key)))
            db.Set<EmailTemplate>().Add(new EmailTemplate
            {
                ScopeKey = Workspace.AgencyKey, Name = starter.Name, Category = starter.Category, Subject = starter.Subject, PreviewText = starter.PreviewText,
                DesignJson = starter.Design.ToJson(), SeedKey = starter.Key,
            });
        await db.SaveChangesAsync(ct);

        var templates = await db.Set<EmailTemplate>().AsNoTracking().Where(t => t.ScopeKey == Workspace.AgencyKey && t.SeedKey != null)
            .ToDictionaryAsync(t => t.SeedKey!, t => t.Id, ct);
        var journeys = await db.Set<Automation>().Where(a => a.ScopeKey == Workspace.AgencyKey && a.SeedKey != null).Select(a => a.SeedKey!).ToListAsync(ct);
        foreach (var journey in ReadyMadeJourneys.Build(null, templates, null, null))
        {
            if (journeys.Contains(journey.Automation.SeedKey!)) continue;
            journey.Automation.Status = AutomationStatus.Draft;
            db.Set<Automation>().Add(journey.Automation);
            db.Set<AutomationStep>().AddRange(journey.Steps);
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>The three ready-made journeys: welcome series, abandoned cart and re-engagement.</summary>
public static class ReadyMadeJourneys
{
    public sealed record Journey(Automation Automation, List<AutomationStep> Steps);

    public static IEnumerable<Journey> Build(Guid? clientId, IReadOnlyDictionary<string, Guid> templates, Guid? listId, Guid? senderId)
    {
        var scope = Workspace.Key(clientId);
        Guid? T(string key) => templates.TryGetValue(key, out var id) ? id : null;

        yield return Make(clientId, scope, "journey-welcome", "Welcome series (3 emails)",
            "Greets new subscribers, shows them the ropes and follows up with an offer for engaged readers.",
            AutomationTrigger.ListSubscribed, new TriggerConfig { ListId = listId }, ReentryPolicy.Never, 0, new GoalConfig { Kind = "purchased" }, senderId,
            new StepDefinition { Key = "email-1", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-welcome") }, Next = "wait-1" },
            new StepDefinition { Key = "wait-1", Type = AutomationStepType.Wait, Config = new StepConfig { Days = 2 }, Next = "email-2" },
            new StepDefinition { Key = "email-2", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-newsletter"), Subject = "Getting the most out of {{org_name}}" }, Next = "wait-2" },
            new StepDefinition { Key = "wait-2", Type = AutomationStepType.Wait, Config = new StepConfig { Days = 3, UntilTime = "10:00" }, Next = "opened" },
            new StepDefinition { Key = "opened", Type = AutomationStepType.Condition, Config = new StepConfig { Check = "opened" }, Next = "email-3", AltNext = "tag-cold" },
            new StepDefinition { Key = "email-3", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-promo"), Subject = "A welcome gift for you, {{first_name|friend}}" } },
            new StepDefinition { Key = "tag-cold", Type = AutomationStepType.AddTag, Config = new StepConfig { Tag = "welcome-unengaged" } });

        yield return Make(clientId, scope, "journey-abandoned-cart", "Abandoned cart recovery",
            "Triggered by a cart_abandoned event from the store; stops as soon as the order is completed.",
            AutomationTrigger.CustomEvent, new TriggerConfig { EventName = "cart_abandoned" }, ReentryPolicy.AfterExit, 7, new GoalConfig { Kind = "purchased" }, senderId,
            new StepDefinition { Key = "wait-1h", Type = AutomationStepType.Wait, Config = new StepConfig { Hours = 1 }, Next = "ordered" },
            new StepDefinition { Key = "ordered", Type = AutomationStepType.Condition, Config = new StepConfig { Check = "event", EventName = "order_completed" }, Next = "done", AltNext = "reminder" },
            new StepDefinition { Key = "reminder", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-abandoned-cart") }, Next = "wait-1d" },
            new StepDefinition { Key = "wait-1d", Type = AutomationStepType.Wait, Config = new StepConfig { Days = 1 }, Next = "last-call" },
            new StepDefinition { Key = "last-call", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-abandoned-cart"), Subject = "Last chance: your cart expires soon" } },
            new StepDefinition { Key = "done", Type = AutomationStepType.Exit });

        yield return Make(clientId, scope, "journey-reengagement", "Re-engagement",
            "Wins back contacts tagged inactive-90d; those who don't click are tagged for sunsetting and the account manager is told.",
            AutomationTrigger.TagAdded, new TriggerConfig { Tag = "inactive-90d" }, ReentryPolicy.AfterExit, 90, null, senderId,
            new StepDefinition { Key = "winback", Type = AutomationStepType.SendEmail, Config = new StepConfig { TemplateId = T("starter-reengagement") }, Next = "wait-7d" },
            new StepDefinition { Key = "wait-7d", Type = AutomationStepType.Wait, Config = new StepConfig { Days = 7 }, Next = "clicked" },
            new StepDefinition { Key = "clicked", Type = AutomationStepType.Condition, Config = new StepConfig { Check = "clicked" }, Next = "reactivated", AltNext = "sunset" },
            new StepDefinition { Key = "reactivated", Type = AutomationStepType.RemoveTag, Config = new StepConfig { Tag = "inactive-90d" } },
            new StepDefinition { Key = "sunset", Type = AutomationStepType.AddTag, Config = new StepConfig { Tag = "sunset" }, Next = "notify" },
            new StepDefinition { Key = "notify", Type = AutomationStepType.NotifyStaff, Config = new StepConfig { Message = "A contact did not respond to the re-engagement email and was tagged for sunsetting." } });
    }

    private static Journey Make(Guid? clientId, string scope, string key, string name, string description, AutomationTrigger trigger, TriggerConfig config,
        ReentryPolicy reentry, int cooldown, GoalConfig? goal, Guid? senderId, params StepDefinition[] steps)
    {
        var a = new Automation
        {
            ClientAccountId = clientId, ScopeKey = scope, Name = name, Description = description, Trigger = trigger, TriggerConfigJson = AutomationRules.ToJson(config),
            Reentry = reentry, ReentryCooldownDays = cooldown, GoalJson = goal is null ? null : AutomationRules.ToJson(goal), SenderProfileId = senderId,
            EntryStepKey = steps[0].Key, SeedKey = key,
        };
        var rows = steps.Select((s, i) => new AutomationStep
        {
            AutomationId = a.Id, Key = s.Key, Position = i, Type = s.Type, ConfigJson = AutomationRules.ToJson(s.Config), NextKey = s.Next, AltNextKey = s.AltNext,
        }).ToList();
        return new Journey(a, rows);
    }
}

/// <summary>
/// Demo profile (Order 300, idempotent): the four canonical demo clients, two demo staff members, lists with a few
/// hundred contacts, campaigns in every status with realistic statistics, SMS, an A/B test with a winner and active
/// journeys with enrollments. DEMO/STAGING DATA ONLY — addresses use the reserved example.com domain.
/// </summary>
public sealed class EmailDemoSeeder(TimeProvider clock, IPasswordHasher<User> hasher, IDatabaseDialect dialect, ILogger<EmailDemoSeeder> logger) : ISeeder
{
    public const string DemoPassword = "Demo#2026!pass";
    public const string MarkerSeedKey = "demo-email-seeded";

    public string Profile => "Demo";
    public int Order => 300;

    public sealed record DemoClient(string Slug, string Name, string Industry, string Country, string Currency, string TimeZone, string Address, string Domain);

    public static readonly DemoClient[] Clients =
    {
        new("nimbus-fitness", "Nimbus Fitness", "SaaS fitness app", "US", "USD", "America/New_York", "1200 Market Street, Suite 400, San Francisco, CA 94102, USA", "nimbusfitness.example.com"),
        new("wanderly-travel", "Wanderly Travel", "Travel", "GB", "GBP", "Europe/London", "18 Clerkenwell Road, London EC1M 5RN, United Kingdom", "wanderly.example.com"),
        new("aurora-skincare", "Aurora Skincare", "E-commerce beauty", "AE", "AED", "Asia/Dubai", "Office 1204, Bay Square Building 7, Business Bay, Dubai, UAE", "auroraskincare.example.com"),
        new("karachi-eats", "Karachi Eats", "Restaurant group", "PK", "PKR", "Asia/Karachi", "Plot 14-C, Khayaban-e-Ittehad, DHA Phase 6, Karachi 75500, Pakistan", "karachieats.example.com"),
    };

    private static readonly string[] FirstNames =
    {
        "Olivia", "Liam", "Emma", "Noah", "Ava", "Elijah", "Sophia", "James", "Isabella", "Lucas", "Mia", "Mason", "Amelia", "Ethan", "Harper", "Aiden",
        "Fatima", "Ahmed", "Ayesha", "Omar", "Zara", "Bilal", "Hira", "Hamza", "Layla", "Yusuf", "Mariam", "Ali", "Sara", "Hassan", "Noor", "Imran",
        "Chloe", "Oscar", "Grace", "Harry", "Freya", "George", "Isla", "Jack", "Priya", "Arjun", "Mei", "Kenji", "Lucia", "Mateo", "Elena", "Diego",
    };

    private static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Brown", "Taylor", "Wilson", "Davies", "Evans", "Thomas", "Khan", "Ahmed", "Hussain", "Malik", "Qureshi", "Siddiqui", "Sheikh",
        "Rahman", "Al Mansoori", "Haddad", "Nasser", "Farouk", "Garcia", "Martinez", "Rossi", "Muller", "Nguyen", "Patel", "Sharma", "Chen", "Kim", "Silva",
    };

    private Random _rng = new(20260923);
    private DateTime _now;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<EmailList>().AnyAsync(l => l.SeedKey == MarkerSeedKey, ct))
        {
            logger.LogInformation("Email demo data already present; skipping");
            return;
        }
        _now = clock.GetUtcNow().UtcDateTime;
        _rng = new Random(20260923);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);

        await EnsureStaffAsync(db, "content", "Casey Content", Role.ContentCreator, ct);
        var am = await EnsureStaffAsync(db, "am", "Amira Malik", Role.AccountManager, ct);
        var clients = new List<ClientAccount>();
        foreach (var c in Clients) clients.Add(await EnsureClientAsync(db, c, am, ct));
        await db.SaveChangesAsync(ct);

        var starters = await db.Set<EmailTemplate>().AsNoTracking().Where(t => t.ScopeKey == Workspace.AgencyKey && t.SeedKey != null).ToListAsync(ct);

        // Agency workspace: settings, sender and the website newsletter list (marker).
        await AddWorkspaceAsync(db, null, "Optimize All", "Optimize All Ltd, 71-75 Shelton Street, London WC2H 9JQ, United Kingdom", "UTC",
            "Optimize All", "hello@optimizeall.example.com", requireApproval: false, ct);
        var newsletter = NewList(db, null, "Website newsletter", "Agency news and marketing insights from the website sign-up form.", NewsletterSubscribedEmailHandler.ListSeedKey);
        newsletter.SeedKey = MarkerSeedKey;
        var agencyContacts = AddContacts(db, null, 60, "UTC", new[] { "US", "GB", "AE", "PK" }, "optimizeall-readers.example.com", withPhones: false);
        Join(db, newsletter, agencyContacts);

        foreach (var (client, spec) in clients.Zip(Clients))
            await SeedClientAsync(db, client, spec, starters, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogWarning("Email demo data created for {Count} clients. Demo/staging data only.", clients.Count);
    }

    private async Task<User> EnsureStaffAsync(AppDbContext db, string local, string name, Role role, CancellationToken ct)
    {
        var email = $"{local}@demo.optimizeall.app";
        var normalized = Normalization.Email(email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is not null)
        {
            if (!user.Roles.Any(r => r.Role == role)) user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
            return user;
        }
        user = new User
        {
            Email = email, NormalizedEmail = normalized, DisplayName = name, CountryCode = "GB", LanguageCode = "en", TimeZone = "Europe/London",
            EmailVerifiedAt = _now, ReferralCode = ("EM" + Guid.NewGuid().ToString("N")[..8]).ToUpperInvariant(),
        };
        user.PasswordHash = hasher.HashPassword(user, DemoPassword);
        user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now });
        db.Set<User>().Add(user);
        return user;
    }

    private async Task<ClientAccount> EnsureClientAsync(AppDbContext db, DemoClient c, User am, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().FirstOrDefaultAsync(x => x.Slug == c.Slug, ct);
        if (client is not null) return client;
        client = new ClientAccount
        {
            Name = c.Name, Slug = c.Slug, Industry = c.Industry, CountryCode = c.Country, Currency = c.Currency, TimeZone = c.TimeZone,
            Status = ClientAccountStatus.Active, Website = "https://" + c.Domain, BillingAddress = c.Address, AccountManagerUserId = am.Id,
        };
        db.Set<ClientAccount>().Add(client);
        return client;
    }

    private async Task<SenderProfile> AddWorkspaceAsync(AppDbContext db, Guid? clientId, string org, string address, string tz, string fromName, string fromEmail,
        bool requireApproval, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        // Settings may already exist (created lazily when someone opened the workspace before the demo seed).
        await db.Set<EmailWorkspaceSettings>().Where(x => x.ScopeKey == key).ExecuteDeleteAsync(ct);
        db.Set<EmailWorkspaceSettings>().Add(new EmailWorkspaceSettings
        {
            ClientAccountId = clientId, ScopeKey = key, OrganizationName = org, PhysicalAddress = address, DefaultTimeZone = tz, RequireClientApproval = requireApproval,
            CostCurrency = "USD",
        });
        var sender = new SenderProfile
        {
            ClientAccountId = clientId, ScopeKey = key, FromName = fromName, FromEmail = fromEmail, ReplyTo = fromEmail.Replace("hello@", "support@"),
            IsDefault = true, VerifiedAt = _now.AddDays(-120),
        };
        db.Set<SenderProfile>().Add(sender);
        return sender;
    }

    private EmailList NewList(AppDbContext db, Guid? clientId, string name, string description, string seedKey, bool doubleOptIn = true)
    {
        var list = new EmailList
        {
            ClientAccountId = clientId, ScopeKey = Workspace.Key(clientId), Name = name, Description = description, DoubleOptIn = doubleOptIn,
            PublicKey = AudienceService.NewPublicKey(), SeedKey = seedKey, CreatedAt = _now.AddDays(-200),
        };
        db.Set<EmailList>().Add(list);
        return list;
    }

    private List<Subscriber> AddContacts(AppDbContext db, Guid? clientId, int count, string tz, string[] countries, string domain, bool withPhones)
    {
        var key = Workspace.Key(clientId);
        var result = new List<Subscriber>();
        var used = new HashSet<string>();
        for (var i = 0; i < count; i++)
        {
            var first = FirstNames[_rng.Next(FirstNames.Length)];
            var last = LastNames[_rng.Next(LastNames.Length)];
            var local = $"{first}.{last}".ToLowerInvariant().Replace(' ', '-');
            var email = $"{local}{i}@{domain}";
            if (!used.Add(email)) continue;
            var created = _now.AddDays(-_rng.Next(1, 190)).AddMinutes(-_rng.Next(0, 1440));
            var country = countries[_rng.Next(countries.Length)];
            var s = new Subscriber
            {
                ClientAccountId = clientId, ScopeKey = key, Email = email, NormalizedEmail = email, FirstName = first, LastName = last,
                Language = country == "AE" && _rng.NextDouble() < 0.3 ? "ar" : country == "PK" && _rng.NextDouble() < 0.3 ? "ur" : "en",
                CountryCode = country, TimeZone = tz, Source = _rng.NextDouble() < 0.6 ? "import" : "form", Status = SubscriberStatus.Subscribed,
                EmailConsent = ConsentStatus.Granted, EmailConsentAt = created, CreatedAt = created,
                Phone = withPhones ? $"+92300{_rng.Next(1000000, 9999999)}" : null,
            };
            if (withPhones) { s.SmsConsent = _rng.NextDouble() < 0.85 ? ConsentStatus.Granted : ConsentStatus.Unknown; s.SmsConsentAt = created; }
            db.Set<Subscriber>().Add(s);
            db.Set<ConsentRecord>().Add(new ConsentRecord
            {
                SubscriberId = s.Id, ClientAccountId = clientId, Channel = MessageChannel.Email, Status = ConsentStatus.Granted, RecordedAt = created,
                Source = s.Source, ConsentTextVersion = "v1", Note = s.Source == "import" ? "Demo import: opted in at checkout" : "Demo sign-up form",
            });
            if (_rng.NextDouble() < 0.5)
                db.Set<SubscriberField>().Add(new SubscriberField { SubscriberId = s.Id, Key = "birthday", Value = new DateOnly(1970 + _rng.Next(0, 35), _rng.Next(1, 13), _rng.Next(1, 29)).ToString("yyyy-MM-dd") });
            if (_rng.NextDouble() < 0.4) db.Set<SubscriberTag>().Add(new SubscriberTag { SubscriberId = s.Id, Tag = _rng.NextDouble() < 0.5 ? "vip" : "customer", AddedAt = created });
            result.Add(s);
        }
        return result;
    }

    private static void Join(AppDbContext db, EmailList list, IEnumerable<Subscriber> contacts)
    {
        foreach (var s in contacts)
            db.Set<ListMembership>().Add(new ListMembership
            {
                ListId = list.Id, SubscriberId = s.Id, Status = MembershipStatus.Subscribed, Source = s.Source, CreatedAt = s.CreatedAt, SubscribedAt = s.CreatedAt,
                ConfirmedAt = s.Source == "form" ? s.CreatedAt.AddMinutes(4) : null,
            });
    }

    private async Task SeedClientAsync(AppDbContext db, ClientAccount client, DemoClient spec, List<EmailTemplate> starters, CancellationToken ct)
    {
        var id = client.Id;
        var sender = await AddWorkspaceAsync(db, id, spec.Name, spec.Address, spec.TimeZone, spec.Name, $"hello@{spec.Domain}", requireApproval: spec.Slug == "aurora-skincare", ct);
        var templates = new Dictionary<string, Guid>();
        foreach (var t in starters)
        {
            var copy = new EmailTemplate
            {
                ClientAccountId = id, ScopeKey = Workspace.Key(id), Name = $"{spec.Name} — {t.Name}", Category = t.Category, Subject = t.Subject,
                PreviewText = t.PreviewText, DesignJson = t.DesignJson.Replace("www.example.com", spec.Domain), SeedKey = t.SeedKey,
            };
            db.Set<EmailTemplate>().Add(copy);
            templates[t.SeedKey!] = copy.Id;
        }
        string Design(string key) => starters.First(s => s.SeedKey == key).DesignJson.Replace("www.example.com", spec.Domain);

        switch (spec.Slug)
        {
            case "nimbus-fitness":
            {
                var members = NewList(db, id, "App members", "Everyone with a Nimbus account who opted in to emails.", "demo-members", doubleOptIn: false);
                var updates = NewList(db, id, "Product updates", "Release notes and feature announcements.", "demo-updates");
                var contacts = AddContacts(db, id, 280, spec.TimeZone, new[] { "US", "US", "US", "CA", "GB" }, "mail.example.com", withPhones: false);
                Join(db, members, contacts);
                Join(db, updates, contacts.Take(120));
                SimulateSent(db, NewCampaign(db, id, "September product update", sender, members, "Your September Nimbus update", Design("starter-newsletter"), CampaignStatus.Sent, _now.AddDays(-21)),
                    contacts, _now.AddDays(-21), 0.41, 0.07, spec, abTest: true);
                SimulateSent(db, NewCampaign(db, id, "Spring challenge kickoff", sender, members, "{{first_name|Hey}}, the 30-day challenge starts Monday", Design("starter-event-invite"), CampaignStatus.Sent, _now.AddDays(-9)),
                    contacts, _now.AddDays(-9), 0.37, 0.05, spec);
                var scheduled = NewCampaign(db, id, "October webinar invite", sender, members, "Live: build a routine that sticks", Design("starter-event-invite"), CampaignStatus.Scheduled, null);
                scheduled.ScheduleMode = ScheduleMode.FixedTime;
                scheduled.ScheduledAt = _now.Date.AddDays(5).AddHours(15);
                scheduled.SendConfirmedAt = _now.AddHours(-3);
                NewCampaign(db, id, "Black Friday early access", sender, members, "Early access: 40% off Nimbus Pro", Design("starter-promo"), CampaignStatus.Draft, null);
                var sending = NewCampaign(db, id, "Re-engagement wave 1", sender, members, "Is this goodbye, {{first_name|friend}}?", Design("starter-reengagement"), CampaignStatus.Sending, _now.AddHours(-2));
                sending.ScheduleMode = ScheduleMode.RecipientTimeZone;
                sending.ScheduledLocalTime = _now.AddDays(3).ToString("yyyy-MM-dd'T'10:00");
                SimulatePartial(db, sending, contacts.Take(150).ToList(), sentShare: 0.5, pendingDue: _now.AddDays(3));
                AddJourneys(db, id, templates, members.Id, sender.Id, active: "journey-welcome", contacts);
                break;
            }
            case "wanderly-travel":
            {
                var inspiration = NewList(db, id, "Travel inspiration", "Weekly destination ideas and deals.", "demo-inspiration");
                var vip = NewList(db, id, "VIP travellers", "Past bookers with 3+ trips.", "demo-vip");
                var contacts = AddContacts(db, id, 240, spec.TimeZone, new[] { "GB", "GB", "IE", "FR", "DE" }, "post.example.com", withPhones: false);
                Join(db, inspiration, contacts);
                Join(db, vip, contacts.Where((_, i) => i % 4 == 0));
                SimulateSent(db, NewCampaign(db, id, "Autumn escapes", sender, inspiration, "Autumn escapes from £199 — this week only", Design("starter-promo"), CampaignStatus.Sent, _now.AddDays(-14)),
                    contacts, _now.AddDays(-14), 0.44, 0.06, spec);
                var paused = NewCampaign(db, id, "Winter sun deals", sender, inspiration, "Swap grey skies for 25°C", Design("starter-promo"), CampaignStatus.Paused, _now.AddDays(-1));
                paused.PausedAt = _now.AddHours(-20);
                paused.PauseReason = "Paused to correct a price in the hero offer.";
                SimulatePartial(db, paused, contacts, sentShare: 0.3, pendingDue: _now.AddHours(-20));
                NewCampaign(db, id, "Newsletter — November", sender, inspiration, "Where to go in November", Design("starter-newsletter"), CampaignStatus.Draft, null);
                var cancelled = NewCampaign(db, id, "Summer sale (duplicate)", sender, inspiration, "Summer sale: up to 30% off", Design("starter-promo"), CampaignStatus.Cancelled, null);
                cancelled.CancelledAt = _now.AddDays(-40);
                AddJourneys(db, id, templates, inspiration.Id, sender.Id, active: "journey-reengagement", contacts);
                break;
            }
            case "aurora-skincare":
            {
                var insiders = NewList(db, id, "Beauty insiders", "Newsletter and launch alerts for Aurora customers.", "demo-insiders");
                var contacts = AddContacts(db, id, 300, spec.TimeZone, new[] { "AE", "AE", "SA", "KW", "QA" }, "inbox.example.com", withPhones: false);
                Join(db, insiders, contacts);
                SimulateSent(db, NewCampaign(db, id, "New Vitamin C serum launch", sender, insiders, "Meet the serum 12,000 people waitlisted for", Design("starter-promo"), CampaignStatus.Sent, _now.AddDays(-12)),
                    contacts, _now.AddDays(-12), 0.39, 0.09, spec, conversions: true);
                var approval = NewCampaign(db, id, "Weekend flash sale", sender, insiders, "48 hours only: 25% off bestsellers", Design("starter-promo"), CampaignStatus.Scheduled, null);
                approval.ScheduleMode = ScheduleMode.FixedTime;
                approval.ScheduledAt = _now.Date.AddDays(3).AddHours(7);
                approval.ApprovalStatus = ApprovalStatus.Pending;
                approval.SendConfirmedAt = _now.AddHours(-5);
                NewCampaign(db, id, "Holiday gift guide", sender, insiders, "The Aurora holiday gift guide", Design("starter-newsletter"), CampaignStatus.Draft, null);
                AddJourneys(db, id, templates, insiders.Id, sender.Id, active: "journey-abandoned-cart", contacts);
                break;
            }
            default:
            {
                var loyalty = NewList(db, id, "Loyalty club", "Karachi Eats loyalty members (email + SMS).", "demo-loyalty", doubleOptIn: false);
                var contacts = AddContacts(db, id, 220, spec.TimeZone, new[] { "PK" }, "webmail.example.com", withPhones: true);
                Join(db, loyalty, contacts);
                SimulateSent(db, NewCampaign(db, id, "Ramadan iftar menu", sender, loyalty, "Our iftar menu is here, {{first_name|friend}}", Design("starter-event-invite"), CampaignStatus.Sent, _now.AddDays(-30)),
                    contacts, _now.AddDays(-30), 0.33, 0.06, spec);
                var sms = new EmailCampaign
                {
                    ClientAccountId = id, ScopeKey = Workspace.Key(id), Name = "Friday biryani deal (SMS)", Channel = MessageChannel.Sms, Status = CampaignStatus.Sent,
                    ListId = loyalty.Id, SmsBody = "Karachi Eats: Friday deal! 2 biryani + 2 drinks for Rs 1,999. Order: karachieats.example.com/deal Reply STOP to opt out",
                    SendStartedAt = _now.AddDays(-4), ExpandedAt = _now.AddDays(-4), CompletedAt = _now.AddDays(-4).AddMinutes(20), SendConfirmedAt = _now.AddDays(-4),
                    CreatedAt = _now.AddDays(-6),
                };
                db.Set<EmailCampaign>().Add(sms);
                var segments = SmsSegments.Calculate(sms.SmsBody).Segments;
                var n = 0;
                foreach (var s in contacts.Where(c => c.SmsConsent == ConsentStatus.Granted && c.Phone is not null))
                {
                    var failed = _rng.NextDouble() < 0.03;
                    db.Set<CampaignRecipient>().Add(new CampaignRecipient
                    {
                        CampaignId = sms.Id, ClientAccountId = id, SubscriberId = s.Id, Channel = MessageChannel.Sms, Address = s.Phone!, Status = RecipientStatus.Sent,
                        SentAt = sms.SendStartedAt!.Value.AddSeconds(n++), DeliveredAt = failed ? null : sms.SendStartedAt!.Value.AddSeconds(n + 5), Segments = segments,
                        Cost = segments * 0.0079m, BounceType = failed ? BounceType.Soft : null, BouncedAt = failed ? sms.SendStartedAt : null,
                        ProviderMessageId = "SM" + Guid.NewGuid().ToString("N"), CreatedAt = sms.SendStartedAt!.Value, Attempts = 1,
                    });
                }
                sms.RecipientCount = n;
                var stopper = contacts.First(c => c.Phone is not null);
                stopper.SmsConsent = ConsentStatus.Withdrawn;
                db.Set<Suppression>().Add(new Suppression
                {
                    ClientAccountId = id, ScopeKey = Workspace.Key(id), Channel = MessageChannel.Sms, Value = stopper.Phone!, Reason = SuppressionReason.StopKeyword,
                    Source = "sms-stop", CreatedAt = _now.AddDays(-4).AddHours(1),
                });
                AddJourneys(db, id, templates, loyalty.Id, sender.Id, active: null, contacts);
                break;
            }
        }
    }

    private EmailCampaign NewCampaign(AppDbContext db, Guid clientId, string name, SenderProfile sender, EmailList list, string subject, string designJson,
        CampaignStatus status, DateTime? startedAt)
    {
        var c = new EmailCampaign
        {
            ClientAccountId = clientId, ScopeKey = Workspace.Key(clientId), Name = name, Channel = MessageChannel.Email, Status = status, ListId = list.Id,
            SenderProfileId = sender.Id, Subject = subject, DesignJson = designJson, ThrottlePerMinute = 600,
            SendStartedAt = startedAt, SendConfirmedAt = startedAt?.AddMinutes(-10), CreatedAt = (startedAt ?? _now).AddDays(-3),
        };
        db.Set<EmailCampaign>().Add(c);
        return c;
    }

    private Dictionary<string, Guid> Links(AppDbContext db, EmailCampaign c)
    {
        var map = new Dictionary<string, Guid>();
        var position = 0;
        foreach (var url in EmailRenderer.ExtractLinks(EmailDesign.Parse(c.DesignJson)))
        {
            var link = new TrackedLink { ClientAccountId = c.ClientAccountId, SourceKey = CampaignSendJob.SourceKey(c.Id), Url = url, UrlHash = Text.UrlHash(url), Position = position++, CreatedAt = c.SendStartedAt ?? _now };
            db.Set<TrackedLink>().Add(link);
            map[url] = link.Id;
        }
        return map;
    }

    private static readonly (DeviceType Device, string Client, double Weight)[] Clients2 =
    {
        (DeviceType.Mobile, "Apple Mail (iOS)", 0.34), (DeviceType.Desktop, "Gmail", 0.22), (DeviceType.Mobile, "Gmail", 0.16), (DeviceType.Desktop, "Outlook", 0.14),
        (DeviceType.Mobile, "Android", 0.08), (DeviceType.Tablet, "Apple Mail (iOS)", 0.06),
    };

    private (DeviceType, string) PickClient()
    {
        var x = _rng.NextDouble();
        foreach (var c in Clients2) { if (x < c.Weight) return (c.Device, c.Client); x -= c.Weight; }
        return (DeviceType.Desktop, "Gmail");
    }

    /// <summary>Creates recipients and engagement for a completed send, applying bounces/unsubscribes/complaints to the contacts.</summary>
    private void SimulateSent(AppDbContext db, EmailCampaign c, List<Subscriber> audience, DateTime sentAt, double openRate, double clickRate, DemoClient spec,
        bool abTest = false, bool conversions = false)
    {
        var links = Links(db, c);
        var linkIds = links.Values.ToList();
        c.ExpandedAt = sentAt;
        c.CompletedAt = sentAt.AddMinutes(35);
        if (abTest)
        {
            c.Type = CampaignType.AbTest;
            c.AbTestPercent = 20;
            c.AbWinnerMetric = AbWinnerMetric.OpenRate;
            c.AbWaitHours = 4;
            c.AbWinnerVariant = "B";
            c.AbTestCompletedAt = sentAt.AddMinutes(10);
            c.AbDecidedAt = sentAt.AddHours(4).AddMinutes(10);
            db.Set<CampaignVariant>().Add(new CampaignVariant { CampaignId = c.Id, Key = "A", Subject = c.Subject });
            db.Set<CampaignVariant>().Add(new CampaignVariant { CampaignId = c.Id, Key = "B", Subject = "New: guided workouts are here" });
        }
        var key = Workspace.Key(c.ClientAccountId);
        var order = 0;
        var count = 0;
        foreach (var s in audience.Where(x => x.Status == SubscriberStatus.Subscribed && x.EmailConsent == ConsentStatus.Granted))
        {
            count++;
            var variant = abTest ? AbTesting.Assign(c.Id, s.Id, 20, new[] { "A", "B" }) : null;
            var isTest = variant is not null;
            if (abTest && variant is null) variant = "B";
            var at = isTest || !abTest ? sentAt.AddSeconds(order++) : sentAt.AddHours(4).AddMinutes(15).AddSeconds(order++);
            var r = new CampaignRecipient
            {
                CampaignId = c.Id, ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, Channel = MessageChannel.Email, Address = s.NormalizedEmail!, Variant = variant,
                IsTestCohort = isTest, Status = RecipientStatus.Sent, SentAt = at, CreatedAt = sentAt, Attempts = 1, ProviderMessageId = $"<{Guid.NewGuid():N}@{spec.Domain}>",
            };
            db.Set<CampaignRecipient>().Add(r);
            s.LastSentAt = at;
            var roll = _rng.NextDouble();
            if (roll < 0.008)
            {
                r.BounceType = BounceType.Hard;
                r.BouncedAt = at.AddMinutes(1);
                s.Status = SubscriberStatus.Bounced;
                db.Set<Suppression>().Add(new Suppression { ClientAccountId = c.ClientAccountId, ScopeKey = key, Channel = MessageChannel.Email, Value = s.NormalizedEmail!, Reason = SuppressionReason.HardBounce, Source = "demo", CreatedAt = at.AddMinutes(1) });
                continue;
            }
            if (roll < 0.018) { r.BounceType = BounceType.Soft; r.BouncedAt = at.AddMinutes(2); s.SoftBounceCount++; continue; }

            var rate = variant == "B" ? openRate * 1.12 : openRate;
            if (_rng.NextDouble() < 0.12)
            {
                r.MachineOpenCount = 1;
                db.Set<EngagementEvent>().Add(new EngagementEvent { ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, CampaignId = c.Id, RecipientId = r.Id, Type = EngagementType.Open, OccurredAt = at.AddSeconds(40), IsMachine = true, MailClient = "Apple Mail (Privacy Protection)", Detail = "Apple Mail Privacy Protection prefetch" });
            }
            if (_rng.NextDouble() >= rate) continue;
            var (device, client) = PickClient();
            var openedAt = at.AddMinutes(_rng.Next(2, 60 * 36));
            r.OpenedAt = openedAt;
            r.OpenCount = 1 + (_rng.NextDouble() < 0.3 ? 1 : 0);
            s.LastOpenAt = openedAt > (s.LastOpenAt ?? DateTime.MinValue) ? openedAt : s.LastOpenAt;
            for (var o = 0; o < r.OpenCount; o++)
                db.Set<EngagementEvent>().Add(new EngagementEvent { ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, CampaignId = c.Id, RecipientId = r.Id, Type = EngagementType.Open, OccurredAt = openedAt.AddMinutes(o * 90), Device = device, MailClient = client });
            if (_rng.NextDouble() < 0.004)
            {
                r.UnsubscribedAt = openedAt.AddMinutes(1);
                s.Status = SubscriberStatus.Unsubscribed;
                s.EmailConsent = ConsentStatus.Withdrawn;
                db.Set<EngagementEvent>().Add(new EngagementEvent { ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, CampaignId = c.Id, RecipientId = r.Id, Type = EngagementType.Unsubscribe, OccurredAt = openedAt.AddMinutes(1), Detail = "one-click" });
                db.Set<Suppression>().Add(new Suppression { ClientAccountId = c.ClientAccountId, ScopeKey = key, Channel = MessageChannel.Email, Value = s.NormalizedEmail!, Reason = SuppressionReason.Unsubscribed, Source = "one-click", CreatedAt = openedAt.AddMinutes(1) });
                continue;
            }
            if (linkIds.Count == 0 || _rng.NextDouble() >= clickRate / Math.Max(rate, 0.01)) continue;
            var clickedAt = openedAt.AddMinutes(_rng.Next(1, 15));
            r.ClickedAt = clickedAt;
            r.ClickCount = 1 + (_rng.NextDouble() < 0.25 ? 1 : 0);
            s.LastClickAt = clickedAt;
            for (var k = 0; k < r.ClickCount; k++)
            {
                var linkId = linkIds[_rng.NextDouble() < 0.6 ? 0 : _rng.Next(linkIds.Count)];
                db.Set<EngagementEvent>().Add(new EngagementEvent { ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, CampaignId = c.Id, RecipientId = r.Id, LinkId = linkId, Type = EngagementType.Click, OccurredAt = clickedAt.AddMinutes(k * 3), Device = device, MailClient = client });
            }
            if (conversions && _rng.NextDouble() < 0.28)
            {
                r.ConvertedAt = clickedAt.AddMinutes(_rng.Next(5, 240));
                db.Set<EngagementEvent>().Add(new EngagementEvent
                {
                    ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, CampaignId = c.Id, RecipientId = r.Id, Type = EngagementType.Conversion, OccurredAt = r.ConvertedAt.Value,
                    Value = _rng.Next(12, 46) * 10m, Currency = spec.Currency, ExternalReference = $"DEMO-{spec.Slug}-{order}", DedupKey = $"conv:{key}:DEMO-{c.Id:N}-{order}",
                });
            }
        }
        c.RecipientCount = count;
    }

    private void SimulatePartial(AppDbContext db, EmailCampaign c, List<Subscriber> audience, double sentShare, DateTime pendingDue)
    {
        Links(db, c);
        c.ExpandedAt = c.SendStartedAt;
        var eligible = audience.Where(x => x.Status == SubscriberStatus.Subscribed && x.EmailConsent == ConsentStatus.Granted).ToList();
        var sentCount = (int)(eligible.Count * sentShare);
        for (var i = 0; i < eligible.Count; i++)
        {
            var s = eligible[i];
            var sent = i < sentCount;
            var at = (c.SendStartedAt ?? _now).AddSeconds(i);
            db.Set<CampaignRecipient>().Add(new CampaignRecipient
            {
                CampaignId = c.Id, ClientAccountId = c.ClientAccountId, SubscriberId = s.Id, Channel = MessageChannel.Email, Address = s.NormalizedEmail!,
                Status = sent ? RecipientStatus.Sent : RecipientStatus.Pending, SentAt = sent ? at : null, DueAt = sent ? at : pendingDue, CreatedAt = at,
                Attempts = sent ? 1 : 0, OpenedAt = sent && _rng.NextDouble() < 0.3 ? at.AddMinutes(30) : null,
            });
        }
        c.RecipientCount = eligible.Count;
    }

    private void AddJourneys(AppDbContext db, Guid clientId, Dictionary<string, Guid> templates, Guid listId, Guid senderId, string? active, List<Subscriber> contacts)
    {
        foreach (var journey in ReadyMadeJourneys.Build(clientId, templates, listId, senderId))
        {
            journey.Automation.Status = journey.Automation.SeedKey == active ? AutomationStatus.Active : AutomationStatus.Draft;
            db.Set<Automation>().Add(journey.Automation);
            db.Set<AutomationStep>().AddRange(journey.Steps);
            if (journey.Automation.Status != AutomationStatus.Active) continue;

            // A handful of contacts at different stages of the journey.
            var firstStep = journey.Steps[0];
            var secondStep = journey.Steps.Count > 1 ? journey.Steps[1] : firstStep;
            foreach (var (s, i) in contacts.Take(24).Select((s, i) => (s, i)))
            {
                var entered = _now.AddDays(-(i % 12) - 1);
                var completed = i % 3 == 0;
                var e = new AutomationEnrollment
                {
                    AutomationId = journey.Automation.Id, ClientAccountId = clientId, SubscriberId = s.Id, Iteration = 1, EnteredAt = entered,
                    Status = completed ? EnrollmentStatus.Completed : EnrollmentStatus.Active, CurrentStepKey = completed ? null : secondStep.Key,
                    NextRunAt = completed ? entered : _now.AddDays(1 + i % 3), FinishedAt = completed ? entered.AddDays(5) : null, StepsExecuted = completed ? journey.Steps.Count : 1,
                };
                db.Set<AutomationEnrollment>().Add(e);
                if (firstStep.Type == AutomationStepType.SendEmail)
                    db.Set<AutomationStepRun>().Add(new AutomationStepRun
                    {
                        EnrollmentId = e.Id, AutomationId = journey.Automation.Id, ClientAccountId = clientId, SubscriberId = s.Id, StepKey = firstStep.Key,
                        StepType = firstStep.Type, Status = StepRunStatus.Completed, StartedAt = entered, FinishedAt = entered, Channel = MessageChannel.Email,
                        Address = s.NormalizedEmail, SentAt = entered, OpenedAt = i % 2 == 0 ? entered.AddHours(3) : null, ClickedAt = i % 5 == 0 ? entered.AddHours(3).AddMinutes(4) : null,
                        ProviderMessageId = $"<{Guid.NewGuid():N}@demo>",
                    });
                else if (firstStep.Type == AutomationStepType.Wait)
                    db.Set<AutomationStepRun>().Add(new AutomationStepRun
                    {
                        EnrollmentId = e.Id, AutomationId = journey.Automation.Id, ClientAccountId = clientId, SubscriberId = s.Id, StepKey = firstStep.Key,
                        StepType = firstStep.Type, Status = StepRunStatus.Completed, StartedAt = entered, FinishedAt = entered.AddHours(1), Detail = "waited",
                    });
            }
        }
    }
}
