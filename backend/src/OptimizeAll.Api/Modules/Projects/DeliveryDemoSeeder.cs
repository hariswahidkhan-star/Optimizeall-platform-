using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Clients;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Domain.Settings;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>
/// "Demo" seed profile, Order 200: the canonical agency demo data other modules rely on — the four client accounts, the
/// delivery staff and client users of <see cref="DeliveryDemoData"/> — plus realistic projects, tasks, deliverables in every
/// approval state, time entries and timesheets, briefs, reports, messages, meetings and feedback.
/// Idempotent: users and clients are ensured by email/slug (created only when missing); the delivery dataset is created
/// once (marker setting <see cref="MarkerKey"/>). STAGING / DEMO DATA ONLY.
/// </summary>
public sealed class DeliveryDemoSeeder(
    TimeProvider clock,
    IPasswordHasher<User> hasher,
    IFileStorage storage,
    IDatabaseDialect dialect,
    ILogger<DeliveryDemoSeeder> logger) : ISeeder
{
    public const string MarkerKey = "demo.delivery.seeded";

    public string Profile => "Demo";
    public int Order => 200;

    private readonly List<string> _writtenKeys = new();
    private readonly Random _rng = new(20260923);
    private DateTime _now;
    private DateOnly _today;

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        _now = clock.GetUtcNow().UtcDateTime;
        _today = DateOnly.FromDateTime(_now);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        try
        {
            var people = await EnsureUsersAsync(db, ct);
            var clients = await EnsureClientsAsync(db, people, ct);
            await db.SaveChangesAsync(ct);

            if (!await db.Set<SystemSetting>().AnyAsync(s => s.Key == MarkerKey, ct))
            {
                await SeedDeliveryAsync(db, people, clients, ct);
                db.Set<SystemSetting>().Add(new SystemSetting
                {
                    Key = MarkerKey, UpdatedAt = _now,
                    ValueJson = JsonSerializer.Serialize(new { seededAt = _now, profile = "Demo", version = 1 }),
                    Description = "Marker written by the delivery Demo seed (staging/demo data only).",
                });
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Delivery demo data created (demo/staging data only)");
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            foreach (var key in _writtenKeys)
            {
                try { storage.Delete(key); } catch (IOException) { }
            }
            throw;
        }
        db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------ users & clients (ensured every run)

    private async Task<Dictionary<string, User>> EnsureUsersAsync(AppDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        var countries = new Dictionary<Role, (string Country, string Tz)>
        {
            [Role.AccountManager] = ("AE", "Asia/Dubai"), [Role.Strategist] = ("GB", "Europe/London"), [Role.ContentCreator] = ("PK", "Asia/Karachi"),
            [Role.Designer] = ("PK", "Asia/Karachi"), [Role.SeoSpecialist] = ("PK", "Asia/Karachi"), [Role.AdsSpecialist] = ("US", "America/New_York"),
            [Role.SocialMediaManager] = ("AE", "Asia/Dubai"), [Role.SalesRep] = ("GB", "Europe/London"),
        };
        foreach (var s in DeliveryDemoData.Staff)
        {
            var (country, tz) = countries[s.Role];
            result[s.Email] = await EnsureUserAsync(db, s.Email, s.DisplayName, s.Role, country, tz, ct);
        }
        foreach (var c in DeliveryDemoData.ClientUsers)
        {
            var client = DeliveryDemoData.Clients.First(x => x.Slug == c.ClientSlug);
            result[c.Email] = await EnsureUserAsync(db, c.Email, c.DisplayName, Role.Client, client.CountryCode, client.TimeZone, ct);
        }
        var admin = Normalization.Email(DemoAccounts.Admin);
        var adminUser = await db.Set<User>().FirstOrDefaultAsync(u => u.NormalizedEmail == admin, ct);
        if (adminUser is not null) result[DemoAccounts.Admin] = adminUser;
        return result;
    }

    private async Task<User> EnsureUserAsync(AppDbContext db, string email, string name, Role role, string country, string tz, CancellationToken ct)
    {
        var normalized = Normalization.Email(email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = new User
            {
                Email = email, NormalizedEmail = normalized, DisplayName = name, CountryCode = country, TimeZone = tz,
                EmailVerifiedAt = _now.AddDays(-150), ReferralCode = await ReferralCodeAsync(db, email, ct), CreatedAt = _now.AddDays(-150),
                LastLoginAt = _now.AddDays(-1), LastActiveAt = _now.AddHours(-3),
            };
            user.PasswordHash = hasher.HashPassword(user, DeliveryDemoData.Password);
            db.Set<User>().Add(user);
        }
        if (!user.HasRole(role)) user.Roles.Add(new UserRole { UserId = user.Id, Role = role, GrantedAt = _now.AddDays(-150) });
        return user;
    }

    private static async Task<string> ReferralCodeAsync(AppDbContext db, string email, CancellationToken ct)
    {
        var baseCode = "DL" + Normalization.Sha256Hex(email.ToLowerInvariant())[..8].ToUpperInvariant();
        var code = baseCode;
        for (var i = 0; await db.Set<User>().AnyAsync(u => u.ReferralCode == code, ct); i++) code = baseCode[..8] + i.ToString("00");
        return code;
    }

    private async Task<Dictionary<string, ClientAccount>> EnsureClientsAsync(AppDbContext db, Dictionary<string, User> people, CancellationToken ct)
    {
        var am = people[DeliveryDemoData.AccountManager.Email];
        var result = new Dictionary<string, ClientAccount>();
        foreach (var d in DeliveryDemoData.Clients)
        {
            var client = await db.Set<ClientAccount>().FirstOrDefaultAsync(c => c.Slug == d.Slug, ct);
            if (client is null)
            {
                client = new ClientAccount
                {
                    Slug = d.Slug, Name = d.Name, Industry = d.Industry, CountryCode = d.CountryCode, Currency = d.Currency, TimeZone = d.TimeZone,
                    Status = d.Status, Website = d.Website, Summary = d.Summary, AccountManagerUserId = am.Id, StatusChangedAt = _now.AddDays(-90),
                    CreatedAt = _now.AddDays(d.Status == ClientAccountStatus.Onboarding ? -6 : -120),
                    BillingContactName = d.Slug == DeliveryDemoData.Nimbus.Slug ? "Morgan Lee" : null,
                    BillingEmail = d.Slug == DeliveryDemoData.Nimbus.Slug ? DeliveryDemoData.NimbusBilling.Email : null,
                };
                db.Set<ClientAccount>().Add(client);
                var sort = 0;
                foreach (var item in OnboardingChecklistTemplate.Items)
                {
                    var done = d.Status != ClientAccountStatus.Onboarding
                        ? item.Key != "tracking-audit" || d.Slug != DeliveryDemoData.Wanderly.Slug
                        : item.Key is "contract-signed" or "billing-details";
                    db.Set<ClientOnboardingItem>().Add(new ClientOnboardingItem
                    {
                        ClientAccountId = client.Id, Key = item.Key, Title = item.Title, Description = item.Description, Category = item.Category,
                        Owner = item.Owner, SortOrder = sort++, Status = done ? OnboardingItemStatus.Done : OnboardingItemStatus.Pending,
                        CompletedAt = done ? client.CreatedAt.AddDays(sort) : null, CompletedByUserId = done ? am.Id : null,
                    });
                }
                db.Set<BrandKit>().Add(BrandKitFor(client.Id, d.Slug, am.Id));
            }
            result[d.Slug] = client;
        }

        foreach (var cu in DeliveryDemoData.ClientUsers)
        {
            var client = result[cu.ClientSlug];
            var user = people[cu.Email];
            if (!await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == client.Id && m.UserId == user.Id, ct) &&
                !db.ChangeTracker.Entries<ClientMember>().Any(e => e.Entity.ClientAccountId == client.Id && e.Entity.UserId == user.Id))
                db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = client.Id, UserId = user.Id, Role = cu.Duty, AddedAt = _now.AddDays(-100), AddedByUserId = am.Id });
        }

        var teams = new Dictionary<string, (DeliveryDemoData.DemoStaff Staff, ClientServiceRole Role)[]>
        {
            [DeliveryDemoData.Nimbus.Slug] = new[]
            {
                (DeliveryDemoData.AccountManager, ClientServiceRole.AccountManager), (DeliveryDemoData.Strategist, ClientServiceRole.Strategist),
                (DeliveryDemoData.Seo, ClientServiceRole.Seo), (DeliveryDemoData.Social, ClientServiceRole.Social),
                (DeliveryDemoData.Content, ClientServiceRole.Content), (DeliveryDemoData.Designer, ClientServiceRole.Design),
                (DeliveryDemoData.Ads, ClientServiceRole.Ads),
            },
            [DeliveryDemoData.Wanderly.Slug] = new[]
            {
                (DeliveryDemoData.AccountManager, ClientServiceRole.AccountManager), (DeliveryDemoData.Strategist, ClientServiceRole.Strategist),
                (DeliveryDemoData.Designer, ClientServiceRole.Design), (DeliveryDemoData.Content, ClientServiceRole.Content),
                (DeliveryDemoData.Seo, ClientServiceRole.Seo),
            },
            [DeliveryDemoData.Aurora.Slug] = new[]
            {
                (DeliveryDemoData.AccountManager, ClientServiceRole.AccountManager), (DeliveryDemoData.Ads, ClientServiceRole.Ads),
                (DeliveryDemoData.Social, ClientServiceRole.Social), (DeliveryDemoData.Designer, ClientServiceRole.Design),
            },
            [DeliveryDemoData.KarachiEats.Slug] = new[]
            {
                (DeliveryDemoData.AccountManager, ClientServiceRole.AccountManager), (DeliveryDemoData.Strategist, ClientServiceRole.Strategist),
                (DeliveryDemoData.Social, ClientServiceRole.Social),
            },
        };
        foreach (var (slug, members) in teams)
        {
            var client = result[slug];
            foreach (var (staff, role) in members)
            {
                var user = people[staff.Email];
                if (!await db.Set<ClientTeamAssignment>().AnyAsync(a => a.ClientAccountId == client.Id && a.UserId == user.Id && a.ServiceRole == role, ct) &&
                    !db.ChangeTracker.Entries<ClientTeamAssignment>().Any(e => e.Entity.ClientAccountId == client.Id && e.Entity.UserId == user.Id && e.Entity.ServiceRole == role))
                    db.Set<ClientTeamAssignment>().Add(new ClientTeamAssignment
                    {
                        ClientAccountId = client.Id, UserId = user.Id, ServiceRole = role, IsPrimary = true, AssignedAt = _now.AddDays(-100),
                    });
            }
        }
        return result;
    }

    private static BrandKit BrandKitFor(Guid clientId, string slug, Guid by) => slug switch
    {
        "nimbus-fitness" => new BrandKit
        {
            ClientAccountId = clientId, UpdatedByUserId = by,
            Colors = new() { new("Nimbus Blue", "#2F6BFF"), new("Energy Lime", "#B6F23A"), new("Graphite", "#1C1F26"), new("Cloud", "#F4F6FA") },
            Fonts = new() { "Poppins (headings)", "Inter (body)" },
            ToneOfVoice = "Encouraging, energetic and practical. Talk like a supportive coach: short sentences, second person, no shaming or extreme claims.",
            Personas = new()
            {
                new("Busy Priya, 32", "Product manager with 20 minutes a day. Wants quick home workouts and simple meal plans; motivated by streaks."),
                new("Beginner Ben, 45", "Returning to exercise after years off. Needs low-impact programmes, reassurance and visible progress."),
            },
            Competitors = new() { "Peloton App", "Nike Training Club", "Freeletics" },
            Dos = new() { "Show real people of different body types", "Lead with the benefit, then the feature", "Use metric and imperial units" },
            Donts = new() { "Before/after body shots", "Medical or weight-loss guarantees", "Stock photos of empty gyms" },
            KeyMessages = new() { "Fitness that fits your day", "Coach-led plans from 10 minutes", "Cancel anytime — first 14 days free" },
        },
        "aurora-skincare" => new BrandKit
        {
            ClientAccountId = clientId, UpdatedByUserId = by,
            Colors = new() { new("Aurora Rose", "#E8A4A0"), new("Sand", "#F3E7DA"), new("Deep Plum", "#4A2545") },
            Fonts = new() { "Cormorant Garamond", "Montserrat" }, ToneOfVoice = "Calm, expert and inclusive. Bilingual (English/Arabic) where possible.",
            Competitors = new() { "Huda Beauty Skin", "The Ordinary", "Glossier" },
            Dos = new() { "Mention ingredients and skin types", "Include Arabic captions for GCC audiences" },
            Donts = new() { "Claims like 'cures acne'", "Filters on skin close-ups" },
            KeyMessages = new() { "Clean formulas for desert climates", "Free delivery across the UAE" },
        },
        "wanderly-travel" => new BrandKit
        {
            ClientAccountId = clientId, UpdatedByUserId = by, Colors = new() { new("Wander Teal", "#0F7C80"), new("Sunset", "#F28C38") },
            Fonts = new() { "DM Serif Display", "DM Sans" }, ToneOfVoice = "Curious, warm, well-travelled. Specific places, sensory detail.",
            KeyMessages = new() { "Small groups, big experiences", "Local guides in every destination" },
        },
        _ => new BrandKit { ClientAccountId = clientId, UpdatedByUserId = by },
    };

    // ------------------------------------------------------------------ delivery dataset (once)

    private sealed record Ctx(AppDbContext Db, Dictionary<string, User> People, Dictionary<string, ClientAccount> Clients)
    {
        public User P(DeliveryDemoData.DemoStaff s) => People[s.Email];
        public User P(DeliveryDemoData.DemoClientUser c) => People[c.Email];
        public ClientAccount C(DeliveryDemoData.DemoClient c) => Clients[c.Slug];
    }

    private async Task SeedDeliveryAsync(AppDbContext db, Dictionary<string, User> people, Dictionary<string, ClientAccount> clients, CancellationToken ct)
    {
        var x = new Ctx(db, people, clients);
        var templates = await db.Set<ProjectTemplate>().AsNoTracking().ToDictionaryAsync(t => t.Key, ct);
        if (templates.Count == 0)
            foreach (var t in DeliveryBaselineSeeder.ProjectTemplates()) templates[t.Key] = t;
        await EnsureReferenceTemplatesAsync(db, ct);

        SeedRates(db);
        var nimbus = x.C(DeliveryDemoData.Nimbus);
        var wanderly = x.C(DeliveryDemoData.Wanderly);
        var aurora = x.C(DeliveryDemoData.Aurora);
        var karachi = x.C(DeliveryDemoData.KarachiEats);

        var nimbusSeo = Project(x, nimbus, "SEO retainer", ProjectType.SeoProgram, templates["seo-monthly-retainer"], -20, DeliveryDemoData.Seo,
            80, 9600m, new[] { DeliveryDemoData.Content, DeliveryDemoData.Strategist }, overdueLeft: 2);
        var nimbusSocial = Project(x, nimbus, "Social content — monthly", ProjectType.SocialContent, templates["social-monthly-content"], -12,
            DeliveryDemoData.Social, 45, 4500m, new[] { DeliveryDemoData.Designer, DeliveryDemoData.Content }, overdueLeft: 0);
        var nimbusAds = Project(x, nimbus, "Spring app-install campaign", ProjectType.PaidAdsLaunch, templates["google-ads-launch"], -26,
            DeliveryDemoData.Ads, 35, 4200m, new[] { DeliveryDemoData.Designer, DeliveryDemoData.Strategist }, overdueLeft: 1);
        var wanderlyWeb = Project(x, wanderly, "Website rebuild", ProjectType.WebsiteBuild, templates["website-build"], -45,
            DeliveryDemoData.Strategist, 220, 19800m, new[] { DeliveryDemoData.Designer, DeliveryDemoData.Content, DeliveryDemoData.Seo }, overdueLeft: 5);
        Project(x, wanderly, "Email program setup", ProjectType.EmailProgram, templates["email-program-setup"], -4,
            DeliveryDemoData.Content, 60, 4800m, new[] { DeliveryDemoData.Designer }, overdueLeft: 0, status: ProjectStatus.Planning);
        var auroraAds = Project(x, aurora, "Google Ads launch", ProjectType.PaidAdsLaunch, templates["google-ads-launch"], -24,
            DeliveryDemoData.Ads, 35, 15400m, new[] { DeliveryDemoData.Designer }, overdueLeft: 0);
        var auroraSocial = Project(x, aurora, "Social content retainer", ProjectType.SocialContent, templates["social-monthly-content"], -8,
            DeliveryDemoData.Social, 45, 16500m, new[] { DeliveryDemoData.Designer, DeliveryDemoData.Content }, overdueLeft: 0);
        var karachiPlan = Project(x, karachi, "Onboarding & launch plan", ProjectType.OneOffCampaign, null, 2, DeliveryDemoData.Strategist, 20, 450000m,
            new[] { DeliveryDemoData.Social }, overdueLeft: 0, status: ProjectStatus.Planning);
        foreach (var (title, offset, staff) in new[]
                 {
                     ("Kickoff call and goals workshop", 3, DeliveryDemoData.Strategist), ("Audit Instagram and Foodpanda listings", 6, DeliveryDemoData.Social),
                     ("90-day launch plan", 10, DeliveryDemoData.Strategist),
                 })
            AddTask(x, karachiPlan.Project, title, _today.AddDays(offset), ProjectTaskStatus.Todo, x.P(staff), clientVisible: true);

        // A task conversation with an @mention.
        var mentionTask = nimbusSeo.Tasks.First(t => t.Status != ProjectTaskStatus.Done);
        db.Set<TaskComment>().Add(new TaskComment
        {
            TaskId = mentionTask.Id, ClientAccountId = nimbus.Id, AuthorUserId = x.P(DeliveryDemoData.Seo).Id, CreatedAt = _now.AddHours(-20),
            Body = "@Priya Nair can you draft the intro paragraphs for the two posts? Keywords are in the tracking sheet.",
            MentionedUserIds = new() { x.P(DeliveryDemoData.Content).Id },
        });
        db.Set<TaskComment>().Add(new TaskComment
        {
            TaskId = mentionTask.Id, ClientAccountId = nimbus.Id, AuthorUserId = x.P(DeliveryDemoData.Content).Id, CreatedAt = _now.AddHours(-6),
            Body = "On it — first drafts by tomorrow noon.",
        });
        foreach (var (text, done) in new[] { ("Outline approved", true), ("Draft 1", true), ("SEO check (title, meta, links)", false), ("Images sourced", false) })
            db.Set<TaskChecklistItem>().Add(new TaskChecklistItem { TaskId = mentionTask.Id, Text = text, IsDone = done });

        await SeedDeliverablesAsync(x, nimbusSocial, nimbusSeo, nimbusAds, wanderlyWeb, auroraSocial, auroraAds, ct);
        var logo = await FileAsync(x, nimbus.Id, "nimbus-logo.png", DemoPng.Creative(600, 600, 6), "image/png", ".png", x.P(DeliveryDemoData.Designer).Id, ct);
        db.Add(new BrandAsset
        {
            ClientAccountId = nimbus.Id, FileId = logo.Id, Kind = BrandAssetKind.Logo, Label = "Primary logo (PNG)",
            UploadedByUserId = x.P(DeliveryDemoData.NimbusOwner).Id, CreatedAt = _now.AddDays(-100),
        });
        nimbus.LogoFileId = logo.Id;
        SeedTime(x, new[] { nimbusSeo, nimbusSocial, nimbusAds, wanderlyWeb, auroraAds, auroraSocial });
        SeedBriefs(x, nimbus, nimbusSeo);
        SeedReports(x, nimbus, wanderly, aurora);
        await SeedMessagesAsync(x, nimbus, wanderly, ct);
        SeedMeetings(x, nimbus, aurora, karachi, nimbusSocial);
        SeedFeedback(x, nimbus, aurora);
    }

    private static async Task EnsureReferenceTemplatesAsync(AppDbContext db, CancellationToken ct)
    {
        var projectKeys = await db.Set<ProjectTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in DeliveryBaselineSeeder.ProjectTemplates().Where(t => !projectKeys.Contains(t.Key))) db.Add(t);
        var briefKeys = await db.Set<BriefTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in DeliveryBaselineSeeder.BriefTemplates().Where(t => !briefKeys.Contains(t.Key))) db.Add(t);
        var reportKeys = await db.Set<ReportTemplate>().Select(t => t.Key).ToListAsync(ct);
        foreach (var t in DeliveryBaselineSeeder.ReportTemplates().Where(t => !reportKeys.Contains(t.Key))) db.Add(t);
    }

    private static void SeedRates(AppDbContext db)
    {
        var usd = new Dictionary<Role, decimal>
        {
            [Role.AccountManager] = 120, [Role.Strategist] = 140, [Role.ContentCreator] = 85, [Role.Designer] = 95,
            [Role.SeoSpecialist] = 110, [Role.AdsSpecialist] = 115, [Role.SocialMediaManager] = 90,
        };
        foreach (var (role, rate) in usd)
        {
            db.Add(new HourlyRate { Role = role, Rate = rate, Currency = "USD" });
            db.Add(new HourlyRate { Role = role, Rate = Math.Round(rate * 0.79m), Currency = "GBP" });
            db.Add(new HourlyRate { Role = role, Rate = Math.Round(rate * 3.67m), Currency = "AED" });
            db.Add(new HourlyRate { Role = role, Rate = Math.Round(rate * 280m / 100m) * 100m, Currency = "PKR" });
        }
    }

    private sealed record SeededProject(Project Project, List<ProjectTask> Tasks);

    private SeededProject Project(Ctx x, ClientAccount client, string name, ProjectType type, ProjectTemplate? template, int startOffset,
        DeliveryDemoData.DemoStaff owner, decimal budgetHours, decimal budgetAmount, DeliveryDemoData.DemoStaff[] members, int overdueLeft,
        ProjectStatus status = ProjectStatus.Active)
    {
        var start = _today.AddDays(startOffset);
        var project = new Project
        {
            ClientAccountId = client.Id, Name = name, Type = type, Currency = client.Currency, Status = status, StartDate = start,
            EndDate = template?.DurationDays is { } d ? start.AddDays(d) : start.AddDays(60), BudgetHours = budgetHours,
            BudgetAmount = budgetAmount, OwnerUserId = x.P(owner).Id, TemplateKey = template?.Key,
            ServiceLines = template?.ServiceLines.ToList() ?? new List<string> { "strategy", "social" },
            Description = template?.Description, CreatedAt = _now.AddDays(startOffset - 3), CreatedByUserId = x.P(DeliveryDemoData.AccountManager).Id,
        };
        x.Db.Add(project);
        foreach (var m in members.Append(owner).Append(DeliveryDemoData.AccountManager).Distinct())
            x.Db.Add(new ProjectMember { ProjectId = project.Id, UserId = x.P(m).Id, AddedAt = project.CreatedAt });
        var tasks = template is null ? new List<ProjectTask>() : ProjectTemplateApplier.Apply(x.Db, project, template, start, x.P(DeliveryDemoData.AccountManager).Id);
        var assignees = members.Append(owner).ToArray();
        var overdue = 0;
        foreach (var (task, i) in tasks.Select((t, i) => (t, i)))
        {
            task.CreatedAt = project.CreatedAt;
            var due = task.DueDate!.Value;
            if (due < _today && overdue < overdueLeft && due >= _today.AddDays(-12))
            {
                task.Status = ProjectTaskStatus.InProgress;
                overdue++;
            }
            else if (due < _today.AddDays(-1))
            {
                task.Status = ProjectTaskStatus.Done;
                task.CompletedAt = due.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);
            }
            else if (due <= _today.AddDays(3)) task.Status = i % 3 == 0 ? ProjectTaskStatus.InReview : ProjectTaskStatus.InProgress;
            else if (due <= _today.AddDays(8) && i % 4 == 0) task.Status = ProjectTaskStatus.Blocked;
            var who = x.P(assignees[i % assignees.Length]).Id;
            task.Priority = i % 5 == 0 ? TaskPriority.High : TaskPriority.Normal;
            x.Db.Add(new TaskAssignee { TaskId = task.Id, UserId = who });
            x.Db.Add(new TaskWatcher { TaskId = task.Id, UserId = who });
        }
        return new SeededProject(project, tasks);
    }

    private ProjectTask AddTask(Ctx x, Project project, string title, DateOnly due, ProjectTaskStatus status, User assignee, bool clientVisible)
    {
        var task = new ProjectTask
        {
            ProjectId = project.Id, ClientAccountId = project.ClientAccountId, Title = title, DueDate = due, Status = status,
            ClientVisible = clientVisible, SortOrder = 1000 * (_rng.Next(1, 50)), CreatedByUserId = project.OwnerUserId,
        };
        x.Db.Add(task);
        x.Db.Add(new TaskAssignee { TaskId = task.Id, UserId = assignee.Id });
        x.Db.Add(new TaskWatcher { TaskId = task.Id, UserId = assignee.Id });
        return task;
    }

    // ------------------------------------------------------------------ deliverables (every state)

    private async Task<DeliveryFile> FileAsync(Ctx x, Guid clientId, string name, byte[] bytes, string contentType, string ext, Guid by, CancellationToken ct)
    {
        var key = storage.NewKey(_now, ext);
        await storage.WriteAsync(key, bytes, ct);
        _writtenKeys.Add(key);
        var file = new DeliveryFile
        {
            ClientAccountId = clientId, StorageKey = key, ContentType = contentType, SizeBytes = bytes.Length, Sha256 = Normalization.Sha256Hex(bytes),
            OriginalFileName = name, UploadedByUserId = by, CreatedAt = _now.AddDays(-3),
        };
        x.Db.Add(file);
        return file;
    }

    private Deliverable NewDeliverable(Ctx x, SeededProject p, string title, DeliverableType type, DeliveryDemoData.DemoStaff owner,
        DeliveryDemoData.DemoStaff? reviewer, int createdDaysAgo)
    {
        var d = new Deliverable
        {
            ClientAccountId = p.Project.ClientAccountId, ProjectId = p.Project.Id, Title = title, Type = type, OwnerUserId = x.P(owner).Id,
            ReviewerUserId = reviewer is null ? null : x.P(reviewer).Id, CreatedByUserId = x.P(owner).Id, CreatedAt = _now.AddDays(-createdDaysAgo),
            TaskId = p.Tasks.FirstOrDefault(t => t.Labels.Contains(type switch
            {
                DeliverableType.Design or DeliverableType.SocialPostSet => "design",
                DeliverableType.BlogPost => "content",
                DeliverableType.AdCreative => "creative",
                _ => "report",
            }))?.Id,
        };
        x.Db.Add(d);
        return d;
    }

    private void Version(Ctx x, Deliverable d, int number, User by, int daysAgo, DeliveryFile? file = null, string? link = null, string? body = null, string? notes = null)
    {
        x.Db.Add(new DeliverableVersion
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, Number = number, FileId = file?.Id, LinkUrl = link, Body = body,
            Notes = notes, CreatedByUserId = by.Id, CreatedAt = _now.AddDays(-daysAgo),
        });
        d.CurrentVersion = Math.Max(d.CurrentVersion, number);
    }

    private void Review(Ctx x, Deliverable d, int version, ReviewStage stage, ReviewDecision decision, User? by, double daysAgo, string? comment = null)
    {
        x.Db.Add(new DeliverableReview
        {
            DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = version, Stage = stage, Decision = decision, UserId = by?.Id,
            UserName = by?.DisplayName ?? "Automatic approval", Comment = comment, CreatedAt = _now.AddDays(-daysAgo),
        });
        if (comment is not null && decision is ReviewDecision.ChangesRequested or ReviewDecision.InternalChangesRequested or ReviewDecision.Approved)
            x.Db.Add(new DeliverableComment
            {
                DeliverableId = d.Id, ClientAccountId = d.ClientAccountId, VersionNumber = version, AuthorUserId = by!.Id,
                FromClient = stage == ReviewStage.Client, IsInternal = stage == ReviewStage.Internal, Body = comment, CreatedAt = _now.AddDays(-daysAgo),
            });
    }

    private void SendToClient(Deliverable d, int version, double daysAgo, int slaDays)
    {
        d.Status = DeliverableStatus.ClientReview;
        d.LastSentVersion = version;
        d.SentToClientAt = _now.AddDays(-daysAgo);
        d.ClientDueAt = DeliverableWorkflow.DueAt(d.SentToClientAt.Value, slaDays);
    }

    private async Task SeedDeliverablesAsync(Ctx x, SeededProject social, SeededProject seo, SeededProject ads, SeededProject web,
        SeededProject auroraSocial, SeededProject auroraAds, CancellationToken ct)
    {
        var designer = x.P(DeliveryDemoData.Designer);
        var content = x.P(DeliveryDemoData.Content);
        var strategist = x.P(DeliveryDemoData.Strategist);
        var am = x.P(DeliveryDemoData.AccountManager);
        var approver = x.P(DeliveryDemoData.NimbusApprover);
        var owner = x.P(DeliveryDemoData.NimbusOwner);
        var nimbusId = social.Project.ClientAccountId;

        // Published: v1 changes requested → v2 approved → published.
        var feed = NewDeliverable(x, social, "Feed posts — week 1 set", DeliverableType.SocialPostSet, DeliveryDemoData.Social, DeliveryDemoData.AccountManager, 16);
        Version(x, feed, 1, designer, 15, await FileAsync(x, nimbusId, "feed-week1-v1.png", DemoPng.Creative(800, 800, 1), "image/png", ".png", designer.Id, ct), notes: "Four posts, carousel first.");
        Review(x, feed, 1, ReviewStage.Internal, ReviewDecision.Submitted, designer, 15);
        Review(x, feed, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 14.5);
        Review(x, feed, 1, ReviewStage.Client, ReviewDecision.ChangesRequested, approver, 13, "Love the carousel. Can we use the lime accent less on post 3? It clashes with the photo.");
        Version(x, feed, 2, designer, 12, await FileAsync(x, nimbusId, "feed-week1-v2.png", DemoPng.Creative(800, 800, 2), "image/png", ".png", designer.Id, ct), notes: "Toned down lime on post 3.");
        Review(x, feed, 2, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 11.8);
        Review(x, feed, 2, ReviewStage.Client, ReviewDecision.Approved, approver, 11, "Perfect, approved.");
        Review(x, feed, 2, ReviewStage.Internal, ReviewDecision.Published, x.P(DeliveryDemoData.Social), 9);
        feed.Status = DeliverableStatus.Published; feed.LastSentVersion = 2; feed.SentToClientAt = _now.AddDays(-11.8);
        feed.ApprovedAt = _now.AddDays(-11); feed.ApprovedVersion = 2; feed.ApprovedByUserId = approver.Id; feed.PublishedAt = _now.AddDays(-9);

        // Approved (with CSAT).
        var hero = NewDeliverable(x, social, "Hero banner — new workout plans", DeliverableType.Design, DeliveryDemoData.Designer, DeliveryDemoData.AccountManager, 8);
        Version(x, hero, 1, designer, 7, await FileAsync(x, nimbusId, "hero-banner-v1.png", DemoPng.Creative(1200, 628, 3), "image/png", ".png", designer.Id, ct));
        Review(x, hero, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 6.5);
        Review(x, hero, 1, ReviewStage.Client, ReviewDecision.Approved, owner, 5, "Great energy. Approved.");
        hero.Status = DeliverableStatus.Approved; hero.LastSentVersion = 1; hero.SentToClientAt = _now.AddDays(-6.5);
        hero.ApprovedAt = _now.AddDays(-5); hero.ApprovedVersion = 1; hero.ApprovedByUserId = owner.Id;
        x.Db.Add(new ClientFeedback
        {
            ClientAccountId = nimbusId, UserId = owner.Id, Kind = ClientFeedbackKind.Csat, Score = 5, DeliverableId = hero.Id,
            Comment = "Fast turnaround and spot on brand.", DedupeKey = $"csat:{hero.Id}:{owner.Id}", CreatedAt = _now.AddDays(-5),
        });

        // Client review v2 (after changes requested on v1), due tomorrow.
        var reel = NewDeliverable(x, social, "Reel script — Summer Shred challenge", DeliverableType.Copy, DeliveryDemoData.Content, DeliveryDemoData.Strategist, 6);
        Version(x, reel, 1, content, 5, body: "HOOK: \"No gym? No problem.\"\nSCENE 1: Alarm, 6:30am…\nCTA: Start your free 14 days.", notes: "30s script");
        Review(x, reel, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, strategist, 4.5);
        Review(x, reel, 1, ReviewStage.Client, ReviewDecision.ChangesRequested, approver, 3.5, "Hook is great. Please avoid 'shred' in the CTA — legal prefers 'get stronger'.");
        Version(x, reel, 2, content, 3, body: "HOOK: \"No gym? No problem.\"\nSCENE 1: Alarm, 6:30am, living room mat…\nCTA: Get stronger in 10 minutes a day — start your free 14 days.", notes: "CTA updated per legal.");
        Review(x, reel, 2, ReviewStage.Internal, ReviewDecision.InternalApproved, strategist, 2);
        SendToClient(reel, 2, 2, 3);
        reel.ClientDueAt = _now.AddHours(20);

        // Client review v1, overdue (SLA reminders).
        var blog = NewDeliverable(x, seo, "Blog post: 10-minute morning workouts", DeliverableType.BlogPost, DeliveryDemoData.Content, DeliveryDemoData.Seo, 9);
        Version(x, blog, 1, content, 7, link: "https://docs.example.com/nimbus/10-minute-morning-workouts", notes: "1,400 words, target keyword 'morning workout'.");
        Review(x, blog, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, x.P(DeliveryDemoData.Seo), 6);
        SendToClient(blog, 1, 6, 3);

        // Internal review.
        var retarget = NewDeliverable(x, ads, "Retargeting ad creatives (6 sizes)", DeliverableType.AdCreative, DeliveryDemoData.Designer, DeliveryDemoData.Strategist, 3);
        Version(x, retarget, 1, designer, 1, await FileAsync(x, nimbusId, "retargeting-set-v1.png", DemoPng.Creative(1080, 1080, 4), "image/png", ".png", designer.Id, ct), notes: "1080², 1200×628, 1080×1920 and GDN sizes.");
        Review(x, retarget, 1, ReviewStage.Internal, ReviewDecision.Submitted, designer, 1);
        retarget.Status = DeliverableStatus.InternalReview;

        // Draft.
        var landing = NewDeliverable(x, ads, "Landing page — annual plan offer", DeliverableType.LandingPage, DeliveryDemoData.Strategist, null, 2);
        Version(x, landing, 1, strategist, 1, link: "https://staging.nimbusfitness.example/annual-offer", notes: "Staging build, copy still placeholder.");

        // Changes requested.
        var recap = NewDeliverable(x, social, "Monthly highlights video recap", DeliverableType.Video, DeliveryDemoData.Social, DeliveryDemoData.AccountManager, 10);
        Version(x, recap, 1, x.P(DeliveryDemoData.Social), 8, link: "https://vimeo.com/000000000", notes: "45s cut, captions burned in.");
        Review(x, recap, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 7);
        Review(x, recap, 1, ReviewStage.Client, ReviewDecision.ChangesRequested, owner, 5, "Can we swap the music for something less aggressive and add our logo at the end?");
        recap.Status = DeliverableStatus.ChangesRequested; recap.LastSentVersion = 1; recap.SentToClientAt = _now.AddDays(-7);

        // Other clients.
        var wireframes = NewDeliverable(x, web, "Homepage and tour page wireframes", DeliverableType.Design, DeliveryDemoData.Designer, DeliveryDemoData.Strategist, 5);
        Version(x, wireframes, 1, designer, 2, await FileAsync(x, web.Project.ClientAccountId, "wanderly-wireframes-v1.pdf", Pdf("Wanderly — homepage wireframes v1"), "application/pdf", ".pdf", designer.Id, ct));
        Review(x, wireframes, 1, ReviewStage.Internal, ReviewDecision.Submitted, designer, 2);
        wireframes.Status = DeliverableStatus.InternalReview;

        var auroraSet = NewDeliverable(x, auroraSocial, "Ramadan campaign post set", DeliverableType.SocialPostSet, DeliveryDemoData.Social, DeliveryDemoData.AccountManager, 4);
        Version(x, auroraSet, 1, designer, 3, await FileAsync(x, auroraSocial.Project.ClientAccountId, "aurora-ramadan-v1.png", DemoPng.Creative(800, 1000, 5), "image/png", ".png", designer.Id, ct));
        Review(x, auroraSet, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 2);
        SendToClient(auroraSet, 1, 2, 3);

        var auroraAdsCopy = NewDeliverable(x, auroraAds, "Search ad copy — Vitamin C serum", DeliverableType.AdCreative, DeliveryDemoData.Ads, DeliveryDemoData.AccountManager, 12);
        Version(x, auroraAdsCopy, 1, x.P(DeliveryDemoData.Ads), 11, body: "Headline 1: Brighter Skin in 14 Days\nHeadline 2: Clean Vitamin C Serum\nDescription: Free UAE delivery. Dermatologist tested.");
        Review(x, auroraAdsCopy, 1, ReviewStage.Internal, ReviewDecision.InternalApproved, am, 10);
        Review(x, auroraAdsCopy, 1, ReviewStage.Client, ReviewDecision.Approved, x.P(DeliveryDemoData.AuroraOwner), 9);
        auroraAdsCopy.Status = DeliverableStatus.Approved; auroraAdsCopy.LastSentVersion = 1; auroraAdsCopy.SentToClientAt = _now.AddDays(-10);
        auroraAdsCopy.ApprovedAt = _now.AddDays(-9); auroraAdsCopy.ApprovedVersion = 1; auroraAdsCopy.ApprovedByUserId = x.P(DeliveryDemoData.AuroraOwner).Id;
        x.Db.Add(new ClientFeedback
        {
            ClientAccountId = auroraAdsCopy.ClientAccountId, UserId = x.P(DeliveryDemoData.AuroraOwner).Id, Kind = ClientFeedbackKind.Csat, Score = 3,
            DeliverableId = auroraAdsCopy.Id, Comment = "Good, but we needed it two days earlier.",
            DedupeKey = $"csat:{auroraAdsCopy.Id}:{x.P(DeliveryDemoData.AuroraOwner).Id}", CreatedAt = _now.AddDays(-9),
        });
    }

    /// <summary>A tiny valid one-page PDF with a title (for demo deliverables).</summary>
    private static byte[] Pdf(string title)
    {
        var text = title.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var stream = $"BT /F1 24 Tf 72 700 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ------------------------------------------------------------------ time

    private void SeedTime(Ctx x, SeededProject[] projects)
    {
        var workers = new[]
        {
            DeliveryDemoData.AccountManager, DeliveryDemoData.Strategist, DeliveryDemoData.Content, DeliveryDemoData.Designer,
            DeliveryDemoData.Seo, DeliveryDemoData.Ads, DeliveryDemoData.Social,
        };
        var thisWeek = BudgetMath.WeekStart(_today);
        foreach (var w in workers)
        {
            var user = x.P(w);
            var mine = projects.Where(p => x.Db.ChangeTracker.Entries<ProjectMember>()
                .Any(m => m.Entity.ProjectId == p.Project.Id && m.Entity.UserId == user.Id)).ToList();
            if (mine.Count == 0) continue;
            for (var day = _today.AddDays(-20); day <= _today; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                var entries = _rng.Next(2, 4);
                for (var e = 0; e < entries; e++)
                {
                    var p = mine[_rng.Next(mine.Count)];
                    var open = p.Tasks.Where(t => t.DueDate >= day.AddDays(-5)).ToList();
                    x.Db.Add(new TimeEntry
                    {
                        UserId = user.Id, ClientAccountId = p.Project.ClientAccountId, ProjectId = p.Project.Id,
                        TaskId = open.Count > 0 ? open[_rng.Next(open.Count)].Id : null, Date = day,
                        Minutes = 30 * _rng.Next(2, day == _today ? 4 : 7), Billable = _rng.NextDouble() < 0.85,
                        Note = e == 0 ? "Client work" : null, CreatedAt = day.ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc),
                    });
                }
            }
            // Timesheets: two weeks ago approved; last week submitted for content/designer, approved for the rest.
            x.Db.Add(new Timesheet
            {
                UserId = user.Id, WeekStart = thisWeek.AddDays(-14), Status = TimesheetStatus.Approved, SubmittedAt = _now.AddDays(-9),
                DecidedAt = _now.AddDays(-8), DecidedByUserId = x.P(DeliveryDemoData.AccountManager).Id, TotalMinutes = 0,
            });
            var pendingReview = w == DeliveryDemoData.Content || w == DeliveryDemoData.Designer;
            x.Db.Add(new Timesheet
            {
                UserId = user.Id, WeekStart = thisWeek.AddDays(-7), SubmittedAt = _now.AddDays(-2),
                Status = pendingReview ? TimesheetStatus.Submitted : TimesheetStatus.Approved,
                DecidedAt = pendingReview ? null : _now.AddDays(-1), DecidedByUserId = pendingReview ? null : x.P(DeliveryDemoData.AccountManager).Id,
            });
        }
        // Fill timesheet totals from the entries just created.
        foreach (var sheet in x.Db.ChangeTracker.Entries<Timesheet>().Select(e => e.Entity))
            sheet.TotalMinutes = x.Db.ChangeTracker.Entries<TimeEntry>().Select(e => e.Entity)
                .Where(e => e.UserId == sheet.UserId && e.Date >= sheet.WeekStart && e.Date <= sheet.WeekStart.AddDays(6)).Sum(e => e.Minutes);

        // A running timer on the social media manager's dashboard.
        var social = projects.First(p => p.Project.Name.StartsWith("Social content", StringComparison.Ordinal));
        var socialUser = x.P(DeliveryDemoData.Social);
        x.Db.Add(new TimeEntry
        {
            UserId = socialUser.Id, ClientAccountId = social.Project.ClientAccountId, ProjectId = social.Project.Id, Date = _today,
            StartedAt = _now.AddMinutes(-35), RunningUserId = socialUser.Id, Billable = true, Note = "Community management",
        });
    }

    // ------------------------------------------------------------------ briefs, reports, messages, meetings, feedback

    private void SeedBriefs(Ctx x, ClientAccount nimbus, SeededProject seo)
    {
        x.Db.Add(new Brief
        {
            ClientAccountId = nimbus.Id, TemplateKey = "social-content", Title = "New Year 30-day challenge", SubmittedByClient = true,
            SubmittedByUserId = x.P(DeliveryDemoData.NimbusApprover).Id, Deadline = _today.AddDays(25), CreatedAt = _now.AddDays(-1),
            Answers = new()
            {
                new("goal", "Goal", "Drive 5,000 challenge sign-ups and 800 paid conversions in January."),
                new("audience", "Audience", "Busy Priya and Beginner Ben personas; lapsed users from last year."),
                new("key_message", "Key message", "30 days, 10 minutes a day — join the Nimbus New Year challenge."),
                new("channels", "Channels", "Instagram\nTikTok\nYouTube Shorts"),
                new("format", "Format", "Mixed"),
                new("mandatories", "Mandatories", "Include 'Cancel anytime'. No before/after imagery."),
            },
        });
        var converted = new Brief
        {
            ClientAccountId = nimbus.Id, TemplateKey = "blog-content", Title = "Blog: protein myths", SubmittedByClient = true, ProjectId = seo.Project.Id,
            SubmittedByUserId = x.P(DeliveryDemoData.NimbusOwner).Id, Status = BriefStatus.Converted, ConvertedAt = _now.AddDays(-8), CreatedAt = _now.AddDays(-10),
            Answers = new()
            {
                new("goal", "Goal", "Rank for 'how much protein do I need' and link to meal plans."),
                new("audience", "Audience", "Beginners confused by conflicting advice."),
                new("key_message", "Key message", "You probably need less than you think — and it's easy to get."),
                new("topic", "Topic or working title", "5 protein myths, busted"),
            },
            StaffNote = "Added to the SEO retainer as a task.",
        };
        x.Db.Add(converted);
        var task = AddTask(x, seo.Project, "Write: 5 protein myths, busted", _today.AddDays(6), ProjectTaskStatus.InProgress, x.P(DeliveryDemoData.Content), true);
        task.BriefId = converted.Id;
    }

    private static ReportKpi K(string key, string label, decimal? value, string? unit, decimal? prev, string source, KpiMeasurement m, string? note = null) =>
        new(key, label, value, unit, prev, source, m, note);

    private void SeedReports(Ctx x, ClientAccount nimbus, ClientAccount wanderly, ClientAccount aurora)
    {
        var last = new DateOnly(_today.Year, _today.Month, 1).AddMonths(-1);
        var sections = new List<ReportSection>
        {
            new("summary", "summary", "Executive summary",
                "Organic traffic grew **14%** month on month, driven by the two new workout guides. Paid app installs beat target by 6% at a lower cost per install. Social reach was steady; the carousel format continues to outperform single images.", new()),
            new("delivery", "kpis", "Delivery this month", null, new()
            {
                K("deliverables_approved", "Deliverables approved", 7, null, 5, "Optimize All delivery records", KpiMeasurement.Measured),
                K("tasks_completed", "Tasks completed", 31, null, 27, "Optimize All delivery records", KpiMeasurement.Measured),
                K("hours_delivered", "Hours delivered", 118.5m, "h", 104m, "Optimize All delivery records", KpiMeasurement.Measured),
            }, "delivery"),
            new("seo", "channel", "SEO", "Two new guides reached page one for 'morning workout' and 'home workout plan'.", new()
            {
                K("organic_sessions", "Organic sessions", 18420, null, 16150, "Google Analytics 4", KpiMeasurement.Measured),
                K("avg_position", "Average position (tracked keywords)", 11.4m, null, 13.1m, "Google Search Console", KpiMeasurement.Measured),
                K("organic_signups", "Sign-ups from organic", 612, null, 540, "Google Analytics 4", KpiMeasurement.Measured),
            }, "seo"),
            new("social", "channel", "Social media", "Carousels drove 2.3× the saves of single-image posts.", new()
            {
                K("followers_gained", "Followers gained", 1240, null, 1010, "Instagram Insights", KpiMeasurement.Measured),
                K("reach", "Reach", 214000, null, 198000, "Meta Business Suite (modelled reach)", KpiMeasurement.Estimated, "Meta reports reach as an estimate."),
                K("engagement_rate", "Engagement rate", 4.8m, "%", 4.5m, "Instagram Insights", KpiMeasurement.Measured),
            }, "social"),
            new("ads", "channel", "Paid advertising", null, new()
            {
                K("installs", "App installs", 3180, null, 2710, "Google Ads", KpiMeasurement.Measured),
                K("cpi", "Cost per install", 2.14m, "USD", 2.41m, "Google Ads", KpiMeasurement.Measured),
                K("attributed_revenue", "Attributed first-month revenue", 24300, "USD", null, "Google Ads + App Store (modelled)", KpiMeasurement.Estimated),
            }, "ads"),
            new("email", "channel", "Email marketing", null, new()
            {
                K("open_rate", "Newsletter open rate", 41, "%", 39, "Klaviyo export (entered manually)", KpiMeasurement.Manual),
            }, "email", "No data source is connected for this section yet. Enter figures manually (they will be labelled Manual)."),
            new("wins", "wins", "Wins", "- 'Morning workout' guide now ranks #6\n- Lowest CPI since launch\n- 5/5 CSAT on the hero banner", new()),
            new("plan", "plan", "Next month's plan", "1. Launch the New Year challenge content\n2. Test TikTok Spark Ads\n3. Two more pillar guides", new()),
        };
        x.Db.Add(new ClientReport
        {
            ClientAccountId = nimbus.Id, PeriodStart = last, PeriodEnd = last.AddMonths(1).AddDays(-1), Status = ReportStatus.Published,
            Title = $"Nimbus Fitness — {last:MMMM yyyy} performance report", TemplateKey = ReportService.DefaultTemplateKey, Sections = sections,
            PublishedAt = _now.AddHours(-20), PublishedByUserId = x.P(DeliveryDemoData.AccountManager).Id,
            CreatedByUserId = x.P(DeliveryDemoData.AccountManager).Id, AutoKey = $"{nimbus.Id}:{last:yyyy-MM}",
        });
        foreach (var c in new[] { wanderly, aurora })
            x.Db.Add(new ClientReport
            {
                ClientAccountId = c.Id, PeriodStart = last, PeriodEnd = last.AddMonths(1).AddDays(-1), Status = ReportStatus.Draft,
                Title = $"{c.Name} — {last:MMMM yyyy} performance report", TemplateKey = ReportService.DefaultTemplateKey, AutoKey = $"{c.Id}:{last:yyyy-MM}",
                Sections = new()
                {
                    new("summary", "summary", "Executive summary", null, new()),
                    new("delivery", "kpis", "Delivery this month", null, new(), "delivery", "Refresh to load delivery data."),
                    new("wins", "wins", "Wins", null, new()),
                    new("plan", "plan", "Next month's plan", null, new()),
                },
            });
    }

    private async Task SeedMessagesAsync(Ctx x, ClientAccount nimbus, ClientAccount wanderly, CancellationToken ct)
    {
        var am = x.P(DeliveryDemoData.AccountManager);
        var owner = x.P(DeliveryDemoData.NimbusOwner);
        var approver = x.P(DeliveryDemoData.NimbusApprover);
        void Thread(ClientAccount c, string subject, params (User By, bool FromClient, string Body, double HoursAgo, Guid? File)[] messages)
        {
            var t = new MessageThread { ClientAccountId = c.Id, Subject = subject, CreatedByUserId = messages[0].By.Id, CreatedAt = _now.AddHours(-messages[0].HoursAgo) };
            foreach (var m in messages)
                x.Db.Add(new ThreadMessage
                {
                    ThreadId = t.Id, ClientAccountId = c.Id, AuthorUserId = m.By.Id, FromClient = m.FromClient, Body = m.Body,
                    AttachmentFileIds = m.File is { } f ? new List<Guid> { f } : new List<Guid>(), CreatedAt = _now.AddHours(-m.HoursAgo),
                });
            var last = messages[^1];
            t.LastMessageAt = _now.AddHours(-last.HoursAgo);
            t.MessageCount = messages.Length;
            t.LastAuthorUserId = last.By.Id;
            t.LastMessagePreview = last.Body.Length > 140 ? last.Body[..140] + "…" : last.Body;
            x.Db.Add(t);
            foreach (var reader in messages.Select(m => m.By).DistinctBy(u => u.Id))
                x.Db.Add(new ThreadReadState { ThreadId = t.Id, UserId = reader.Id, LastReadAt = _now.AddHours(-messages.Where(m => m.By.Id == reader.Id).Min(m => m.HoursAgo)) });
        }
        var screenshot = await FileAsync(x, nimbus.Id, "app-store-connect-access.png", DemoPng.Screenshot(7), "image/png", ".png", owner.Id, ct);
        Thread(nimbus, "Summer campaign timeline",
            (owner, true, "Hi Amira — can we bring the Summer Shred launch forward a week? Our app update ships on the 3rd.", 50, null),
            (am, false, "Yes, that works. We'll move the content set review to Monday and launch ads on the 4th. Updated plan in the project.", 46, null),
            (owner, true, "Brilliant, thank you!", 30, null));
        Thread(nimbus, "Access to App Store Connect",
            (approver, true, "I've added marketing@ as an App Manager — screenshot attached.", 8, screenshot.Id),
            (am, false, "Got it, thanks Taylor. Ads will pick up install events from tomorrow.", 3, null));
        Thread(wanderly, "Homepage copy direction",
            (am, false, "Sharing two headline directions for the new homepage ahead of the wireframe review.", 20, null));
    }

    private void SeedMeetings(Ctx x, ClientAccount nimbus, ClientAccount aurora, ClientAccount karachi, SeededProject nimbusSocial)
    {
        var am = x.P(DeliveryDemoData.AccountManager);
        var today15 = _today.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);
        x.Db.Add(new Meeting
        {
            ClientAccountId = nimbus.Id, ProjectId = nimbusSocial.Project.Id, Title = "Monthly performance review", Kind = MeetingKind.MonthlyReview,
            StartsAt = today15, DurationMinutes = 45, Location = "Google Meet", CreatedByUserId = am.Id,
            Agenda = "1. Last month's report\n2. New Year challenge brief\n3. Budget for Q1",
            AttendeeUserIds = new() { am.Id, x.P(DeliveryDemoData.Strategist).Id, x.P(DeliveryDemoData.NimbusOwner).Id, x.P(DeliveryDemoData.NimbusApprover).Id },
        });
        var converted = AddTask(x, nimbusSocial.Project, "Share brand photography guidelines with the designers", _today.AddDays(-50), ProjectTaskStatus.Done, am, false);
        converted.CompletedAt = _now.AddDays(-52);
        x.Db.Add(new Meeting
        {
            ClientAccountId = nimbus.Id, Title = "Kickoff", Kind = MeetingKind.Kickoff, StartsAt = _now.AddDays(-60), DurationMinutes = 90, Status = MeetingStatus.Held,
            CreatedByUserId = am.Id, Location = "Nimbus HQ, Brooklyn", Agenda = "Goals, KPIs, approvals, access",
            Notes = "Agreed KPIs: installs, CPI, organic sign-ups. Approvals by Taylor (content) and Jordan (budget).",
            AttendeeUserIds = new() { am.Id, x.P(DeliveryDemoData.NimbusOwner).Id },
            ActionItems = new()
            {
                new(Guid.NewGuid(), "Share brand photography guidelines with the designers", am.Id, _today.AddDays(-50), converted.Id),
                new(Guid.NewGuid(), "Set up conversion tracking for trial starts", x.P(DeliveryDemoData.Ads).Id, _today.AddDays(-45), null),
            },
        });
        x.Db.Add(new Meeting
        {
            ClientAccountId = aurora.Id, Title = "Creative review — Ramadan set", Kind = MeetingKind.Creative, StartsAt = today15.AddDays(1), DurationMinutes = 30,
            Location = "Zoom", CreatedByUserId = am.Id, AttendeeUserIds = new() { am.Id, x.P(DeliveryDemoData.AuroraOwner).Id },
        });
        x.Db.Add(new Meeting
        {
            ClientAccountId = karachi.Id, Title = "Kickoff call", Kind = MeetingKind.Kickoff, StartsAt = today15.AddDays(3).AddHours(-5), DurationMinutes = 60,
            Location = "Karachi Eats head office, Clifton", CreatedByUserId = am.Id, Agenda = "Goals, locations, delivery platforms, brand assets",
            AttendeeUserIds = new() { am.Id, x.P(DeliveryDemoData.Strategist).Id },
        });
    }

    private void SeedFeedback(Ctx x, ClientAccount nimbus, ClientAccount aurora)
    {
        var lastQuarter = _now.AddMonths(-3);
        var period = ClientRelationshipService.Quarter(lastQuarter);
        var owner = x.P(DeliveryDemoData.NimbusOwner);
        x.Db.Add(new ClientFeedback
        {
            ClientAccountId = nimbus.Id, UserId = owner.Id, Kind = ClientFeedbackKind.Nps, Score = 9, Period = period,
            Comment = "Proactive team, clear reporting.", DedupeKey = $"nps:{period}:{nimbus.Id}:{owner.Id}", CreatedAt = lastQuarter,
        });
        var auroraOwner = x.P(DeliveryDemoData.AuroraOwner);
        x.Db.Add(new ClientFeedback
        {
            ClientAccountId = aurora.Id, UserId = auroraOwner.Id, Kind = ClientFeedbackKind.Nps, Score = 7, Period = period,
            Comment = "Good results; turnaround could be faster.", DedupeKey = $"nps:{period}:{aurora.Id}:{auroraOwner.Id}", CreatedAt = lastQuarter,
        });
    }
}
