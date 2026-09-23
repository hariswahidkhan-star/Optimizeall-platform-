using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Seed;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>
/// Creates the canonical demo people and client accounts of <see cref="DeliveryDemoData"/> when they are missing: staff and
/// client users (by email), the four client accounts with their onboarding checklist and brand kit (by slug), the client
/// memberships and the account teams. Every "Demo" seeder that needs these records calls <see cref="EnsureAsync"/>, so the
/// data is identical whichever seeder runs first. Stages its changes; the caller saves. STAGING / DEMO DATA ONLY.
/// </summary>
public sealed class DeliveryDemoAccounts
{
    private readonly IPasswordHasher<User> hasher;
    private readonly DateTime _now;

    private DeliveryDemoAccounts(IPasswordHasher<User> hasher, DateTime now)
    {
        this.hasher = hasher;
        _now = now;
    }

    /// <summary>People by email (staff, client users and the demo admin when present) and client accounts by slug.</summary>
    public sealed record Result(Dictionary<string, User> People, Dictionary<string, ClientAccount> Clients);

    public static async Task<Result> EnsureAsync(AppDbContext db, IPasswordHasher<User> hasher, DateTime now, CancellationToken ct)
    {
        var accounts = new DeliveryDemoAccounts(hasher, now);
        var people = await accounts.EnsureUsersAsync(db, ct);
        var clients = await accounts.EnsureClientsAsync(db, people, ct);
        return new Result(people, clients);
    }

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
}
